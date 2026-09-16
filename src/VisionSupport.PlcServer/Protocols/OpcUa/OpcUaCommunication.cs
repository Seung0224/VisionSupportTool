using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using VirtualPlcServer.Core;
using VirtualPlcServer.Protocols.Common;

namespace VirtualPlcServer.Protocols.OpcUa
{
    /// <summary>
    /// OPC UA 서버(내장 StandardServer)를 구동하는 통신 계층. NodeMap의 값 변경을 실시간으로 OPC UA 노드에 반영한다.
    /// </summary>
    public sealed class OpcUaCommunication : ICommunication
    {
        private readonly OpcUaServerConfig _config;
        private readonly NodeMap _nodeMap;
        private ApplicationInstance _application;
        private OpcUaHostServer _server;

        public OpcUaCommunication(OpcUaServerConfig config, NodeMap nodeMap)
        {
            _config = config;
            _nodeMap = nodeMap;
            _nodeMap.NodeAdded += OnNodeAdded;
            _nodeMap.NodeRemoved += OnNodeRemoved;
            _nodeMap.ValueChanged += OnNodeValueChanged;
        }

        public bool IsRunning { get; private set; }

        public event EventHandler<string> StatusChanged;

        public event EventHandler<Exception> ErrorOccurred;

        /// <summary>클라이언트가 실제로 접속할 때 쓸 전체 NodeId 문자열 (예: ns=2;s=NodeName). 서버 시작 전이면 null.</summary>
        public string GetResolvedNodeIdString(string name)
        {
            return _server?.NodeManager?.GetResolvedNodeIdString(name);
        }

        public async Task StartAsync()
        {
            if (IsRunning)
            {
                return;
            }

            try
            {
                ApplicationConfiguration configuration = BuildConfiguration();

                _application = new ApplicationInstance
                {
                    ApplicationName = _config.ApplicationName,
                    ApplicationType = ApplicationType.Server,
                    ApplicationConfiguration = configuration
                };

                // BaseAddresses/ApplicationUri에 들어있는 "localhost"를 실제 머신 호스트명으로 치환한다.
                // 그대로 두면 다른 PC의 클라이언트가 GetEndpoints까지는 성공해도, 서버가 돌려주는
                // EndpointUrl이 "localhost"라서 그 이후 세션 연결이 클라이언트 자신에게(127.0.0.1) 되어 실패한다.
                ApplicationInstance.FixupAppConfig(configuration);

                await configuration.ValidateAsync(ApplicationType.Server).ConfigureAwait(false);
                configuration.CertificateValidator.CertificateValidation += (sender, e) => { e.Accept = true; };

                bool haveCertificate;
                try
                {
                    haveCertificate = await _application.CheckApplicationInstanceCertificatesAsync(false).ConfigureAwait(false);
                }
                catch
                {
                    // ApplicationUri(호스트명)가 바뀌는 등의 이유로 이전에 저장된 인증서와 맞지 않아
                    // 접근/검증에 실패하는 경우, 저장된 인증서를 지우고 새로 만들어 한 번 더 시도한다.
                    DeleteOwnCertificateFiles(configuration);
                    haveCertificate = await _application.CheckApplicationInstanceCertificatesAsync(false).ConfigureAwait(false);
                }

                if (!haveCertificate)
                {
                    throw new InvalidOperationException("OPC UA 서버 인증서를 생성/확인하지 못했습니다.");
                }

                _server = new OpcUaHostServer(_nodeMap);
                await _application.StartAsync(_server).ConfigureAwait(false);

                IsRunning = true;
                string resolvedAddress = configuration.ServerConfiguration.BaseAddresses.Count > 0
                    ? configuration.ServerConfiguration.BaseAddresses[0]
                    : $"opc.tcp://localhost:{_config.Port}/{_config.ApplicationName}";
                StatusChanged?.Invoke(this, $"OPC UA 서버 시작됨 ({resolvedAddress})");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (!IsRunning)
            {
                return;
            }

            if (_server != null)
            {
                await _server.StopAsync().ConfigureAwait(false);
            }

            IsRunning = false;
            StatusChanged?.Invoke(this, "OPC UA 서버 정지됨");
        }

        private void OnNodeAdded(object sender, NodeDefinition definition)
        {
            _server?.NodeManager?.CreateVariableNode(definition);
        }

        private void OnNodeRemoved(object sender, string name)
        {
            _server?.NodeManager?.RemoveVariableNode(name);
        }

        private void OnNodeValueChanged(object sender, MapValueChangedEventArgs e)
        {
            _server?.NodeManager?.UpdateVariableValue(e.Key, e.Value);
        }

        private static void DeleteOwnCertificateFiles(ApplicationConfiguration configuration)
        {
            try
            {
                string storePath = configuration.SecurityConfiguration.ApplicationCertificate.StorePath;
                if (string.IsNullOrEmpty(storePath) || !Directory.Exists(storePath))
                {
                    return;
                }

                foreach (string file in Directory.GetFiles(storePath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private ApplicationConfiguration BuildConfiguration()
        {
            string baseDirectory = Path.Combine(
                @"D:\Datas", "VisionSupport", "VirtualPlcServer", "OpcUa");

            var configuration = new ApplicationConfiguration
            {
                ApplicationName = _config.ApplicationName,
                ApplicationUri = "urn:localhost:" + _config.ApplicationName,
                ProductUri = "urn:VirtualPlcServer",
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(baseDirectory, "pki", "own"),
                        SubjectName = "CN=" + _config.ApplicationName + ", DC=localhost"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(baseDirectory, "pki", "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(baseDirectory, "pki", "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(baseDirectory, "pki", "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true,
                    MinimumCertificateKeySize = 1024
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 600000,
                    MaxStringLength = 1048576,
                    MaxByteStringLength = 1048576,
                    MaxArrayLength = 65535,
                    MaxMessageSize = 4194304,
                    MaxBufferSize = 65535,
                    ChannelLifetime = 300000,
                    SecurityTokenLifetime = 3600000
                },
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = new StringCollection
                    {
                        "opc.tcp://localhost:" + _config.Port + "/" + _config.ApplicationName
                    },
                    SecurityPolicies = new ServerSecurityPolicyCollection
                    {
                        new ServerSecurityPolicy
                        {
                            SecurityMode = MessageSecurityMode.None,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    },
                    UserTokenPolicies = new UserTokenPolicyCollection
                    {
                        new UserTokenPolicy(UserTokenType.Anonymous)
                    },
                    MaxSessionCount = 100,
                    MinSessionTimeout = 10000,
                    MaxSessionTimeout = 3600000,
                    MaxRequestAge = 600000,
                    MinPublishingInterval = 100,
                    MaxPublishingInterval = 3600000,
                    MaxSubscriptionLifetime = 3600000,
                    MaxMessageQueueSize = 100,
                    MaxNotificationQueueSize = 100,
                    MaxNotificationsPerPublish = 1000,
                    MinMetadataSamplingInterval = 1000,
                    MaxPublishRequestCount = 20,
                    MaxSubscriptionCount = 100,
                    MaxEventQueueSize = 10000
                },
                TraceConfiguration = new TraceConfiguration
                {
                    OutputFilePath = Path.Combine(baseDirectory, "Logs", "VirtualPlcServer.log.txt"),
                    DeleteOnLoad = true
                }
            };

            return configuration;
        }

        private sealed class OpcUaHostServer : StandardServer
        {
            private readonly NodeMap _nodeMap;

            public OpcUaHostServer(NodeMap nodeMap)
            {
                _nodeMap = nodeMap;
            }

            public OpcUaNodeManager NodeManager { get; private set; }

            protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            {
                NodeManager = new OpcUaNodeManager(server, configuration, _nodeMap);
                var nodeManagers = new List<INodeManager> { NodeManager };
                return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
            }
        }
    }
}
