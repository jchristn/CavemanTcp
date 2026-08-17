namespace CavemanTcp
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Runtime.InteropServices;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// CavemanTcp is a simple TCP client and server providing callers with easy integration and full control over network reads and writes.
    /// Set the ClientConnected and ClientDisconnected callbacks, then, use Start() to begin listening for connections.
    /// </summary>
    public class CavemanTcpServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Indicates if the server is listening for connections.
        /// </summary>
        public bool IsListening
        {
            get
            {
                return _IsListening;
            }
        }

        /// <summary>
        /// Method to invoke when sending log messages.
        /// </summary>
        public Action<string> Logger = null;

        /// <summary>
        /// CavemanTcp server settings.
        /// </summary>
        public CavemanTcpServerSettings Settings
        {
            get
            {
                return _Settings;
            }
            set
            {
                if (value == null) _Settings = new CavemanTcpServerSettings();
                else _Settings = value;
            }
        }

        /// <summary>
        /// CavemanTcp server events.
        /// </summary>
        public CavemanTcpServerEvents Events
        {
            get
            {
                return _Events;
            }
            set
            {
                if (value == null) _Events = new CavemanTcpServerEvents();
                else _Events = value;
            }
        }

        /// <summary>
        /// CavemanTcp server callbacks.
        /// </summary>
        public CavemanTcpServerCallbacks Callbacks
        {
            get
            {
                return _Callbacks;
            }
            set
            {
                if (value == null) _Callbacks = new CavemanTcpServerCallbacks();
                else _Callbacks = value;
            }
        }

        /// <summary>
        /// CavemanTcp statistics.
        /// </summary>
        public CavemanTcpStatistics Statistics
        {
            get
            {
                return _Statistics;
            }
        }

        /// <summary>
        /// CavemanTcp keepalive settings.
        /// </summary>
        public CavemanTcpKeepaliveSettings Keepalive
        {
            get
            {
                return _Keepalive;
            }
            set
            {
                if (value == null) _Keepalive = new CavemanTcpKeepaliveSettings();
                else _Keepalive = value;
            }
        }

        #endregion

        #region Private-Members

        private CavemanTcpServerSettings _Settings = new CavemanTcpServerSettings();
        private CavemanTcpServerEvents _Events = new CavemanTcpServerEvents();
        private CavemanTcpServerCallbacks _Callbacks = new CavemanTcpServerCallbacks();
        private CavemanTcpKeepaliveSettings _Keepalive = new CavemanTcpKeepaliveSettings();
        private CavemanTcpStatistics _Statistics = new CavemanTcpStatistics();

        private string _Header = "[CavemanTcp.Server] ";
        private bool _IsListening = false;
        private string _ListenerIp;
        private IPAddress _IPAddress;
        private int _Port;
        private bool _Ssl;
        private string _PfxCertFilename;
        private string _PfxPassword;

        private X509Certificate2 _SslCertificate = null;
        private X509Certificate2Collection _SslCertificateCollection = null;

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;
        private CancellationTokenRegistration _ExternalTokenRegistration;
        private TcpListener _Listener;
        private Task _AcceptConnections = null;
        private static readonly TimeSpan _DefaultConnectionMonitorInterval = TimeSpan.FromMilliseconds(250);

        private readonly object _ClientsLock = new object();
        private Dictionary<Guid, ClientMetadata> _Clients = new Dictionary<Guid, ClientMetadata>();
        private Dictionary<string, Guid> _ClientIdsByIpPort = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region tructors-and-Factories

        /// <summary>
        /// Instantiates the TCP server without SSL.  Set the ClientConnected, ClientDisconnected, and DataReceived callbacks.  Once set, use Start() to begin listening for connections.
        /// </summary>
        /// <param name="ipPort">The IP:port of the server.</param> 
        public CavemanTcpServer(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));

            Common.ParseIpPort(ipPort, out _ListenerIp, out _Port);
            if (_Port < 0) throw new ArgumentException("Port must be zero or greater.");

            if (!IPAddress.TryParse(_ListenerIp, out _IPAddress))
            {
                _IPAddress = Dns.GetHostEntry(_ListenerIp).AddressList[0];
                _ListenerIp = _IPAddress.ToString();
            }

            if (String.IsNullOrEmpty(_ListenerIp))
            {
                _IPAddress = IPAddress.Loopback;
                _ListenerIp = _IPAddress.ToString();
            }
            else if (_ListenerIp == "*" || _ListenerIp == "+")
            {
                _IPAddress = IPAddress.Any;
            }
            else
            {
                if (!IPAddress.TryParse(_ListenerIp, out _IPAddress))
                {
                    _IPAddress = Dns.GetHostEntry(_ListenerIp).AddressList[0];
                    _ListenerIp = _IPAddress.ToString();
                }
            }

            _IsListening = false;

            _Header = "[CavemanTcp.Server " + _ListenerIp + ":" + _Port + "] ";
        }

        /// <summary>
        /// Instantiates the TCP server without SSL.  Set the ClientConnected, ClientDisconnected, and DataReceived callbacks.  Once set, use Start() to begin listening for connections.
        /// </summary>
        /// <param name="listenerIp">The listener IP address or hostname.</param>
        /// <param name="port">The TCP port on which to listen.</param> 
        /// <param name="certificate">SSL certificate.</param>
        public CavemanTcpServer(string listenerIp, int port, X509Certificate2 certificate = null)
        {
            if (String.IsNullOrEmpty(listenerIp)) throw new ArgumentNullException(nameof(listenerIp));
            if (port < 0) throw new ArgumentException("Port must be zero or greater.");

            if (String.IsNullOrEmpty(listenerIp))
            {
                _IPAddress = IPAddress.Loopback;
                _ListenerIp = _IPAddress.ToString();
            }
            else if (listenerIp == "*" || listenerIp == "+")
            {
                _IPAddress = IPAddress.Any;
                _ListenerIp = listenerIp;
            }
            else
            {
                if (!IPAddress.TryParse(listenerIp, out _IPAddress))
                {
                    _IPAddress = Dns.GetHostEntry(listenerIp).AddressList[0];
                }

                _ListenerIp = listenerIp;
            }

            _Port = port;
            _IsListening = false;

            if (certificate != null)
            {
                _Ssl = true;
                _SslCertificate = certificate;
                _SslCertificateCollection = new X509Certificate2Collection { _SslCertificate };
            }

            _Header = "[CavemanTcp.Server " + _ListenerIp + ":" + _Port + "] ";
        }

        /// <summary>
        /// Instantiates the TCP server.  Set the ClientConnected, ClientDisconnected, and DataReceived callbacks.  Once set, use Start() to begin listening for connections.
        /// </summary>
        /// <param name="ipPort">The IP:port of the server.</param> 
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="pfxCertFilename">The filename of the PFX certificate file.</param>
        /// <param name="pfxPassword">The password to the PFX certificate file.</param>
        public CavemanTcpServer(string ipPort, bool ssl, string pfxCertFilename, string pfxPassword)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));

            Common.ParseIpPort(ipPort, out _ListenerIp, out _Port);
            if (_Port < 0) throw new ArgumentException("Port must be zero or greater.");

            if (!IPAddress.TryParse(_ListenerIp, out _IPAddress))
            {
                _IPAddress = Dns.GetHostEntry(_ListenerIp).AddressList[0];
                _ListenerIp = _IPAddress.ToString();
            }

            if (String.IsNullOrEmpty(_ListenerIp))
            {
                _IPAddress = IPAddress.Loopback;
                _ListenerIp = _IPAddress.ToString();
            }
            else if (_ListenerIp == "*" || _ListenerIp == "+")
            {
                _IPAddress = IPAddress.Any;
            }
            else
            {
                if (!IPAddress.TryParse(_ListenerIp, out _IPAddress))
                {
                    _IPAddress = Dns.GetHostEntry(_ListenerIp).AddressList[0];
                    _ListenerIp = _IPAddress.ToString();
                }
            }

            _Ssl = ssl;
            _PfxCertFilename = pfxCertFilename;
            _PfxPassword = pfxPassword;
            _IsListening = false;

            if (_Ssl)
            {
                _SslCertificate = LoadCertificateFromFile(pfxCertFilename, pfxPassword);

                _SslCertificateCollection = new X509Certificate2Collection
                {
                    _SslCertificate
                };
            }

            _Header = "[CavemanTcp.Server " + _ListenerIp + ":" + _Port + "] ";
        }

        /// <summary>
        /// Instantiates the TCP server.  Set the ClientConnected, ClientDisconnected, and DataReceived callbacks.  Once set, use Start() to begin listening for connections.
        /// </summary>
        /// <param name="listenerIp">The listener IP address or hostname.</param>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="pfxCertFilename">The filename of the PFX certificate file.</param>
        /// <param name="pfxPassword">The password to the PFX certificate file.</param>
        public CavemanTcpServer(string listenerIp, int port, bool ssl, string pfxCertFilename, string pfxPassword)
        {
            if (String.IsNullOrEmpty(listenerIp)) throw new ArgumentNullException(nameof(listenerIp));
            if (port < 0) throw new ArgumentException("Port must be zero or greater.");

            if (String.IsNullOrEmpty(listenerIp))
            {
                _IPAddress = IPAddress.Loopback;
                _ListenerIp = _IPAddress.ToString();
            }
            else if (listenerIp == "*" || listenerIp == "+")
            {
                _IPAddress = IPAddress.Any;
                _ListenerIp = listenerIp;
            }
            else
            {
                if (!IPAddress.TryParse(listenerIp, out _IPAddress))
                {
                    _IPAddress = Dns.GetHostEntry(listenerIp).AddressList[0];
                }

                _ListenerIp = listenerIp;
            }

            _Port = port;
            _Ssl = ssl;
            _PfxCertFilename = pfxCertFilename;
            _PfxPassword = pfxPassword;
            _IsListening = false;

            if (_Ssl)
            {
                _SslCertificate = LoadCertificateFromFile(pfxCertFilename, pfxPassword);

                _SslCertificateCollection = new X509Certificate2Collection
                {
                    _SslCertificate
                };
            }

            _Header = "[CavemanTcp.Server " + _ListenerIp + ":" + _Port + "] ";
        }

        #endregion

        #region Public-Methods

        #region General

        /// <summary>
        /// Dispose of the TCP server.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Start accepting connections.
        /// </summary>
        public void Start()
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationStart,
                _Ssl,
                ActivityKind.Server);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                if (_IsListening) throw new InvalidOperationException("CavemanTcpServer is already running.");

                _Listener = new TcpListener(_IPAddress, _Port);
                _Listener.Start();
                _ExternalTokenRegistration.Dispose();
                _ExternalTokenRegistration = default;

                _TokenSource = new CancellationTokenSource();
                _Token = _TokenSource.Token;

                _Statistics = new CavemanTcpStatistics();
                _AcceptConnections = Task.Run(() => AcceptConnections(), _Token);

                Logger?.Invoke(_Header + "started");
            }
            catch (Exception e)
            {
                telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationStart, e, telemetryActivity);
                throw;
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationStart,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        /// <summary>
        /// Start accepting connections.
        /// </summary>
        /// <param name="token">Cancellation token for canceling the server.</param>
        /// <returns>Task.</returns>
        public Task StartAsync(CancellationToken token = default)
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationStartAsync,
                _Ssl,
                ActivityKind.Server);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                if (_IsListening) throw new InvalidOperationException("CavemanTcpServer is already running.");

                _Listener = new TcpListener(_IPAddress, _Port);
                _Listener.Start();
                _ExternalTokenRegistration.Dispose();
                _ExternalTokenRegistration = default;

                if (token != default(CancellationToken))
                {
                    _TokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                    _Token = _TokenSource.Token;
                    _ExternalTokenRegistration = token.Register(() =>
                    {
                        try
                        {
                            _Listener?.Stop();
                        }
                        catch
                        {
                        }
                    });
                }
                else
                {
                    _TokenSource = new CancellationTokenSource();
                    _Token = _TokenSource.Token;
                }

                _Statistics = new CavemanTcpStatistics();
                _AcceptConnections = Task.Run(() => AcceptConnections(), _Token);

                Logger?.Invoke(_Header + "started");
                return _AcceptConnections; // sets _IsListening
            }
            catch (Exception e)
            {
                telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationStartAsync, e, telemetryActivity);
                throw;
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationStartAsync,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        /// <summary>
        /// Stop accepting new connections.
        /// </summary>
        public void Stop()
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationStop,
                _Ssl,
                ActivityKind.Server);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                if (!_IsListening) throw new InvalidOperationException("CavemanTcpServer is not running.");

                _IsListening = false;
                _Listener.Stop();
                _TokenSource.Cancel();
                _ExternalTokenRegistration.Dispose();
                _ExternalTokenRegistration = default;

                Logger?.Invoke(_Header + "stopped");
            }
            catch (Exception e)
            {
                telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationStop, e, telemetryActivity);
                throw;
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationStop,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        /// <summary>
        /// Retrieve a list of client metadata for clients connected to the server.
        /// </summary>
        /// <returns>IEnumerable of ClientMetadata.</returns>
        public IEnumerable<ClientMetadata> GetClients()
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationGetClients,
                _Ssl,
                ActivityKind.Internal);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                return GetClientList();
            }
            catch (Exception e)
            {
                telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationGetClients, e, telemetryActivity);
                throw;
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationGetClients,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        #endregion

        #region Send

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="data">String containing data to send.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public WriteResult Send(string ipPort, string data)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return Send(ipPort, bytes);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <param name="data">String containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(Guid guid, string data)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return Send(guid, bytes);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public WriteResult Send(string ipPort, byte[] data)
        {
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            Guid guid = ClientGuidFromIpPort(ipPort);
            return SendBytesWithoutTimeoutInternal(guid, data);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(Guid guid, byte[] data)
        {
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            return SendBytesWithoutTimeoutInternal(guid, data);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public WriteResult Send(string ipPort, long contentLength, Stream stream)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithoutTimeoutInternal(guid, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(Guid guid, long contentLength, Stream stream)
        {
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithoutTimeoutInternal(guid, contentLength, stream);
        }

        #endregion

        #region SendWithTimeout

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="data">String containing data to send.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public WriteResult SendWithTimeout(int timeoutMs, string ipPort, string data)
        {
            byte[] bytes = Array.Empty<byte>();
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return SendWithTimeout(timeoutMs, ipPort, bytes);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="guid">Client GUID.</param>
        /// <param name="data">String containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, Guid guid, string data)
        {
            byte[] bytes = Array.Empty<byte>();
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return SendWithTimeout(timeoutMs, guid, bytes);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public WriteResult SendWithTimeout(int timeoutMs, string ipPort, byte[] data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            Guid guid = ClientGuidFromIpPort(ipPort);
            return SendBytesWithTimeoutInternal(timeoutMs, guid, data);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="guid">Client GUID.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, Guid guid, byte[] data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            return SendBytesWithTimeoutInternal(timeoutMs, guid, data);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public WriteResult SendWithTimeout(int timeoutMs, string ipPort, long contentLength, Stream stream)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithTimeoutInternal(timeoutMs, guid, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="guid">Client GUID.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, Guid guid, long contentLength, Stream stream)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithTimeoutInternal(timeoutMs, guid, contentLength, stream);
        }

        #endregion

        #region SendAsync

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="data">String containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public async Task<WriteResult> SendAsync(string ipPort, string data, CancellationToken token = default)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendAsync(ipPort, bytes, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <param name="data">String containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(Guid guid, string data, CancellationToken token = default)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendAsync(guid, bytes, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public async Task<WriteResult> SendAsync(string ipPort, byte[] data, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            Guid guid = ClientGuidFromIpPort(ipPort);
            return await SendBytesWithoutTimeoutInternalAsync(guid, data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(Guid guid, byte[] data, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            return await SendBytesWithoutTimeoutInternalAsync(guid, data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public async Task<WriteResult> SendAsync(string ipPort, long contentLength, Stream stream, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendWithoutTimeoutInternalAsync(guid, contentLength, stream, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(Guid guid, long contentLength, Stream stream, CancellationToken token = default)
        {
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendWithoutTimeoutInternalAsync(guid, contentLength, stream, token).ConfigureAwait(false);
        }

        #endregion

        #region SendWithTimeoutAsync

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="data">String containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, string data, CancellationToken token = default)
        {
            byte[] bytes = Array.Empty<byte>();
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendWithTimeoutAsync(timeoutMs, ipPort, bytes, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="guid">Client GUID.</param>
        /// <param name="data">String containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, Guid guid, string data, CancellationToken token = default)
        {
            byte[] bytes = Array.Empty<byte>();
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendWithTimeoutAsync(timeoutMs, guid, bytes, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, byte[] data, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            Guid guid = ClientGuidFromIpPort(ipPort);
            return await SendBytesWithTimeoutInternalAsync(timeoutMs, guid, data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="guid">Client GUID.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, Guid guid, byte[] data, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            return await SendBytesWithTimeoutInternalAsync(timeoutMs, guid, data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, long contentLength, Stream stream, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendWithTimeoutInternalAsync(timeoutMs, guid, contentLength, stream, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by GUID.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="guid">Client GUID.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, Guid guid, long contentLength, Stream stream, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendWithTimeoutInternalAsync(timeoutMs, guid, contentLength, stream, token).ConfigureAwait(false);
        }

        #endregion

        #region Disconnect

        /// <summary>
        /// Disconnects the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the client.</param>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public void DisconnectClient(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            DisconnectClient(guid);
        }

        /// <summary>
        /// Disconnects the specified client.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        public void DisconnectClient(Guid guid)
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationDisconnect,
                _Ssl,
                ActivityKind.Internal);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                ClientMetadata client = GetClient(guid);
                if (client == null)
                {
                    telemetryStatus = CavemanTcpInstrumentation.StatusClientNotFound;
                    return;
                }

                CavemanTcpInstrumentation.SetClient(telemetryActivity, client);
                _Events.HandleClientDisconnected(this, new ClientDisconnectedEventArgs(client, DisconnectReason.Kicked));

                RemoveAndDisposeClient(guid, DisconnectReason.Kicked);
            }
            catch (Exception e)
            {
                telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationDisconnect, e, telemetryActivity);
                throw;
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationDisconnect,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public ReadResult Read(string ipPort, int count)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return ReadWithoutTimeoutInternal(guid, (long)count);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult Read(Guid guid, int count)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return ReadWithoutTimeoutInternal(guid, (long)count);
        }

        #endregion

        #region ReadWithTimeout

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public ReadResult ReadWithTimeout(int timeoutMs, string ipPort, int count)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return ReadWithTimeoutInternal(timeoutMs, guid, (long)count);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="guid">Client GUID.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult ReadWithTimeout(int timeoutMs, Guid guid, int count)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return ReadWithTimeoutInternal(timeoutMs, guid, (long)count);
        }

        #endregion

        #region ReadAsync

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public async Task<ReadResult> ReadAsync(string ipPort, int count, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return await ReadWithoutTimeoutInternalAsync(guid, (long)count, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadAsync(Guid guid, int count, CancellationToken token = default)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return await ReadWithoutTimeoutInternalAsync(guid, (long)count, token).ConfigureAwait(false);
        }

        #endregion

        #region ReadWithTimeoutAsync

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public async Task<ReadResult> ReadWithTimeoutAsync(int timeoutMs, string ipPort, int count, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return await ReadWithTimeoutInternalAsync(timeoutMs, guid, (long)count, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="guid">Client GUID.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadWithTimeoutAsync(int timeoutMs, Guid guid, int count, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return await ReadWithTimeoutInternalAsync(timeoutMs, guid, (long)count, token).ConfigureAwait(false);
        }

        #endregion

        #region Other

        /// <summary>
        /// Get direct access to the underlying client stream.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <returns>Stream.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public Stream GetStream(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            return GetStream(guid);
        }

        /// <summary>
        /// Get direct access to the underlying client stream.
        /// </summary>
        /// <param name="guid">Client GUID.</param>
        /// <returns>Stream.</returns>
        public Stream GetStream(Guid guid)
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationGetStream,
                _Ssl,
                ActivityKind.Internal);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                ClientMetadata client = GetClient(guid);
                if (client == null)
                {
                    telemetryStatus = CavemanTcpInstrumentation.StatusClientNotFound;
                    throw new KeyNotFoundException("Client with GUID " + guid.ToString() + " not found.");
                }

                CavemanTcpInstrumentation.SetClient(telemetryActivity, client);
                return GetClientTransportStream(client);
            }
            catch (Exception e)
            {
                if (!String.Equals(telemetryStatus, CavemanTcpInstrumentation.StatusClientNotFound, StringComparison.Ordinal))
                {
                    telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                }

                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationGetStream, e, telemetryActivity);
                throw;
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationGetStream,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        /// <summary>
        /// Determines if a client is connected by its IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port.</param>
        /// <returns>True if connected.</returns>
        [Obsolete("Please migrate to methods that use a Guid to identify the recipient.")]
        public bool IsConnected(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            Guid guid = ClientGuidFromIpPort(ipPort);
            return IsConnected(guid);
        }

        /// <summary>
        /// Determines if a client is connected by its GUID.
        /// </summary>
        /// <param name="guid">The client GUID.</param>
        /// <returns>True if connected.</returns>
        public bool IsConnected(Guid guid)
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationIsConnected,
                _Ssl,
                ActivityKind.Internal);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                bool exists = ClientExists(guid);
                if (!exists) telemetryStatus = CavemanTcpInstrumentation.StatusClientNotFound;
                return exists;
            }
            catch (Exception e)
            {
                telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationIsConnected, e, telemetryActivity);
                throw;
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationIsConnected,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        #endregion

        #endregion

        #region Private-Methods

        private static X509Certificate2 LoadCertificateFromFile(string filename, string password)
        {
#if NET9_0_OR_GREATER
            if (String.IsNullOrEmpty(password))
                return X509CertificateLoader.LoadPkcs12FromFile(filename, null);
            else
                return X509CertificateLoader.LoadPkcs12FromFile(filename, password);
#else
#pragma warning disable SYSLIB0057
            if (String.IsNullOrEmpty(password))
                return new X509Certificate2(filename);
            else
                return new X509Certificate2(filename, password);
#pragma warning restore SYSLIB0057
#endif
        }

        /// <summary>
        /// Dispose of the TCP server.
        /// </summary>
        /// <param name="disposing">Dispose of resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
                Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationDispose,
                    _Ssl,
                    ActivityKind.Internal);
                CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
                string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

                Logger?.Invoke(_Header + "disposing");

                try
                {
                    lock (_ClientsLock)
                    {
                        if (_Clients != null && _Clients.Count > 0)
                        {
                            foreach (KeyValuePair<Guid, ClientMetadata> curr in _Clients)
                            {
                                curr.Value.Dispose();
                                CavemanTcpInstrumentation.RecordConnectionClosed(CavemanTcpInstrumentation.RoleServer, _Ssl, DisconnectReason.Normal);
                                Logger?.Invoke(_Header + "disconnected client " + curr.Value.ToString());
                            }

                            _Clients.Clear();
                            _ClientIdsByIpPort.Clear();
                        }
                    }

                    if (_TokenSource != null)
                    {
                        if (!_TokenSource.IsCancellationRequested)
                        {
                            _TokenSource.Cancel();
                            _TokenSource.Dispose();
                        }
                    }

                    _ExternalTokenRegistration.Dispose();
                    _ExternalTokenRegistration = default;

                    if (_Listener != null && _Listener.Server != null)
                    {
                        _Listener.Server.Close();
                        _Listener.Server.Dispose();
                    }

                    if (_Listener != null)
                    {
                        _Listener.Stop();
                    }
                }
                catch (Exception e)
                {
                    telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                    CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationDispose, e, telemetryActivity);
                    Logger?.Invoke(_Header + "dispose exception:" +
                        Environment.NewLine +
                        e.ToString() +
                        Environment.NewLine);
                    _Events.HandleExceptionEncountered(this, e);
                }
                finally
                {
                    _Events.ClearAllEventHandlers();
                    CavemanTcpInstrumentation.RecordOperation(
                        CavemanTcpInstrumentation.RoleServer,
                        CavemanTcpInstrumentation.OperationDispose,
                        telemetryStatus,
                        telemetryStart);
                    CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                    if (telemetryActivity != null) telemetryActivity.Dispose();
                }

                _IsListening = false;

                Logger?.Invoke(_Header + "disposed");
            }
        }

        private void EnableKeepalives(TcpClient client)
        {
            // issues with definitions: https://github.com/dotnet/sdk/issues/14540

            try
            {
#if NETCOREAPP3_1_OR_GREATER || NET6_0_OR_GREATER

                // NETCOREAPP3_1_OR_GREATER catches .NET 5.0

                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);

                // Windows 10 version 1703 or later

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    && Environment.OSVersion.Version >= new Version(10, 0, 15063))
                {
                    client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);
                }

#elif NETFRAMEWORK

                byte[] keepAlive = new byte[12];
                Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveTime), 0, keepAlive, 4, 4); 
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveInterval), 0, keepAlive, 8, 4); 
                client.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

#elif NETSTANDARD

#endif
            }
            catch (Exception)
            {
                Logger?.Invoke(_Header + "keepalives not supported on this platform, disabled");
                _Keepalive.EnableTcpKeepAlives = false;
            }
        }

        #region Connection

        private bool IsClientConnected(ClientMetadata client)
        {
            Socket socket = client?.Client?.Client;

            if (socket == null || !client.Client.Connected)
            {
                return false;
            }

            try
            {
                if (socket.Poll(0, SelectMode.SelectError))
                {
                    Logger?.Invoke(_Header + "socket poll reported an error for client " + client.Guid.ToString());
                    return false;
                }

                if (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
                {
                    Logger?.Invoke(_Header + "socket poll detected remote disconnect for client " + client.Guid.ToString());
                    return false;
                }

                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (SocketException se)
            {
                Logger?.Invoke(_Header + "socket exception while checking connectivity for client " + client.Guid.ToString() + ": " + se.Message);
                return false;
            }
        }

        private async Task AcceptConnections()
        {
            _IsListening = true;

            while (!_Token.IsCancellationRequested)
            {
                ClientMetadata client = null;

                #region Check-for-Maximum-Connections

                if (!_IsListening && (GetClientCount() >= _Settings.MaxConnections))
                {
                    await Task.Delay(100, _Token).ConfigureAwait(false);
                    continue;
                }
                else if (!_IsListening)
                {
                    _Listener.Start();
                    _IsListening = true;
                }

                #endregion

                try
                {
                    TcpClient tcpClient = await _Listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    tcpClient.NoDelay = true;

                    string clientIpPort = tcpClient.Client.RemoteEndPoint.ToString();
                    long acceptTelemetryStart = CavemanTcpInstrumentation.GetTimestamp();
                    Activity acceptTelemetryActivity = CavemanTcpInstrumentation.StartActivity(
                        CavemanTcpInstrumentation.RoleServer,
                        CavemanTcpInstrumentation.OperationAccept,
                        _Ssl,
                        ActivityKind.Server);
                    CavemanTcpInstrumentation.SetEndpoint(acceptTelemetryActivity, _ListenerIp, _Port);
                    string acceptTelemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

                    try
                    {
                        client = new ClientMetadata(tcpClient);
                        CavemanTcpInstrumentation.SetClient(acceptTelemetryActivity, client);
                        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Token, client.Token);

                        string clientIp = null;
                        int clientPort = 0;
                        Common.ParseIpPort(clientIpPort, out clientIp, out clientPort);

                        if (!IsClientPermitted(clientIp))
                        {
                            acceptTelemetryStatus = CavemanTcpInstrumentation.StatusNotPermitted;
                            CavemanTcpInstrumentation.RecordConnectionDeclined(acceptTelemetryStatus);
                            Logger?.Invoke($"{_Header}rejecting connection from {clientIp} (not permitted)");
                            _Events.HandleClientDeclined(this, new ClientDeclinedEventArgs(clientIpPort));
                            linkedCts.Dispose();
                            client.Dispose();
                            continue;
                        }

                        if (IsClientBlocked(clientIp))
                        {
                            acceptTelemetryStatus = CavemanTcpInstrumentation.StatusBlocked;
                            CavemanTcpInstrumentation.RecordConnectionDeclined(acceptTelemetryStatus);
                            Logger?.Invoke($"{_Header}rejecting connection from {clientIp} (blocked)");
                            _Events.HandleClientDeclined(this, new ClientDeclinedEventArgs(clientIpPort));
                            linkedCts.Dispose();
                            client.Dispose();
                            continue;
                        }

                        if (!IsClientAuthorized(clientIp, clientPort))
                        {
                            acceptTelemetryStatus = CavemanTcpInstrumentation.StatusAuthorizationRejected;
                            CavemanTcpInstrumentation.RecordConnectionDeclined(acceptTelemetryStatus);
                            Logger?.Invoke($"{_Header}rejecting connection from {clientIpPort} (authorization callback)");
                            _Events.HandleClientDeclined(this, new ClientDeclinedEventArgs(clientIpPort));
                            linkedCts.Dispose();
                            client.Dispose();
                            continue;
                        }

                        AddClient(client);

                        if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives(tcpClient);

                        var _ = HandleClientConnection(client, linkedCts.Token)
                            .ContinueWith(x => linkedCts.Dispose())
                            .ConfigureAwait(false);

                        #region Check-for-Maximum-Connections

                        if (GetClientCount() >= _Settings.MaxConnections)
                        {
                            Logger?.Invoke($"{_Header}maximum connections {_Settings.MaxConnections} met (currently {GetClientCount()} connections), pausing");
                            _IsListening = false;
                            _Listener.Stop();
                        }

                        #endregion
                    }
                    catch (Exception e)
                    {
                        acceptTelemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                        CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationAccept, e, acceptTelemetryActivity);
                        throw;
                    }
                    finally
                    {
                        CavemanTcpInstrumentation.RecordOperation(
                            CavemanTcpInstrumentation.RoleServer,
                            CavemanTcpInstrumentation.OperationAccept,
                            acceptTelemetryStatus,
                            acceptTelemetryStart);
                        CavemanTcpInstrumentation.CompleteActivity(acceptTelemetryActivity, acceptTelemetryStatus);
                        if (acceptTelemetryActivity != null) acceptTelemetryActivity.Dispose();
                    }
                }
                catch (TaskCanceledException)
                {
                    _IsListening = false;
                    if (client != null) RemoveAndDisposeClient(client.Guid);
                    return;
                }
                catch (OperationCanceledException)
                {
                    _IsListening = false;
                    if (client != null) RemoveAndDisposeClient(client.Guid);
                    return;
                }
                catch (ObjectDisposedException)
                {
                    continue;
                }
                catch (Exception e)
                {
                    Logger?.Invoke($"{_Header}exception while awaiting connections: {e.ToString()}");
                    _Events.HandleExceptionEncountered(this, e);
                    continue;
                }
            }

            _IsListening = false;
        }

        private async Task HandleClientConnection(ClientMetadata client, CancellationToken token = default)
        {
            try
            {
                if (_Ssl)
                {
                    if (_Settings.AcceptInvalidCertificates)
                    {
                        client.SslStream = new SslStream(client.NetworkStream, false, new RemoteCertificateValidationCallback(AcceptCertificate));
                    }
                    else
                    {
                        client.SslStream = new SslStream(client.NetworkStream, false);
                    }

                    bool success = await StartTls(client).ConfigureAwait(false);
                    if (!success)
                    {
                        RemoveAndDisposeClient(client.Guid);
                        return;
                    }
                }

                if (_Settings.MonitorClientConnections)
                {
                    Logger?.Invoke(_Header + "starting connection monitor for: " + client.ToString());
                    Task unawaited = Task.Run(() => ConnectionMonitor(client, token), token);
                }

                _Events.HandleClientConnected(this, 
                    new ClientConnectedEventArgs(
                        client,
                        client.Client.Client.LocalEndPoint));
            }
            catch (TaskCanceledException)
            {
                Logger?.Invoke(_Header + "canceled " + client.IpPort);
                client.Dispose();
            }
            catch (OperationCanceledException)
            {
                Logger?.Invoke(_Header + "canceled " + client.IpPort);
                client.Dispose();
            }
        }

        private async Task<bool> StartTls(ClientMetadata client)
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationTls,
                _Ssl,
                ActivityKind.Server);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            CavemanTcpInstrumentation.SetClient(telemetryActivity, client);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                await client.SslStream.AuthenticateAsServerAsync(
                    _SslCertificate,
                    _Settings.MutuallyAuthenticate,
                    Common.GetSslProtocol,
                    !_Settings.AcceptInvalidCertificates).ConfigureAwait(false);

                if (!client.SslStream.IsEncrypted)
                {
                    Logger?.Invoke(_Header + "client " + client.IpPort + " not encrypted, disconnecting");
                    telemetryStatus = CavemanTcpInstrumentation.StatusDeclined;
                    return false;
                }

                if (!client.SslStream.IsAuthenticated)
                {
                    Logger?.Invoke(_Header + "client " + client.IpPort + " not SSL/TLS authenticated, disconnecting");
                    telemetryStatus = CavemanTcpInstrumentation.StatusDeclined;
                    return false;
                }

                if (_Settings.MutuallyAuthenticate && !client.SslStream.IsMutuallyAuthenticated)
                {
                    Logger?.Invoke(_Header + "client " + client.IpPort + " failed mutual authentication, disconnecting");
                    telemetryStatus = CavemanTcpInstrumentation.StatusDeclined;
                    return false;
                }

                return true;
            }
            catch (TaskCanceledException)
            {
                telemetryStatus = CavemanTcpInstrumentation.StatusCanceled;
            }
            catch (OperationCanceledException)
            {
                telemetryStatus = CavemanTcpInstrumentation.StatusCanceled;
            }
            catch (Exception e)
            {
                telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationTls, e, telemetryActivity);
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationTls,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }

            return false;
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return _Settings.AcceptInvalidCertificates;
        }

        private async Task ConnectionMonitor(ClientMetadata client, CancellationToken token)
        {
            try
            {
                while (client != null && !client.Token.IsCancellationRequested)
                {
                    await Task.Delay(_DefaultConnectionMonitorInterval, token).ConfigureAwait(false);
                    if (!IsClientConnected(client)) break;
                }
            }
            catch (SocketException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "client " + client.ToString() + " monitor exception:" + Environment.NewLine + e.ToString());
                _Events.HandleExceptionEncountered(this, e);
            }
            finally
            {
                Logger?.Invoke(_Header + "client " + client.ToString() + " disconnected");
                DisconnectClient(client.Guid);
            }
        }

        #endregion

        #region Send

        private WriteResult SendBytesWithoutTimeoutInternal(Guid guid, byte[] data)
        {
            return SendBytesInternalAsync(guid, data, -1, CancellationToken.None).GetAwaiter().GetResult();
        }

        private WriteResult SendBytesWithTimeoutInternal(int timeoutMs, Guid guid, byte[] data)
        {
            return SendBytesInternalAsync(guid, data, timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        }

        private WriteResult SendWithoutTimeoutInternal(Guid guid, long contentLength, Stream stream)
        {
            return SendStreamInternalAsync(guid, contentLength, stream, -1, CancellationToken.None).GetAwaiter().GetResult();
        }

        private WriteResult SendWithTimeoutInternal(int timeoutMs, Guid guid, long contentLength, Stream stream)
        {
            return SendStreamInternalAsync(guid, contentLength, stream, timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        }

        private Task<WriteResult> SendBytesWithoutTimeoutInternalAsync(Guid guid, byte[] data, CancellationToken token)
        {
            return SendBytesInternalAsync(guid, data, -1, token);
        }

        private Task<WriteResult> SendBytesWithTimeoutInternalAsync(int timeoutMs, Guid guid, byte[] data, CancellationToken token)
        {
            return SendBytesInternalAsync(guid, data, timeoutMs, token);
        }

        private async Task<WriteResult> SendWithoutTimeoutInternalAsync(Guid guid, long contentLength, Stream stream, CancellationToken token)
        {
            return await SendStreamInternalAsync(guid, contentLength, stream, -1, token).ConfigureAwait(false);
        }

        private async Task<WriteResult> SendWithTimeoutInternalAsync(int timeoutMs, Guid guid, long contentLength, Stream stream, CancellationToken token)
        {
            return await SendStreamInternalAsync(guid, contentLength, stream, timeoutMs, token).ConfigureAwait(false);
        }

        private async Task<WriteResult> SendBytesInternalAsync(Guid guid, byte[] data, int timeoutMs, CancellationToken token)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            return await ExecuteWriteAsync(
                guid,
                timeoutMs,
                token,
                async (client, transport, cancellationToken) =>
                {
                    await transport.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                    return data.Length;
                }).ConfigureAwait(false);
        }

        private async Task<WriteResult> SendStreamInternalAsync(Guid guid, long contentLength, Stream stream, int timeoutMs, CancellationToken token)
        {
            return await ExecuteWriteAsync(
                guid,
                timeoutMs,
                token,
                async (client, transport, cancellationToken) =>
                {
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(_Settings.StreamBufferSize);
                    long totalWritten = 0;

                    try
                    {
                        long bytesRemaining = contentLength;

                        while (bytesRemaining > 0)
                        {
                            int readLength = (int)Math.Min(buffer.Length, bytesRemaining);
                            int bytesRead = await stream.ReadAsync(buffer, 0, readLength, cancellationToken).ConfigureAwait(false);
                            if (bytesRead <= 0) throw new EndOfStreamException("Source stream ended before the requested number of bytes could be sent.");

                            await transport.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);

                            totalWritten += bytesRead;
                            bytesRemaining -= bytesRead;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    return totalWritten;
                }).ConfigureAwait(false);
        }

        private async Task<WriteResult> ExecuteWriteAsync(Guid guid, int timeoutMs, CancellationToken callerToken, Func<ClientMetadata, Stream, CancellationToken, Task<long>> writer)
        {
            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationSend,
                _Ssl,
                ActivityKind.Internal);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            CavemanTcpInstrumentation.SetTimeout(telemetryActivity, timeoutMs);

            ClientMetadata client = GetClient(guid);
            if (client != null) CavemanTcpInstrumentation.SetClient(telemetryActivity, client);

            WriteResult result = client == null ?
                new WriteResult(WriteResultStatus.ClientNotFound, 0) :
                new WriteResult(WriteResultStatus.Success, 0);
            bool semaphoreHeld = false;

            try
            {
                if (client == null) return result;

                using (CancellationTokenSource linkedCts = CreateOperationTokenSource(client.Token, callerToken, timeoutMs))
                {
                    try
                    {
                        await client.WriteSemaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                        semaphoreHeld = true;

                        Stream transport = GetClientTransportStream(client);
                        long bytesWritten = await writer(client, transport, linkedCts.Token).ConfigureAwait(false);

                        result.BytesWritten = bytesWritten;
                        _Statistics.AddSentBytes(bytesWritten);
                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        result.Status = GetWriteCancellationStatus(client, timeoutMs, callerToken);
                        if (result.Status == WriteResultStatus.Disconnected) RemoveAndDisposeClient(guid);
                        return result;
                    }
                    catch (Exception e)
                    {
                        result.Status = WriteResultStatus.Disconnected;
                        CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationSend, e, telemetryActivity);
                        RemoveAndDisposeClient(guid);
                        return result;
                    }
                    finally
                    {
                        if (semaphoreHeld) client.WriteSemaphore.Release();
                    }
                }
            }
            finally
            {
                string telemetryStatus = CavemanTcpInstrumentation.ToStatus(result.Status);
                CavemanTcpInstrumentation.RecordWrite(CavemanTcpInstrumentation.RoleServer, telemetryStatus, result.BytesWritten, telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus, result.BytesWritten);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        #endregion

        #region Read

        private ReadResult ReadWithoutTimeoutInternal(Guid guid, long count)
        {
            return ReadInternalAsync(guid, count, -1, CancellationToken.None).GetAwaiter().GetResult();
        }

        private ReadResult ReadWithTimeoutInternal(int timeoutMs, Guid guid, long count)
        {
            return ReadInternalAsync(guid, count, timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        }

        private async Task<ReadResult> ReadWithoutTimeoutInternalAsync(Guid guid, long count, CancellationToken token)
        {
            return await ReadInternalAsync(guid, count, -1, token).ConfigureAwait(false);
        }

        private async Task<ReadResult> ReadWithTimeoutInternalAsync(int timeoutMs, Guid guid, long count, CancellationToken token)
        {
            return await ReadInternalAsync(guid, count, timeoutMs, token).ConfigureAwait(false);
        }

        private async Task<ReadResult> ReadInternalAsync(Guid guid, long count, int timeoutMs, CancellationToken token)
        {
            if (count < 1) return new ReadResult(ReadResultStatus.Success, 0, null);
            if (count > Int32.MaxValue) throw new ArgumentOutOfRangeException(nameof(count), "Count must not exceed Int32.MaxValue.");

            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationRead,
                _Ssl,
                ActivityKind.Internal);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            CavemanTcpInstrumentation.SetTimeout(telemetryActivity, timeoutMs);

            ClientMetadata client = GetClient(guid);
            if (client != null) CavemanTcpInstrumentation.SetClient(telemetryActivity, client);

            ReadResult result = client == null ?
                new ReadResult(ReadResultStatus.ClientNotFound, 0, null) :
                new ReadResult(ReadResultStatus.Success, 0, null);
            bool semaphoreHeld = false;
            int totalCount = (int)count;
            byte[] data = new byte[totalCount];
            int offset = 0;

            try
            {
                if (client == null) return result;

                using (CancellationTokenSource linkedCts = CreateOperationTokenSource(client.Token, token, timeoutMs))
                {
                    try
                    {
                        await client.ReadSemaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                        semaphoreHeld = true;

                        Stream transport = GetClientTransportStream(client);

                        while (offset < totalCount)
                        {
                            int readLength = Math.Min(_Settings.StreamBufferSize, totalCount - offset);
                            int bytesRead = await transport.ReadAsync(data, offset, readLength, linkedCts.Token).ConfigureAwait(false);
                            if (bytesRead <= 0)
                            {
                                RemoveAndDisposeClient(guid);
                                result = CreateReadResult(ReadResultStatus.Disconnected, data, offset);
                                return result;
                            }

                            offset += bytesRead;
                            _Statistics.AddReceivedBytes(bytesRead);
                        }

                        result = CreateReadResult(ReadResultStatus.Success, data, offset);
                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        ReadResultStatus status = GetReadCancellationStatus(client, timeoutMs, token);
                        if (status == ReadResultStatus.Disconnected) RemoveAndDisposeClient(guid);
                        result = CreateReadResult(status, data, offset);
                        return result;
                    }
                    catch (Exception e)
                    {
                        CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationRead, e, telemetryActivity);
                        RemoveAndDisposeClient(guid);
                        result = CreateReadResult(ReadResultStatus.Disconnected, data, offset);
                        return result;
                    }
                    finally
                    {
                        if (semaphoreHeld) client.ReadSemaphore.Release();
                    }
                }
            }
            finally
            {
                string telemetryStatus = CavemanTcpInstrumentation.ToStatus(result.Status);
                CavemanTcpInstrumentation.RecordRead(CavemanTcpInstrumentation.RoleServer, telemetryStatus, result.BytesRead, telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus, result.BytesRead);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        #endregion

        private CancellationTokenSource CreateOperationTokenSource(CancellationToken connectionToken, CancellationToken callerToken, int timeoutMs)
        {
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Token, connectionToken, callerToken);
            if (timeoutMs > 0) linkedCts.CancelAfter(timeoutMs);
            return linkedCts;
        }

        private WriteResultStatus GetWriteCancellationStatus(ClientMetadata client, int timeoutMs, CancellationToken callerToken)
        {
            if (callerToken.IsCancellationRequested) return WriteResultStatus.Canceled;
            if (_Token.IsCancellationRequested || client == null || client.Token.IsCancellationRequested || !ClientExists(client.Guid)) return WriteResultStatus.Disconnected;
            if (timeoutMs > 0) return WriteResultStatus.Timeout;
            return WriteResultStatus.Canceled;
        }

        private ReadResultStatus GetReadCancellationStatus(ClientMetadata client, int timeoutMs, CancellationToken callerToken)
        {
            if (callerToken.IsCancellationRequested) return ReadResultStatus.Canceled;
            if (_Token.IsCancellationRequested || client == null || client.Token.IsCancellationRequested || !ClientExists(client.Guid)) return ReadResultStatus.Disconnected;
            if (timeoutMs > 0) return ReadResultStatus.Timeout;
            return ReadResultStatus.Canceled;
        }

        private Stream GetClientTransportStream(ClientMetadata client)
        {
            if (_Ssl)
            {
                if (client?.SslStream == null) throw new IOException("SSL stream is not connected.");
                return client.SslStream;
            }

            if (client?.NetworkStream == null) throw new IOException("Network stream is not connected.");
            return client.NetworkStream;
        }

        private ReadResult CreateReadResult(ReadResultStatus status, byte[] buffer, int bytesRead)
        {
            if (bytesRead <= 0) return new ReadResult(status, 0, null);

            byte[] data;
            if (buffer != null && bytesRead == buffer.Length)
            {
                data = buffer;
            }
            else
            {
                data = new byte[bytesRead];
                if (buffer != null) Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);
            }

            MemoryStream ms = new MemoryStream(data, 0, bytesRead, false, true);
            return new ReadResult(status, bytesRead, ms, data);
        }

        #region Client

        private IEnumerable<ClientMetadata> GetClientList()
        {
            lock (_ClientsLock)
            {
                return new List<ClientMetadata>(_Clients.Values);
            }
        }

        private Guid ClientGuidFromIpPort(string ipPort)
        {
            lock (_ClientsLock)
            {
                return _ClientIdsByIpPort.TryGetValue(ipPort, out Guid guid) ? guid : Guid.Empty;
            }
        }

        private bool ClientExists(Guid guid)
        {
            lock (_ClientsLock)
            {
                return _Clients.ContainsKey(guid);
            }
        }

        private void RemoveAndDisposeClient(Guid guid)
        {
            RemoveAndDisposeClient(guid, DisconnectReason.Normal);
        }

        private void RemoveAndDisposeClient(Guid guid, DisconnectReason reason)
        {
            ClientMetadata client = null;

            lock (_ClientsLock)
            {
                if (_Clients.TryGetValue(guid, out client))
                {
                    _Clients.Remove(guid);
                    _ClientIdsByIpPort.Remove(client.IpPort);
                }
            }

            if (client != null)
            {
                client.Dispose();
                CavemanTcpInstrumentation.RecordConnectionClosed(CavemanTcpInstrumentation.RoleServer, _Ssl, reason);
                Logger?.Invoke(_Header + "removed: " + client.ToString());
            }
        }

        private Stream GetClientStream(Guid guid)
        {
            lock (_ClientsLock)
            {
                if (!_Clients.TryGetValue(guid, out ClientMetadata client))
                {
                    throw new KeyNotFoundException("Client with GUID " + guid.ToString() + " not found.");
                }

                return GetClientTransportStream(client);
            }
        }

        private void AddClient(ClientMetadata client)
        {
            lock (_ClientsLock)
            {
                Logger?.Invoke(_Header + "adding client " + client.ToString());
                _Clients.Add(client.Guid, client);
                _ClientIdsByIpPort[client.IpPort] = client.Guid;
                CavemanTcpInstrumentation.RecordConnectionOpened(CavemanTcpInstrumentation.RoleServer, _Ssl);
            }
        }

        private ClientMetadata GetClient(Guid guid)
        {
            lock (_ClientsLock)
            {
                return _Clients.TryGetValue(guid, out ClientMetadata client) ? client : null;
            }
        }

        private int GetClientCount()
        {
            lock (_ClientsLock)
            {
                return _Clients.Count;
            }
        }

        private bool IsClientPermitted(string clientIp)
        {
            List<string> permitted = _Settings.PermittedIPs;
            if (permitted == null || permitted.Count < 1) return true;

            for (int i = 0; i < permitted.Count; i++)
            {
                if (String.Equals(permitted[i], clientIp, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsClientBlocked(string clientIp)
        {
            List<string> blocked = _Settings.BlockedIPs;
            if (blocked == null || blocked.Count < 1) return false;

            for (int i = 0; i < blocked.Count; i++)
            {
                if (String.Equals(blocked[i], clientIp, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsClientAuthorized(string clientIp, int clientPort)
        {
            if (_Callbacks?.AuthorizeConnection == null) return true;

            long telemetryStart = CavemanTcpInstrumentation.GetTimestamp();
            Activity telemetryActivity = CavemanTcpInstrumentation.StartActivity(
                CavemanTcpInstrumentation.RoleServer,
                CavemanTcpInstrumentation.OperationAuthorizeConnection,
                _Ssl,
                ActivityKind.Internal);
            CavemanTcpInstrumentation.SetEndpoint(telemetryActivity, _ListenerIp, _Port);
            string telemetryStatus = CavemanTcpInstrumentation.StatusSuccess;

            try
            {
                bool authorized = _Callbacks.AuthorizeConnection(clientIp, clientPort);
                if (!authorized) telemetryStatus = CavemanTcpInstrumentation.StatusAuthorizationRejected;
                return authorized;
            }
            catch (Exception e)
            {
                telemetryStatus = CavemanTcpInstrumentation.ToStatus(e);
                CavemanTcpInstrumentation.RecordException(CavemanTcpInstrumentation.RoleServer, CavemanTcpInstrumentation.OperationAuthorizeConnection, e, telemetryActivity);
                Logger?.Invoke($"{_Header}authorization callback exception for {clientIp}:{clientPort}: {e.Message}");
                _Events.HandleExceptionEncountered(this, e);
                return false;
            }
            finally
            {
                CavemanTcpInstrumentation.RecordOperation(
                    CavemanTcpInstrumentation.RoleServer,
                    CavemanTcpInstrumentation.OperationAuthorizeConnection,
                    telemetryStatus,
                    telemetryStart);
                CavemanTcpInstrumentation.CompleteActivity(telemetryActivity, telemetryStatus);
                if (telemetryActivity != null) telemetryActivity.Dispose();
            }
        }

        #endregion

        #endregion
    }
}
