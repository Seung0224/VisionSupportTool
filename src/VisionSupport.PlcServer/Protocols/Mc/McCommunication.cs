using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VirtualPlcServer.Core;

namespace VirtualPlcServer.Protocols.Mc
{
    /// <summary>
    /// MC프로토콜(SLMP) 3E프레임 바이너리 통신을 처리하는 UDP 서버.
    /// Read(0x0401)와 Write(0x1401) 요청을 서로 다른 UDP 포트에서 받는 클라이언트(예: 로컬 Read/Write 포트를
    /// 분리해 UdpClient를 각각 여는 구현)와 호환되도록, 포트를 두 개(ReadPort/WritePort)로 분리해서 수신한다.
    /// 커맨드 0x0401(일괄읽기)/0x1401(일괄쓰기), 서브커맨드 0x0000(워드단위), 디바이스 D(0xA8)만 지원한다.
    /// </summary>
    public sealed class McCommunication : ICommunication
    {
        private const byte DeviceCodeD = 0xA8;
        private const ushort CommandBatchRead = 0x0401;
        private const ushort CommandBatchWrite = 0x1401;
        private const ushort SubCommandWordUnits = 0x0000;

        private readonly McServerConfig _config;
        private readonly McMap _map;

        private UdpClient _readClient;
        private UdpClient _writeClient;
        private CancellationTokenSource _cts;
        private Task _readLoopTask;
        private Task _writeLoopTask;

        public McCommunication(McServerConfig config, McMap map)
        {
            _config = config;
            _map = map;
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

            _readClient = new UdpClient(new IPEndPoint(_config.ListenAddress, _config.ReadPort));
            _writeClient = new UdpClient(new IPEndPoint(_config.ListenAddress, _config.WritePort));

            IsRunning = true;
            StatusChanged?.Invoke(this, $"MC 서버 시작됨 (UDP Read={_config.ReadPort}, Write={_config.WritePort})");

            _readLoopTask = ReceiveLoopAsync(_readClient, _cts.Token);
            _writeLoopTask = ReceiveLoopAsync(_writeClient, _cts.Token);

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
            _readClient.Close();
            _writeClient.Close();

            try
            {
                await Task.WhenAll(_readLoopTask, _writeLoopTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            StatusChanged?.Invoke(this, "MC 서버 정지됨");
        }

        private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                try
                {
                    byte[] datagram = result.Buffer;
                    if (datagram.Length < 9)
                    {
                        continue;
                    }

                    byte[] header = new byte[9];
                    Array.Copy(datagram, header, 9);
                    int bodyLength = header[7] | (header[8] << 8);

                    if (datagram.Length < 9 + bodyLength)
                    {
                        continue;
                    }

                    byte[] body = new byte[bodyLength];
                    Array.Copy(datagram, 9, body, 0, bodyLength);

                    byte[] response = BuildResponse(header, body);
                    await client.SendAsync(response, response.Length, result.RemoteEndPoint).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }

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

            bool supported = deviceCode == DeviceCodeD && subCommand == SubCommandWordUnits;

            if (!supported)
            {
                endCode = 0xFFFF;
            }
            else if (command == CommandBatchRead)
            {
                // 요청 범위가 설정된 D영역을 벗어나도, 항상 요청한 길이(points*2바이트)만큼
                // 데이터를 채워 응답한다(범위 밖은 0으로 채움). end code를 검사하지 않고
                // 고정 오프셋에서 바로 데이터를 읽는 클라이언트(예: 이 프로젝트의 실제 연동 대상)가
                // 짧은 에러 프레임 때문에 파싱 중 예외를 일으키는 것을 막기 위함이다.
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
                    // 범위 일부가 설정된 D영역을 벗어나도, 겹치는 부분만 반영하고 나머지는 무시한다.
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
