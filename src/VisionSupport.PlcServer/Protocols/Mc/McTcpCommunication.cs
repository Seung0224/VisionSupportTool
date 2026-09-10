using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Mc
{
    /// <summary>
    /// MC프로토콜(SLMP) 3E프레임 바이너리 통신을 처리하는 TCP 서버 - LS산전 PLC의 "MC프로토콜 호환모드"
    /// (TCP, 지속 연결) 대상. 실제 연동 대상(VO_Bio1FInspector의 VO_MCProtoclTCP.cpp)을 직접 읽어서
    /// 확인한 프레임 포맷을 그대로 구현했다: 요청은 21바이트 헤더(+쓰기 시 데이터)이고, "요청 데이터 길이"
    /// (오프셋 7~8)만큼을 헤더 뒤에서 추가로 읽으면 프레임 하나가 끝난다 - UDP와 달리 TCP는 스트림이라
    /// 메시지 경계가 없으므로 이 길이 필드를 보고 정확히 그만큼만 읽어야 한다(ReadExactAsync).
    ///
    /// 커맨드 0x0401(일괄읽기)/0x1401(일괄쓰기), 서브커맨드 0x0000(워드단위)만 지원하고, 디바이스는
    /// D(0xA8)/R(0xAF)/W(0xB4) 중 설정된 워드 디바이스 하나만 지원한다(대상 클라이언트가 실제 쓰는 건
    /// 'R' 뿐이다 - 비트 디바이스 B/M/X/Y는 이 클라이언트가 아예 쓰지 않아서 별도 비트맵 구조까지는
    /// 만들지 않았다). 기존 UDP MC 서버(McCommunication)와 마찬가지로 Read/Write를 서로 다른 포트에서
    /// 받는 클라이언트와 호환되도록 포트 두 개를 받되, 둘이 같으면 리스너 하나만 띄운다(TCP는 어차피
    /// 한 포트로 다중 접속을 받을 수 있고, 프레임 자체에 명령 종류가 들어있어 포트로 구분할 필요가 없다).
    /// </summary>
    public sealed class McTcpCommunication : ICommunication
    {
        private const ushort CommandBatchRead = 0x0401;
        private const ushort CommandBatchWrite = 0x1401;
        private const ushort SubCommandWordUnits = 0x0000;

        private readonly McTcpServerConfig _config;
        private readonly McMap _map;
        private readonly byte _deviceCode;

        private readonly List<TcpListener> _listeners = new List<TcpListener>();
        private readonly List<TcpClient> _activeClients = new List<TcpClient>();
        private readonly object _clientsLock = new object();
        private readonly List<Task> _acceptTasks = new List<Task>();
        private CancellationTokenSource _cts;

        public McTcpCommunication(McTcpServerConfig config, McMap map)
        {
            _config = config;
            _map = map;
            _deviceCode = DeviceCodeFor(config.Device);
        }

        public bool IsRunning { get; private set; }

        public event EventHandler<string> StatusChanged;

        public event EventHandler<Exception> ErrorOccurred;

        public Task StartAsync()
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            _cts = new CancellationTokenSource();

            var readListener = new TcpListener(_config.ListenAddress, _config.ReadPort);
            readListener.Start();
            _listeners.Add(readListener);

            if (_config.WritePort != _config.ReadPort)
            {
                var writeListener = new TcpListener(_config.ListenAddress, _config.WritePort);
                writeListener.Start();
                _listeners.Add(writeListener);
            }

            IsRunning = true;
            StatusChanged?.Invoke(this,
                $"MC(TCP/LS) 서버 시작됨 (Read={_config.ReadPort}, Write={_config.WritePort}, Device={_config.Device})");

            foreach (TcpListener listener in _listeners)
            {
                _acceptTasks.Add(AcceptLoopAsync(listener, _cts.Token));
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            _cts.Cancel();

            foreach (TcpListener listener in _listeners)
            {
                listener.Stop();
            }

            lock (_clientsLock)
            {
                foreach (TcpClient client in _activeClients)
                {
                    try
                    {
                        client.Close();
                    }
                    catch (Exception)
                    {
                        // 종료 중 소켓 정리 실패는 무시한다.
                    }
                }

                _activeClients.Clear();
            }

            // .NET Framework(net48)에서는 TcpListener.Stop()을 호출해도 그 순간 대기 중이던
            // AcceptTcpClientAsync()가 항상 곧바로 예외로 풀려나며 끝난다는 보장이 없다(최신 .NET
            // Core와 달리 이 API엔 취소 토큰도 없다) - 그래서 무기한 대기하면 앱 종료 자체가 멈춰버릴
            // 수 있다(실제로 재현됨: 창을 닫아도 8초+ 응답은 하지만 안 닫힘). 그래서 일정 시간만
            // 기다리고, 그래도 안 끝나면 정리를 포기하고 진행한다 - 어차피 프로세스가 종료되면
            // 남은 배경 태스크도 함께 정리된다.
            Task waitTask = Task.WhenAll(_acceptTasks);
            Task completed = await Task.WhenAny(waitTask, Task.Delay(2000)).ConfigureAwait(false);

            if (completed != waitTask)
            {
                StatusChanged?.Invoke(this, "MC(TCP/LS) 서버: 접속 수신 태스크가 제때 끝나지 않아 정리를 포기하고 계속 진행합니다.");
            }
            else
            {
                try
                {
                    await waitTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SocketException)
                {
                }
            }

            _listeners.Clear();
            _acceptTasks.Clear();

            StatusChanged?.Invoke(this, "MC(TCP/LS) 서버 정지됨");
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                lock (_clientsLock)
                {
                    _activeClients.Add(client);
                }

                // 접속 하나당 독립적인 요청/응답 루프 - Fire-and-forget이지만, StopAsync가 소켓을 직접
                // Close()하므로 대기 중이던 ReadAsync가 예외로 즉시 풀려나 finally에서 정리된다.
                _ = HandleClientAsync(client, cancel);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancel)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] headerBuf = new byte[9];

                while (!cancel.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, headerBuf, 9, cancel).ConfigureAwait(false))
                    {
                        break; // 클라이언트가 연결을 닫음
                    }

                    int bodyLength = headerBuf[7] | (headerBuf[8] << 8);
                    byte[] body = bodyLength > 0 ? new byte[bodyLength] : Array.Empty<byte>();
                    if (bodyLength > 0 && !await ReadExactAsync(stream, body, bodyLength, cancel).ConfigureAwait(false))
                    {
                        break;
                    }

                    byte[] response = BuildResponse(headerBuf, body);
                    await stream.WriteAsync(response, 0, response.Length, cancel).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
            finally
            {
                lock (_clientsLock)
                {
                    _activeClients.Remove(client);
                }

                try
                {
                    client.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>정확히 count바이트를 채울 때까지 반복해서 읽는다(TCP는 스트림이라 한 번의 ReadAsync가
        /// 요청한 만큼 다 채워준다는 보장이 없다). 상대가 연결을 닫으면(0바이트 수신) false.</summary>
        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken cancel)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer, read, count - read, cancel).ConfigureAwait(false);
                if (n == 0)
                {
                    return false;
                }

                read += n;
            }

            return true;
        }

        private static byte DeviceCodeFor(char device)
        {
            switch (char.ToUpperInvariant(device))
            {
                case 'D': return 0xA8;
                case 'W': return 0xB4;
                case 'R':
                default: return 0xAF;
            }
        }

        /// <summary>header는 항상 9바이트(서브헤더+네트워크No+PC No+대상모듈IO No+대상모듈스테이션No+
        /// 요청데이터길이), body는 그 뒤로 이어지는 "요청 데이터 길이"만큼(CPU감시타이머+커맨드+
        /// 서브커맨드+디바이스번호+디바이스코드+개수(+쓰기 시 실제 데이터)) - 기존 UDP MC 서버
        /// (McCommunication.BuildResponse)와 프레임 파싱 로직 자체는 동일하고, 지원 디바이스 코드만
        /// 설정값(_deviceCode)을 쓰도록 다르다.</summary>
        private byte[] BuildResponse(byte[] header, byte[] body)
        {
            byte networkNo = header[2];
            byte pcNo = header[3];
            byte destIoLo = header[4];
            byte destIoHi = header[5];
            byte destStation = header[6];

            ushort command = (ushort)(body[2] | (body[3] << 8));
            ushort subCommand = (ushort)(body[4] | (body[5] << 8));
            int deviceHead = body[6] | (body[7] << 8) | (body[8] << 16);
            byte deviceCode = body[9];
            ushort points = (ushort)(body[10] | (body[11] << 8));

            ushort endCode = 0;
            byte[] readData = Array.Empty<byte>();

            bool supported = deviceCode == _deviceCode && subCommand == SubCommandWordUnits;

            if (!supported)
            {
                endCode = 0xFFFF;
            }
            else if (command == CommandBatchRead)
            {
                // 요청 범위가 설정된 영역을 벗어나도, 항상 요청한 길이(points*2바이트)만큼 데이터를
                // 채워 응답한다(범위 밖은 0으로 채움) - end code를 검사하지 않고 고정 오프셋에서 바로
                // 데이터를 읽는 클라이언트가 짧은 에러 프레임 때문에 파싱 중 예외를 일으키는 걸 막는다.
                readData = new byte[points * 2];
                for (int i = 0; i < points; i++)
                {
                    ushort word = _map.IsInRange(deviceHead + i, 1) ? _map.ReadWord(deviceHead + i) : (ushort)0;
                    readData[i * 2] = (byte)(word & 0xFF);
                    readData[i * 2 + 1] = (byte)(word >> 8);
                }

                if (!_map.IsInRange(deviceHead, points))
                {
                    endCode = 0x4031;
                }
            }
            else if (command == CommandBatchWrite)
            {
                if (body.Length >= 12 + points * 2)
                {
                    // 범위 일부가 설정된 영역을 벗어나도, 겹치는 부분만 반영하고 나머지는 무시한다.
                    for (int i = 0; i < points; i++)
                    {
                        int address = deviceHead + i;
                        if (!_map.IsInRange(address, 1))
                        {
                            continue;
                        }

                        int off = 12 + i * 2;
                        ushort word = (ushort)(body[off] | (body[off + 1] << 8));
                        _map.WriteWord(address, word);
                    }

                    if (!_map.IsInRange(deviceHead, points))
                    {
                        endCode = 0x4031;
                    }
                }
                else
                {
                    endCode = 0x4031;
                }
            }
            else
            {
                endCode = 0xFFFF;
            }

            ushort responseDataLength = (ushort)(2 + readData.Length);
            byte[] response = new byte[9 + responseDataLength];
            response[0] = 0xD0;
            response[1] = 0x00;
            response[2] = networkNo;
            response[3] = pcNo;
            response[4] = destIoLo;
            response[5] = destIoHi;
            response[6] = destStation;
            response[7] = (byte)(responseDataLength & 0xFF);
            response[8] = (byte)(responseDataLength >> 8);
            response[9] = (byte)(endCode & 0xFF);
            response[10] = (byte)(endCode >> 8);
            Array.Copy(readData, 0, response, 11, readData.Length);

            return response;
        }
    }
}
