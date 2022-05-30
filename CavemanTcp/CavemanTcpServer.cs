using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CavemanTcp
{
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
        /// CavemanTcp server callbacks.
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
        private TcpListener _Listener;
        private Task _AcceptConnections = null;

        private readonly object _ClientsLock = new object();
        private Dictionary<string, ClientMetadata> _Clients = new Dictionary<string, ClientMetadata>();

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
        public CavemanTcpServer(string listenerIp, int port)
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
                if (String.IsNullOrEmpty(pfxPassword))
                {
                    _SslCertificate = new X509Certificate2(pfxCertFilename);
                }
                else
                {
                    _SslCertificate = new X509Certificate2(pfxCertFilename, pfxPassword);
                }

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
                if (String.IsNullOrEmpty(pfxPassword))
                {
                    _SslCertificate = new X509Certificate2(pfxCertFilename);
                }
                else
                {
                    _SslCertificate = new X509Certificate2(pfxCertFilename, pfxPassword);
                }

                _SslCertificateCollection = new X509Certificate2Collection
                {
                    _SslCertificate
                };
            }

            _Header = "[CavemanTcp.Server " + _ListenerIp + ":" + _Port + "] ";
        }

        #endregion

        #region Public-Methods

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
            if (_IsListening) throw new InvalidOperationException("CavemanTcpServer is already running.");

            _Listener = new TcpListener(_IPAddress, _Port);
            _Listener.Start();

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;

            _Statistics = new CavemanTcpStatistics();
            _AcceptConnections = Task.Run(() => AcceptConnections(), _Token);

            Logger?.Invoke(_Header + "started");
        }

        /// <summary>
        /// Start accepting connections.
        /// </summary>
        /// <param name="token">Cancellation token for canceling the server.</param>
        /// <returns>Task.</returns>
        public Task StartAsync(CancellationToken token = default)
        {
            if (_IsListening) throw new InvalidOperationException("CavemanTcpServer is already running.");

            _Listener = new TcpListener(_IPAddress, _Port);
            _Listener.Start();

            if (token == default(CancellationToken))
            {
                _TokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                _Token = token;
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

        /// <summary>
        /// Stop accepting new connections.
        /// </summary>
        public void Stop()
        {
            if (!_IsListening) throw new InvalidOperationException("CavemanTcpServer is not running.");

            _IsListening = false;
            _Listener.Stop();
            _TokenSource.Cancel();

            Logger?.Invoke(_Header + "stopped");
        }

        /// <summary>
        /// Retrieve a list of client IP:port connected to the server.
        /// </summary>
        /// <returns>IEnumerable of strings, each containing client IP:port.</returns>
        public IEnumerable<string> GetClients()
        {
            return GetClientList();
        }

        /// <summary>
        /// Determines if a client is connected by its IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <returns>True if connected.</returns>
        public bool IsConnected(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort)); 
            return ClientExists(ipPort);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">String containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(string ipPort, string data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            ms.Write(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendWithoutTimeoutInternal(ipPort, bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(string ipPort, byte[] data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendWithoutTimeoutInternal(ipPort, data.Length, ms);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(string ipPort, long contentLength, Stream stream)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithoutTimeoutInternal(ipPort, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">String containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, string ipPort, string data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            ms.Write(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendWithTimeoutInternal(timeoutMs, ipPort, bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, string ipPort, byte[] data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendWithTimeoutInternal(timeoutMs, ipPort, data.Length, ms);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, string ipPort, long contentLength, Stream stream)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithTimeoutInternal(timeoutMs, ipPort, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">String containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string ipPort, string data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            if (token == default(CancellationToken)) token = _Token;
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendWithoutTimeoutInternalAsync(ipPort, bytes.Length, ms, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string ipPort, byte[] data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (token == default(CancellationToken)) token = _Token;
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendWithoutTimeoutInternalAsync(ipPort, data.Length, ms, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string ipPort, long contentLength, Stream stream, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            if (token == default(CancellationToken)) token = _Token;
            return await SendWithoutTimeoutInternalAsync(ipPort, contentLength, stream, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">String containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, string data, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            if (token == default(CancellationToken)) token = _Token;
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendWithTimeoutInternalAsync(timeoutMs, ipPort, bytes.Length, ms, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, byte[] data, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (token == default(CancellationToken)) token = _Token;
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendWithTimeoutInternalAsync(timeoutMs, ipPort, data.Length, ms, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, long contentLength, Stream stream, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            if (token == default(CancellationToken)) token = _Token;
            return await SendWithTimeoutInternalAsync(timeoutMs, ipPort, contentLength, stream, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Disconnects the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the client.</param>
        public void DisconnectClient(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            RemoveAndDisposeClient(ipPort);
            _Events.HandleClientDisconnected(this, new ClientDisconnectedEventArgs(ipPort, DisconnectReason.Kicked));
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult Read(string ipPort, int count)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return ReadWithoutTimeoutInternal(ipPort, (long)count);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult ReadWithTimeout(int timeoutMs, string ipPort, int count)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return ReadWithTimeoutInternal(timeoutMs, ipPort, (long)count);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadAsync(string ipPort, int count, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (token == default(CancellationToken)) token = _Token;
            return await ReadWithoutTimeoutInternalAsync(ipPort, (long)count, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadWithTimeoutAsync(int timeoutMs, string ipPort, int count, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (token == default(CancellationToken)) token = _Token;
            return await ReadWithTimeoutInternalAsync(timeoutMs, ipPort, (long)count, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Get direct access to the underlying client stream.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <returns>Stream.</returns>
        public Stream GetStream(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            return GetClientStream(ipPort);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Dispose of the TCP server.
        /// </summary>
        /// <param name="disposing">Dispose of resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger?.Invoke(_Header + "disposing");

                try
                {
                    lock (_ClientsLock)
                    {
                        if (_Clients != null && _Clients.Count > 0)
                        {
                            foreach (KeyValuePair<string, ClientMetadata> curr in _Clients)
                            {
                                curr.Value.Dispose();
                                Logger?.Invoke(_Header + "disconnected client: " + curr.Key);
                            }
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
                    Logger?.Invoke(_Header + "dispose exception:" +
                        Environment.NewLine +
                        e.ToString() +
                        Environment.NewLine);
                    _Events.HandleExceptionEncountered(this, e);
                }

                _IsListening = false;

                Logger?.Invoke(_Header + "disposed");
            }
        }

        private void EnableKeepalives()
        {
            try
            {
#if (NETCOREAPP3_0 || NETCOREAPP3_1 || NET5_0)

                _Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);

#elif NETFRAMEWORK

                byte[] keepAlive = new byte[12];
                Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveTime), 0, keepAlive, 4, 4); 
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveInterval), 0, keepAlive, 8, 4); 
                _Listener.Server.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

#elif NETSTANDARD

#endif
            }
            catch (Exception)
            {
                Logger?.Invoke(_Header + "keepalives not supported on this platform, disabled");
                _Keepalive.EnableTcpKeepAlives = false;
            }
        }

        private void EnableKeepalives(TcpClient client)
        {
            try
            {
#if (NETCOREAPP3_0 || NETCOREAPP3_1 || NET5_0)

                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);

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

        private bool IsClientConnected(System.Net.Sockets.TcpClient client)
        {
            if (client == null) return false;
            if (!client.Connected) return false;

            if ((client.Client.Poll(0, SelectMode.SelectWrite)) && (!client.Client.Poll(0, SelectMode.SelectError)))
            {
                byte[] buffer = new byte[1];
                if (client.Client.Receive(buffer, SocketFlags.Peek) == 0)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        private async Task AcceptConnections()
        {
            _IsListening = true;

            while (!_Token.IsCancellationRequested)
            {
                ClientMetadata client = null;

                try
                {
                    TcpClient tcpClient = await _Listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives(tcpClient);
                    client = new ClientMetadata(tcpClient);
                    CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Token, client.Token);

                    var _ = HandleClientConnection(client, linkedCts.Token)
                        .ContinueWith(x => linkedCts.Dispose())
                        .ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    _IsListening = false;
                    if (client != null) client.Dispose();
                    return;
                }
                catch (OperationCanceledException)
                {
                    _IsListening = false;
                    if (client != null) client.Dispose();
                    return;
                }
                catch (ObjectDisposedException)
                {
                    continue;
                }
                catch (Exception e)
                {
                    Logger?.Invoke(_Header + "exception while awaiting connections: " + e.ToString());
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
                        client.Dispose();
                        return;
                    }
                }

                AddClient(client.IpPort, client);

                if (_Settings.MonitorClientConnections)
                {
                    Logger?.Invoke(_Header + "starting connection monitor for: " + client.IpPort);
                    Task unawaited = Task.Run(() => ConnectionMonitor(client, token), token);
                }

                _Events.HandleClientConnected(this, new ClientConnectedEventArgs(client.IpPort));
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
            try
            {
                await client.SslStream.AuthenticateAsServerAsync(
                    _SslCertificate,
                    _Settings.MutuallyAuthenticate,
                    Common.GetSslProtocols(),
                    !_Settings.AcceptInvalidCertificates).ConfigureAwait(false);

                if (!client.SslStream.IsEncrypted)
                {
                    Logger?.Invoke(_Header + "client " + client.IpPort + " not encrypted, disconnecting");
                    return false;
                }

                if (!client.SslStream.IsAuthenticated)
                {
                    Logger?.Invoke(_Header + "client " + client.IpPort + " not SSL/TLS authenticated, disconnecting");
                    return false;
                }

                if (_Settings.MutuallyAuthenticate && !client.SslStream.IsMutuallyAuthenticated)
                {
                    Logger?.Invoke(_Header + "client " + client.IpPort + " failed mutual authentication, disconnecting");
                    return false;
                }

                return true;
            }
            catch (TaskCanceledException)
            {

            }
            catch (OperationCanceledException)
            {

            }

            return false;
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return _Settings.AcceptInvalidCertificates;
        }

        private async Task ConnectionMonitor(ClientMetadata client, CancellationToken token)
        {
            string ipPort = client.IpPort;

            try
            {
                while (client != null && !client.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    if (client == null || client.Client == null || !IsClientConnected(client.Client)) break;
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
                Logger?.Invoke(_Header + "client " + ipPort + " monitor exception:" + Environment.NewLine + e.ToString());
                _Events.HandleExceptionEncountered(this, e);
            }
            finally
            {
                Logger?.Invoke(_Header + "client " + ipPort + " disconnected");
                DisconnectClient(ipPort);
            }
        }

        #endregion

        #region Send

        // No cancellation token
        private WriteResult SendWithoutTimeoutInternal(string ipPort, long contentLength, Stream stream)
        {
            ClientMetadata client = GetClient(ipPort);
            if (client == null) return new WriteResult(WriteResultStatus.ClientNotFound, 0);

            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            try
            {
                while (!client.WriteSemaphore.Wait(10))
                {
                    Task.Delay(10).Wait();
                }

                if (contentLength > 0 && stream != null && stream.CanRead)
                {
                    long bytesRemaining = contentLength;

                    while (bytesRemaining > 0)
                    {
                        byte[] buffer = new byte[_Settings.StreamBufferSize];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            if (!_Ssl)
                            {
                                client.NetworkStream.Write(buffer, 0, bytesRead);
                                client.NetworkStream.Flush();
                            }
                            else
                            {
                                client.SslStream.Write(buffer, 0, bytesRead);
                                client.SslStream.Flush();
                            }

                            result.BytesWritten += bytesRead;

                            _Statistics.AddSentBytes(bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                result.Status = WriteResultStatus.Canceled;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Status = WriteResultStatus.Canceled;
                return result;
            }
            catch (Exception)
            {
                result.Status = WriteResultStatus.Disconnected;
                return result;
            }
            finally
            {
                if (client != null) client.WriteSemaphore.Release();
            }
        }

        // Timeout cancellation token
        private WriteResult SendWithTimeoutInternal(int timeoutMs, string ipPort, long contentLength, Stream stream)
        {
            ClientMetadata client = GetClient(ipPort);
            if (client == null) return new WriteResult(WriteResultStatus.ClientNotFound, 0);

            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_TokenSource.Token);
            var timeoutToken = timeoutCts.Token;

            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            Task<WriteResult> task = Task.Run(() =>
            {
                try
                {
                    while (!client.WriteSemaphore.Wait(10))
                    {
                        Task.Delay(10).Wait();
                    }

                    if (contentLength > 0 && stream != null && stream.CanRead)
                    {
                        long bytesRemaining = contentLength;

                        while (bytesRemaining > 0)
                        {
                            byte[] buffer = new byte[_Settings.StreamBufferSize];
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                if (!_Ssl)
                                {
                                    client.NetworkStream.Write(buffer, 0, bytesRead);
                                    client.NetworkStream.Flush();
                                }
                                else
                                {
                                    client.SslStream.Write(buffer, 0, bytesRead);
                                    client.SslStream.Flush();
                                }

                                result.BytesWritten += bytesRead;

                                _Statistics.AddSentBytes(bytesRead);
                                bytesRemaining -= bytesRead;
                            }
                        }
                    }

                    return result;
                }
                catch (TaskCanceledException)
                {
                    result.Status = WriteResultStatus.Canceled;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.Status = WriteResultStatus.Canceled;
                    return result;
                }
                catch (Exception)
                {
                    result.Status = WriteResultStatus.Disconnected;
                    return result;
                }
                finally
                {
                    if (client != null) client.WriteSemaphore.Release();
                    timeoutCts.Dispose();
                }
            }, timeoutToken);

            bool success = task.Wait(TimeSpan.FromMilliseconds(timeoutMs));
            timeoutCts.Cancel();

            if (success)
            {
                return task.Result;
            }
            else
            {
                result.Status = WriteResultStatus.Timeout;
                return result;
            }
        }

        // Supplied cancellation token
        private async Task<WriteResult> SendWithoutTimeoutInternalAsync(string ipPort, long contentLength, Stream stream, CancellationToken token)
        {
            ClientMetadata client = GetClient(ipPort);
            if (client == null) return new WriteResult(WriteResultStatus.ClientNotFound, 0);

            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            try
            {
                while (true)
                {
                    bool success = await client.WriteSemaphore.WaitAsync(10, token).ConfigureAwait(false);
                    if (success) break;
                    await Task.Delay(10).ConfigureAwait(false);
                }

                if (contentLength > 0 && stream != null && stream.CanRead)
                {
                    long bytesRemaining = contentLength;

                    while (bytesRemaining > 0)
                    {
                        byte[] buffer = new byte[_Settings.StreamBufferSize];
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        if (bytesRead > 0)
                        {
                            if (!_Ssl)
                            {
                                await client.NetworkStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                await client.NetworkStream.FlushAsync(token).ConfigureAwait(false);
                            }
                            else
                            {
                                await client.SslStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                await client.SslStream.FlushAsync(token).ConfigureAwait(false);
                            }

                            result.BytesWritten += bytesRead;
                            _Statistics.AddSentBytes(bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                result.Status = WriteResultStatus.Canceled;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Status = WriteResultStatus.Canceled;
                return result;
            }
            catch (Exception)
            {
                result.Status = WriteResultStatus.Disconnected;
                return result;
            }
            finally
            {
                if (client != null) client.WriteSemaphore.Release();
            }
        }

        // Supplied cancellation token
        private async Task<WriteResult> SendWithTimeoutInternalAsync(int timeoutMs, string ipPort, long contentLength, Stream stream, CancellationToken token)
        {
            ClientMetadata client = GetClient(ipPort);
            if (client == null) return new WriteResult(WriteResultStatus.ClientNotFound, 0);

            CancellationTokenSource timeoutCts = new CancellationTokenSource();
            CancellationToken timeoutToken = timeoutCts.Token;

            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            Task<WriteResult> task = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        bool success = await client.WriteSemaphore.WaitAsync(10, token).ConfigureAwait(false);
                        if (success) break;
                        await Task.Delay(10).ConfigureAwait(false);
                    }

                    if (contentLength > 0 && stream != null && stream.CanRead)
                    {
                        long bytesRemaining = contentLength;

                        while (bytesRemaining > 0)
                        {
                            byte[] buffer = new byte[_Settings.StreamBufferSize];
                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                            if (bytesRead > 0)
                            {
                                if (!_Ssl)
                                {
                                    await client.NetworkStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                    await client.NetworkStream.FlushAsync(token).ConfigureAwait(false);
                                }
                                else
                                {
                                    await client.SslStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                    await client.SslStream.FlushAsync(token).ConfigureAwait(false);
                                }

                                result.BytesWritten += bytesRead;
                                _Statistics.AddSentBytes(bytesRead);
                                bytesRemaining -= bytesRead;
                            }
                        }
                    }

                    return result;
                }
                catch (TaskCanceledException)
                {
                    result.Status = WriteResultStatus.Canceled;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.Status = WriteResultStatus.Canceled;
                    return result;
                }
                catch (Exception)
                {
                    result.Status = WriteResultStatus.Disconnected;
                    return result;
                }
                finally
                {
                    if (client != null) client.WriteSemaphore.Release();
                    timeoutCts.Dispose();
                }
            },
            timeoutToken);

            Task delay = Task.Delay(timeoutMs, timeoutToken);
            Task first = await Task.WhenAny(task, delay).ConfigureAwait(false);
            timeoutCts.Cancel();

            if (first == task)
            {
                return task.Result;
            }
            else
            {
                result.Status = WriteResultStatus.Timeout;
                return result;
            }
        }

        #endregion

        #region Read

        // No cancellation token
        private ReadResult ReadWithoutTimeoutInternal(string ipPort, long count)
        {
            if (count < 1) return new ReadResult(ReadResultStatus.Success, 0, null);

            ClientMetadata client = GetClient(ipPort);
            if (client == null) return new ReadResult(ReadResultStatus.ClientNotFound, 0, null);
             
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            try
            {
                while (!client.ReadSemaphore.Wait(10))
                {
                    Task.Delay(10).Wait();
                }

                MemoryStream ms = new MemoryStream();
                long bytesRemaining = count;

                while (bytesRemaining > 0)
                {
                    byte[] buffer = null;
                    if (bytesRemaining >= _Settings.StreamBufferSize) buffer = new byte[_Settings.StreamBufferSize];
                    else buffer = new byte[bytesRemaining];

                    int bytesRead = 0;
                    if (!_Ssl) bytesRead = client.NetworkStream.Read(buffer, 0, buffer.Length);
                    else bytesRead = client.SslStream.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);
                        result.BytesRead += bytesRead;
                        _Statistics.AddReceivedBytes(bytesRead);
                        bytesRemaining -= bytesRead;
                    }
                }

                ms.Seek(0, SeekOrigin.Begin);
                result.DataStream = ms;
                return result;
            }
            catch (TaskCanceledException)
            {
                result.Status = ReadResultStatus.Canceled;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Status = ReadResultStatus.Canceled;
                return result;
            }
            catch (Exception)
            {
                result.Status = ReadResultStatus.Disconnected;
                result.BytesRead = 0;
                result.DataStream = null;
                RemoveAndDisposeClient(ipPort);
                return result;
            }
            finally
            {
                if (client != null) client.ReadSemaphore.Release();
            }
        }

        // Timeout cancellation token
        private ReadResult ReadWithTimeoutInternal(int timeoutMs, string ipPort, long count)
        {
            if (count < 1) return new ReadResult(ReadResultStatus.Success, 0, null);

            ClientMetadata client = GetClient(ipPort);
            if (client == null) return new ReadResult(ReadResultStatus.ClientNotFound, 0, null);

            CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_Token);
            CancellationToken timeoutToken = timeoutCts.Token;

            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            Task<ReadResult> task = Task.Run(() =>
            {
                try
                {
                    while (!client.ReadSemaphore.Wait(10))
                    {
                        Task.Delay(10).Wait();
                    }

                    MemoryStream ms = new MemoryStream();
                    long bytesRemaining = count;

                    while (bytesRemaining > 0)
                    {
                        byte[] buffer = null;
                        if (bytesRemaining >= _Settings.StreamBufferSize) buffer = new byte[_Settings.StreamBufferSize];
                        else buffer = new byte[bytesRemaining];

                        int bytesRead = 0;
                        if (!_Ssl) bytesRead = client.NetworkStream.Read(buffer, 0, buffer.Length);
                        else bytesRead = client.SslStream.Read(buffer, 0, buffer.Length);

                        if (bytesRead > 0)
                        {
                            ms.Write(buffer, 0, bytesRead);
                            result.BytesRead += bytesRead;
                            _Statistics.AddReceivedBytes(bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    result.DataStream = ms;
                    return result;
                }
                catch (TaskCanceledException)
                {
                    result.Status = ReadResultStatus.Canceled;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.Status = ReadResultStatus.Canceled;
                    return result;
                }
                catch (Exception)
                {
                    result.Status = ReadResultStatus.Disconnected;
                    result.BytesRead = 0;
                    result.DataStream = null;
                    RemoveAndDisposeClient(ipPort);
                    return result;
                }
            }, timeoutToken);

            bool success = task.Wait(TimeSpan.FromMilliseconds(timeoutMs));
            if (client != null) client.ReadSemaphore.Release();

            timeoutCts.Cancel();
            timeoutCts.Dispose();

            if (success)
            {
                return task.Result;
            }
            else
            {
                result.Status = ReadResultStatus.Timeout;
                return result;
            }
        }

        // Supplied cancellation token
        private async Task<ReadResult> ReadWithoutTimeoutInternalAsync(string ipPort, long count, CancellationToken token)
        {
            if (count < 1) return new ReadResult(ReadResultStatus.Success, 0, null);

            ClientMetadata client = GetClient(ipPort);
            if (client == null) return new ReadResult(ReadResultStatus.ClientNotFound, 0, null);

            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            try
            {
                while (true)
                {
                    bool success = await client.ReadSemaphore.WaitAsync(10, token).ConfigureAwait(false);
                    if (success) break;
                    await Task.Delay(10).ConfigureAwait(false);
                }

                MemoryStream ms = new MemoryStream();
                long bytesRemaining = count;

                while (bytesRemaining > 0)
                {
                    byte[] buffer = null;
                    if (bytesRemaining >= _Settings.StreamBufferSize) buffer = new byte[_Settings.StreamBufferSize];
                    else buffer = new byte[bytesRemaining];

                    int bytesRead = 0;
                    if (!_Ssl) bytesRead = await client.NetworkStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    else bytesRead = await client.SslStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

                    if (bytesRead > 0)
                    {
                        await ms.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                        result.BytesRead += bytesRead;
                        _Statistics.AddReceivedBytes(bytesRead);
                        bytesRemaining -= bytesRead;
                    }
                }

                ms.Seek(0, SeekOrigin.Begin);
                result.DataStream = ms;
                return result;
            }
            catch (TaskCanceledException)
            {
                result.Status = ReadResultStatus.Canceled;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Status = ReadResultStatus.Canceled;
                return result;
            }
            catch (Exception)
            {
                result.Status = ReadResultStatus.Disconnected;
                result.BytesRead = 0;
                result.DataStream = null;
                RemoveAndDisposeClient(ipPort);
                return result;
            }
            finally
            {
                if (client != null) client.ReadSemaphore.Release();
            }
        }

        // Supplied cancellation token, timeout cancellation token
        private async Task<ReadResult> ReadWithTimeoutInternalAsync(int timeoutMs, string ipPort, long count, CancellationToken token)
        {
            if (count < 1) return new ReadResult(ReadResultStatus.Success, 0, null);

            ClientMetadata client = GetClient(ipPort);
            if (client == null) return new ReadResult(ReadResultStatus.ClientNotFound, 0, null);

            CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_Token, token);
            CancellationToken timeoutToken = timeoutCts.Token;

            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            Task<ReadResult> task = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        bool success = await client.ReadSemaphore.WaitAsync(10, token).ConfigureAwait(false);
                        if (success) break;
                        await Task.Delay(10, token).ConfigureAwait(false);
                    }

                    MemoryStream ms = new MemoryStream();
                    long bytesRemaining = count;

                    while (bytesRemaining > 0)
                    {
                        byte[] buffer = null;
                        if (bytesRemaining >= _Settings.StreamBufferSize) buffer = new byte[_Settings.StreamBufferSize];
                        else buffer = new byte[bytesRemaining];

                        int bytesRead = 0;
                        if (!_Ssl) bytesRead = await client.NetworkStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        else bytesRead = await client.SslStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

                        if (bytesRead > 0)
                        {
                            await ms.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                            result.BytesRead += bytesRead;
                            _Statistics.AddReceivedBytes(bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    result.DataStream = ms;
                    return result;
                }
                catch (TaskCanceledException)
                {
                    result.Status = ReadResultStatus.Canceled;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.Status = ReadResultStatus.Canceled;
                    return result;
                }
                catch (Exception)
                {
                    result.Status = ReadResultStatus.Disconnected;
                    result.BytesRead = 0;
                    result.DataStream = null;
                    RemoveAndDisposeClient(ipPort);
                    return result;
                }
            },
            timeoutToken);

            Task delay = Task.Delay(timeoutMs, timeoutToken);
            Task first = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (client != null) client.ReadSemaphore.Release();

            timeoutCts.Cancel();
            timeoutCts.Dispose();

            if (first == task)
            {
                return task.Result;
            }
            else
            {
                result.Status = ReadResultStatus.Timeout;
                return result;
            }
        }

        #endregion

        #region Client

        private IEnumerable<string> GetClientList()
        {
            lock (_ClientsLock)
            {
                return new List<string>(_Clients.Keys);
            }
        }

        private bool ClientExists(string ipPort)
        {
            lock (_ClientsLock)
            {
                return (_Clients.TryGetValue(ipPort, out _));
            }
        }

        private void RemoveAndDisposeClient(string ipPort)
        {
            lock (_ClientsLock)
            {
                if (_Clients.TryGetValue(ipPort, out ClientMetadata client))
                {
                    client.Dispose();
                    _Clients.Remove(ipPort);
                }

                Logger?.Invoke(_Header + "removed: " + ipPort);
            } 
        }

        private Stream GetClientStream(string ipPort)
        { 
            lock (_ClientsLock)
            {
                if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
                {
                    throw new KeyNotFoundException("Client with IP:port of " + ipPort + " not found.");
                }

                if (!_Ssl) return client.NetworkStream;
                else return client.SslStream;
            } 
        }

        private void AddClient(string ipPort, ClientMetadata client)
        {
            lock (_ClientsLock)
            {
                Logger?.Invoke(_Header + "adding client " + client.IpPort);
                _Clients.Add(client.IpPort, client);
            }
        }

        private ClientMetadata GetClient(string ipPort)
        {
            lock (_ClientsLock)
            {
                if (_Clients.TryGetValue(ipPort, out ClientMetadata client))
                {
                    return client;
                }

                return null;
            }
        }

        #endregion

        #endregion
    }
}
