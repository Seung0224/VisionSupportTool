using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TwinCAT.Ads;
using TwinCAT.Ads.Server;
using TwinCAT.Ads.TcpRouter;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Protocols.Ads
{
    /// <summary>
    /// TwinCAT ADS 서버(Raw IndexGroup/Offset 방식)를 구동하는 통신 계층.
    /// 로컬 AMS 라우터를 앱에 내장하여 별도 TwinCAT 설치 없이 동작한다.
    /// </summary>
    public sealed class AdsCommunication : ICommunication
    {
        private static readonly AmsNetId LocalNetId = new AmsNetId("127.0.0.1.1.1");

        private readonly AdsServerConfig _config;
        private readonly NodeMap _nodeMap;
        private readonly AdsMemoryStore _memoryStore = new AdsMemoryStore();

        private AmsTcpIpRouter _router;
        private AdsHostServer _server;
        private CancellationTokenSource _cts;
        private Task _routerTask;

        public AdsCommunication(AdsServerConfig config, NodeMap nodeMap)
        {
            _config = config;
            _nodeMap = nodeMap;
            _nodeMap.NodeAdded += OnNodeAdded;
            _nodeMap.NodeRemoved += OnNodeRemoved;
            _nodeMap.ValueChanged += OnNodeValueChanged;
            _memoryStore.NodeWritten += (name, value) => _nodeMap.SetValue(name, value);
        }

        public bool IsRunning { get; private set; }

        public event EventHandler<string> StatusChanged;

        public event EventHandler<Exception> ErrorOccurred;

        public IReadOnlyList<AdsNodeMemory> MemoryLayout => _memoryStore.Snapshot();

        public async Task StartAsync()
        {
            if (IsRunning)
            {
                return;
            }

            try
            {
                _cts = new CancellationTokenSource();

                IConfiguration routerConfiguration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["AmsRouter:NetId"] = LocalNetId.ToString()
                    })
                    .Build();

                _router = new AmsTcpIpRouter(routerConfiguration);
                _routerTask = Task.Run(() => _router.StartAsync(_cts.Token));

                _server = new AdsHostServer(_config.AmsPort, _config.PortName, _memoryStore, routerConfiguration);
                await ConnectWithRetryAsync(_cts.Token).ConfigureAwait(false);

                IsRunning = true;
                StatusChanged?.Invoke(this, $"ADS 서버 시작됨 (AMS 포트 {_config.AmsPort}, NetId {LocalNetId})");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
        }

        private async Task ConnectWithRetryAsync(CancellationToken cancel)
        {
            const int maxAttempts = 15;
            Exception lastError = null;

            await Task.Delay(300, cancel).ConfigureAwait(false);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (_routerTask.IsFaulted)
                {
                    throw _routerTask.Exception?.GetBaseException() ?? new InvalidOperationException("ADS 라우터 시작에 실패했습니다.");
                }

                try
                {
                    await _server.ConnectServerAsync(cancel).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await Task.Delay(300, cancel).ConfigureAwait(false);
                }
            }

            throw lastError ?? new InvalidOperationException("ADS 서버를 라우터에 연결하지 못했습니다.");
        }

        public Task StopAsync()
        {
            if (!IsRunning)
            {
                return Task.CompletedTask;
            }

            _server?.Disconnect();
            _server?.Dispose();
            _router?.Stop();
            _cts?.Cancel();

            IsRunning = false;
            StatusChanged?.Invoke(this, "ADS 서버 정지됨");
            return Task.CompletedTask;
        }

        private void OnNodeAdded(object sender, NodeDefinition definition)
        {
            _memoryStore.AddNode(definition);
        }

        private void OnNodeRemoved(object sender, string name)
        {
            _memoryStore.RemoveNode(name);
        }

        private void OnNodeValueChanged(object sender, MapValueChangedEventArgs e)
        {
            _memoryStore.UpdateValue(e.Key, e.Value);
        }

        private sealed class AdsHostServer : AdsServer
        {
            private readonly AdsMemoryStore _memoryStore;

            public AdsHostServer(ushort port, string portName, AdsMemoryStore memoryStore, IConfiguration configuration)
                : base(port, portName, configuration, null)
            {
                _memoryStore = memoryStore;
            }

            protected override Task<ResultReadBytes> OnReadAsync(AmsAddress target, uint invokeId, uint indexGroup, uint indexOffset, int readLength, CancellationToken cancel)
            {
                if (!_memoryStore.TryRead(indexOffset, readLength, out byte[] data))
                {
                    return Task.FromResult(new ResultReadBytes(AdsErrorCode.DeviceInvalidData, ReadOnlyMemory<byte>.Empty));
                }

                return Task.FromResult(new ResultReadBytes(AdsErrorCode.NoError, data));
            }

            protected override Task<ResultWrite> OnWriteAsync(AmsAddress target, uint invokeId, uint indexGroup, uint indexOffset, ReadOnlyMemory<byte> writeData, CancellationToken cancel)
            {
                if (!_memoryStore.TryWrite(indexOffset, writeData.ToArray()))
                {
                    return Task.FromResult(new ResultWrite(AdsErrorCode.DeviceInvalidData));
                }

                return Task.FromResult(new ResultWrite(AdsErrorCode.NoError));
            }

            protected override Task<ResultReadDeviceState> OnReadDeviceStateAsync(AmsAddress target, uint invokeId, CancellationToken cancel)
            {
                var stateInfo = new StateInfo(AdsState.Run, (ushort)0);
                return Task.FromResult(ResultReadDeviceState.CreateSuccess(stateInfo));
            }

            protected override Task<ResultAds> OnWriteControlAsync(AmsAddress target, uint invokeId, AdsState adsState, ushort deviceState, ReadOnlyMemory<byte> data, CancellationToken cancel)
            {
                return Task.FromResult(new ResultAds(AdsErrorCode.NoError));
            }
        }

        /// <summary>Raw ADS 메모리(IndexGroup 고정, IndexOffset 순차 할당) 버퍼.</summary>
        private sealed class AdsMemoryStore
        {
            public const uint IndexGroup = 0x4020;
            private const int BufferSize = 1 << 20;

            private readonly object _lock = new object();
            private readonly byte[] _buffer = new byte[BufferSize];
            private readonly Dictionary<string, AdsNodeMemory> _byName = new Dictionary<string, AdsNodeMemory>();
            private readonly List<AdsNodeMemory> _ordered = new List<AdsNodeMemory>();
            private uint _nextOffset;

            public void AddNode(NodeDefinition definition)
            {
                lock (_lock)
                {
                    if (_byName.ContainsKey(definition.Name))
                    {
                        return;
                    }

                    int length = AdsValueCodec.TotalByteLength(definition.DataType, definition.ArrayLength);
                    if (_nextOffset + length > BufferSize)
                    {
                        throw new InvalidOperationException("ADS 메모리 영역이 부족합니다.");
                    }

                    var memory = new AdsNodeMemory
                    {
                        Name = definition.Name,
                        IndexGroup = IndexGroup,
                        IndexOffset = _nextOffset,
                        ByteLength = length,
                        DataType = definition.DataType,
                        IsArray = definition.IsArray,
                        ArrayLength = definition.ArrayLength
                    };

                    AdsValueCodec.WriteValue(_buffer, (int)_nextOffset, definition);

                    _byName[definition.Name] = memory;
                    _ordered.Add(memory);
                    _nextOffset += (uint)length;
                }
            }

            public void RemoveNode(string name)
            {
                lock (_lock)
                {
                    if (_byName.TryGetValue(name, out AdsNodeMemory memory))
                    {
                        _byName.Remove(name);
                        _ordered.Remove(memory);
                    }
                }
            }

            public void UpdateValue(string name, object value)
            {
                lock (_lock)
                {
                    if (!_byName.TryGetValue(name, out AdsNodeMemory memory))
                    {
                        return;
                    }

                    AdsValueCodec.WriteValue(_buffer, (int)memory.IndexOffset, memory.DataType, memory.IsArray, memory.ArrayLength, value);
                }
            }

            public bool TryRead(uint offset, int length, out byte[] data)
            {
                lock (_lock)
                {
                    if (offset + length > BufferSize || length < 0)
                    {
                        data = null;
                        return false;
                    }

                    data = new byte[length];
                    Array.Copy(_buffer, offset, data, 0, length);
                    return true;
                }
            }

            public bool TryWrite(uint offset, byte[] data)
            {
                lock (_lock)
                {
                    if (offset + data.Length > BufferSize)
                    {
                        return false;
                    }

                    Array.Copy(data, 0, _buffer, offset, data.Length);

                    AdsNodeMemory target = _ordered.Find(m => m.IndexOffset == offset && m.ByteLength == data.Length);
                    if (target != null)
                    {
                        object decoded = AdsValueCodec.ReadValue(_buffer, (int)offset, target.DataType, target.IsArray, target.ArrayLength);
                        NodeWritten?.Invoke(target.Name, decoded);
                    }

                    return true;
                }
            }

            public event Action<string, object> NodeWritten;

            public IReadOnlyList<AdsNodeMemory> Snapshot()
            {
                lock (_lock)
                {
                    return new List<AdsNodeMemory>(_ordered);
                }
            }
        }
    }
}
