namespace CavemanTcp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Runtime.InteropServices;
    using System.Security.Authentication;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Buffers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// CavemanTcp is a simple TCP client and server providing callers with easy integration and full control over network reads and writes.
    /// Once instantiated, use Connect(int) to connect to the server.
    /// </summary>
    public class CavemanTcpClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Indicates if the client is connected to the server.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return _IsConnected;
            }
        }

        /// <summary>
        /// Method to invoke when sending log messages.
        /// </summary>
        public Action<string> Logger = null;

        /// <summary>
        /// CavemanTcp client settings.
        /// </summary>
        public CavemanTcpClientSettings Settings
        {
            get
            {
                return _Settings;
            }
            set
            {
                if (value == null) _Settings = new CavemanTcpClientSettings();
                else _Settings = value;
            }
        }

        /// <summary>
        /// CavemanTcp client events.
        /// </summary>
        public CavemanTcpClientEvents Events
        {
            get
            {
                return _Events;
            }
            set
            {
                if (value == null) _Events = new CavemanTcpClientEvents();
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

        private CavemanTcpClientSettings _Settings = new CavemanTcpClientSettings();
        private CavemanTcpClientEvents _Events = new CavemanTcpClientEvents();
        private CavemanTcpKeepaliveSettings _Keepalive = new CavemanTcpKeepaliveSettings();
        private CavemanTcpStatistics _Statistics = new CavemanTcpStatistics();

        private bool _IsConnected = false;
        private bool _Disposed = false;
        private string _Header = "[CavemanTcp.Client] ";
        private string _ServerIp = null;
        private int _ServerPort = 0;
        private IPAddress _IPAddress;
        private System.Net.Sockets.TcpClient _Client = null;
        private NetworkStream _NetworkStream = null;

        private bool _Ssl = false;
        private string _PfxCertFilename = null;
        private string _PfxPassword = null;
        private SslStream _SslStream = null;
        private X509Certificate2 _SslCertificate = null;
        private X509Certificate2Collection _SslCertificateCollection;
        private readonly bool _AuthenticateServerIpOrHostOnly;
        
        private SemaphoreSlim _WriteSemaphore = new SemaphoreSlim(1, 1);
        private SemaphoreSlim _ReadSemaphore = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;
        private Task _ConnectionMonitor = null;
        private static readonly TimeSpan _DefaultConnectionMonitorInterval = TimeSpan.FromMilliseconds(250);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiates the TCP client without SSL.  Set the Connected, Disconnected, and DataReceived callbacks.  Once set, use Connect() to connect to the server.
        /// </summary>
        /// <param name="ipPort">The IP:port of the server.</param> 
        public CavemanTcpClient(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));

            Common.ParseIpPort(ipPort, out _ServerIp, out _ServerPort);
            if (_ServerPort < 0) throw new ArgumentException("Port must be zero or greater.");
            if (String.IsNullOrEmpty(_ServerIp)) throw new ArgumentNullException("Server IP or hostname must not be null.");

            if (!IPAddress.TryParse(_ServerIp, out _IPAddress))
            {
                _IPAddress = Dns.GetHostEntry(_ServerIp).AddressList[0];
                _ServerIp = _IPAddress.ToString();
            }

            InitializeClient();
        }

        /// <summary>
        /// Instantiates the TCP client.  Set the Connected, Disconnected, and DataReceived callbacks.  Once set, use Connect() to connect to the server.
        /// </summary>
        /// <param name="serverIpOrHostname">The server IP address or hostname.</param>
        /// <param name="port">The TCP port on which to connect.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        public CavemanTcpClient(string serverIpOrHostname, int port, bool ssl)
        {
            if (String.IsNullOrEmpty(serverIpOrHostname)) throw new ArgumentNullException(nameof(serverIpOrHostname));
            if (port < 0) throw new ArgumentException("Port must be zero or greater.");

            _ServerIp = serverIpOrHostname;

            if (!IPAddress.TryParse(_ServerIp, out _IPAddress))
            {
                _IPAddress = Dns.GetHostEntry(serverIpOrHostname).AddressList[0];
                _ServerIp = _IPAddress.ToString();
            }

            _ServerPort = port;

            _Ssl = ssl;

            _AuthenticateServerIpOrHostOnly = true;
            
            InitializeClient(); 
        }
        
        /// <summary>
        /// Instantiates the TCP client without SSL.  Set the Connected, Disconnected, and DataReceived callbacks.  Once set, use Connect() to connect to the server.
        /// </summary>
        /// <param name="serverIpOrHostname">The server IP address or hostname.</param>
        /// <param name="port">The TCP port on which to connect.</param>
        /// <param name="certificate">SSL certificate.</param>
        public CavemanTcpClient(string serverIpOrHostname, int port, X509Certificate2 certificate = null)
        {
            if (String.IsNullOrEmpty(serverIpOrHostname)) throw new ArgumentNullException(nameof(serverIpOrHostname));
            if (port < 0) throw new ArgumentException("Port must be zero or greater.");

            _ServerIp = serverIpOrHostname;

            if (!IPAddress.TryParse(_ServerIp, out _IPAddress))
            {
                _IPAddress = Dns.GetHostEntry(serverIpOrHostname).AddressList[0];
                _ServerIp = _IPAddress.ToString();
            }

            _ServerPort = port;

            if (certificate != null)
            {
                _Ssl = true;
                _SslCertificate = certificate;
                _SslCertificateCollection = new X509Certificate2Collection { _SslCertificate };
            }

            InitializeClient();
        }

        /// <summary>
        /// Instantiates the TCP client.  Set the Connected, Disconnected, and DataReceived callbacks.  Once set, use Connect() to connect to the server.
        /// </summary>
        /// <param name="ipPort">The IP:port of the server.</param> 
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="pfxCertFilename">The filename of the PFX certificate file.</param>
        /// <param name="pfxPassword">The password to the PFX certificate file.</param>
        public CavemanTcpClient(string ipPort, bool ssl, string pfxCertFilename, string pfxPassword)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));

            Common.ParseIpPort(ipPort, out _ServerIp, out _ServerPort);
            if (_ServerPort < 0) throw new ArgumentException("Port must be zero or greater.");
            if (String.IsNullOrEmpty(_ServerIp)) throw new ArgumentNullException("Server IP or hostname must not be null.");

            if (!IPAddress.TryParse(_ServerIp, out _IPAddress))
            {
                _IPAddress = Dns.GetHostEntry(_ServerIp).AddressList[0];
                _ServerIp = _IPAddress.ToString();
            }

            _Ssl = ssl;
            _PfxCertFilename = pfxCertFilename;
            _PfxPassword = pfxPassword;

            InitializeClient();
        }

        /// <summary>
        /// Instantiates the TCP client.  Set the Connected, Disconnected, and DataReceived callbacks.  Once set, use Connect() to connect to the server.
        /// </summary>
        /// <param name="serverIpOrHostname">The server IP address or hostname.</param>
        /// <param name="port">The TCP port on which to connect.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="pfxCertFilename">The filename of the PFX certificate file.</param>
        /// <param name="pfxPassword">The password to the PFX certificate file.</param>
        public CavemanTcpClient(string serverIpOrHostname, int port, bool ssl, string pfxCertFilename, string pfxPassword)
        {
            if (String.IsNullOrEmpty(serverIpOrHostname)) throw new ArgumentNullException(nameof(serverIpOrHostname));
            if (port < 0) throw new ArgumentException("Port must be zero or greater.");

            _ServerIp = serverIpOrHostname;

            if (!IPAddress.TryParse(_ServerIp, out _IPAddress))
            {
                _IPAddress = Dns.GetHostEntry(serverIpOrHostname).AddressList[0];
                _ServerIp = _IPAddress.ToString();
            }

            _ServerPort = port;

            _Ssl = ssl;
            _PfxCertFilename = pfxCertFilename;
            _PfxPassword = pfxPassword;

            InitializeClient(); 
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose of the TCP client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Establish the connection to the server.
        /// </summary>
        public void Connect(int timeoutSeconds)
        {  
            if (timeoutSeconds < 1) throw new ArgumentException("Timeout must be greater than zero seconds.");
            if (IsConnected) return;
            _Disposed = false;
            InitializeClient(); 
            if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives();

            IAsyncResult ar = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
            WaitHandle wh = ar.AsyncWaitHandle;

            try
            {
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds), false))
                {
                    _Client.Close();
                    throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                }

                _Client.EndConnect(ar); 
                _NetworkStream = _Client.GetStream();

                if (_Ssl)
                {
                    if (_Settings.AcceptInvalidCertificates)
                    {
                        // accept invalid certs
                        _SslStream = new SslStream(_NetworkStream, false, new RemoteCertificateValidationCallback(AcceptCertificate));

                        // override the online revocation check
                        _Settings.CheckCertificateRevocation = false;
                    }
                    else
                    {
                        // do not accept invalid SSL certificates
                        _SslStream = new SslStream(_NetworkStream, false, ValidateServerCertificate);
                    }

                    _SslStream.AuthenticateAsClient(_ServerIp, _SslCertificateCollection, Common.GetSslProtocol, _Settings.CheckCertificateRevocation);

                    if (!_SslStream.IsEncrypted)
                    {
                        throw new AuthenticationException("Stream is not encrypted");
                    }

                    if (!_SslStream.IsAuthenticated)
                    {
                        throw new AuthenticationException("Stream is not authenticated");
                    }

                    if (_Settings.MutuallyAuthenticate && !_SslStream.IsMutuallyAuthenticated)
                    {
                        throw new AuthenticationException("Mutual authentication failed");
                    }
                }

                _IsConnected = true;
                _Statistics = new CavemanTcpStatistics();
                _TokenSource = new CancellationTokenSource();
                _Token = _TokenSource.Token;

                if (_Settings.EnableConnectionMonitor)
                {
                    Logger?.Invoke(_Header + "starting connection monitor");

                    _ConnectionMonitor = Task.Run(() => ConnectionMonitor(), _Token);
                }

                _Events.HandleClientConnected(this);
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "exception while connecting to " + _ServerIp + ":" + _ServerPort + ": " + e.Message);
                _Events.HandleExceptionEncountered(this, e);
                throw;
            }
            finally
            {
                wh.Close();
            }
        }


        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected) return;
            Dispose();
        }

        #region Send

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(string data)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return Send(bytes);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(byte[] data)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            return SendBytesWithoutTimeoutInternal(data);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(long contentLength, Stream stream)
        {
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithoutTimeoutInternal(contentLength, stream);
        }

        #endregion

        #region SendWithTimeout

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="data">String data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, string data)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return SendWithTimeout(timeoutMs, bytes);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, byte[] data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            return SendBytesWithTimeoutInternal(timeoutMs, data);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, long contentLength, Stream stream)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithTimeoutInternal(timeoutMs, contentLength, stream);
        }

        #endregion

        #region SendAsync

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string data, CancellationToken token = default)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendAsync(bytes, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(byte[] data, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            return await SendBytesWithoutTimeoutInternalAsync(data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(long contentLength, Stream stream, CancellationToken token = default)
        {
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendWithoutTimeoutInternalAsync(contentLength, stream, token).ConfigureAwait(false);
        }

        #endregion

        #region SendWithTimeoutAsync

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="data">String data.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string data, CancellationToken token = default)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendWithTimeoutAsync(timeoutMs, bytes, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, byte[] data, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (data == null || data.Length < 1) data = Array.Empty<byte>();
            if (data.Length < 1) throw new ArgumentException("No data supplied in stream.");
            return await SendBytesWithTimeoutInternalAsync(timeoutMs, data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, long contentLength, Stream stream, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendWithTimeoutInternalAsync(timeoutMs, contentLength, stream, token).ConfigureAwait(false);
        }

        #endregion

        #region Read

        /// <summary>
        /// Read from the server.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult Read(int count)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream."); 
            return ReadWithTimeoutInternal(-1, count); 
        }

        #endregion

        #region ReadWithTimeout

        /// <summary>
        /// Read from the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult ReadWithTimeout(int timeoutMs, int count)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream.");
            return ReadWithTimeoutInternal(timeoutMs, count);
        }

        #endregion

        #region ReadAsync

        /// <summary>
        /// Read from the server.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadAsync(int count, CancellationToken token = default)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream.");
            return await ReadWithoutTimeoutInternalAsync(count, token).ConfigureAwait(false);
        }

        #endregion

        #region ReadWithTimeoutAsync

        /// <summary>
        /// Read from the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadWithTimeoutAsync(int timeoutMs, int count, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream.");
            return await ReadWithTimeoutInternalAsync(timeoutMs, count, token).ConfigureAwait(false);
        }

        #endregion

        #region Other

        /// <summary>
        /// Get direct access to the underlying client stream.
        /// </summary>
        /// <returns>Stream.</returns>
        public Stream GetStream()
        {
            if (_Client == null || !_IsConnected) throw new IOException("Client is not connected.");  
            if (!_Ssl) return _NetworkStream; 
            else return _SslStream; 
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
        /// Dispose of the TCP client.
        /// </summary>
        /// <param name="disposing">Dispose of resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;

            if (disposing)
            {
                Logger?.Invoke(_Header + "disposing");

                bool wasConnected = _IsConnected;

                try
                {
                    // Cancel token and close streams BEFORE acquiring semaphores
                    // to unblock any pending read/write operations that hold them.
                    // Acquiring semaphores first would deadlock if a read/write is
                    // blocked on the stream while holding the semaphore.
                    if (_TokenSource != null && !_Token.IsCancellationRequested)
                    {
                        _TokenSource.Cancel();
                    }

                    if (_SslStream != null)
                    {
                        _SslStream.Close();
                    }

                    if (_NetworkStream != null)
                    {
                        _NetworkStream.Close();
                    }

                    if (_Client != null)
                    {
                        _Client.Close();
                        _Client.Dispose();
                        _Client = null;
                    }

                    // Now acquire semaphores to ensure pending operations have
                    // finished unwinding after the cancellation/close above.
                    _WriteSemaphore.Wait();
                    _ReadSemaphore.Wait();
                }
                catch (Exception e)
                {
                    Logger?.Invoke(_Header + "dispose exception:" +
                        Environment.NewLine +
                        e.ToString() +
                        Environment.NewLine);
                    _Events.HandleExceptionEncountered(this, e);
                }
                finally
                {
                    _IsConnected = false;

                    // Fire disconnected event BEFORE clearing handlers so
                    // subscribers are notified even on explicit Disconnect().
                    if (wasConnected)
                    {
                        _Events.HandleClientDisconnected(this);
                    }

                    _Events.ClearAllEventHandlers();
                    try { _ReadSemaphore.Release(); } catch (ObjectDisposedException) { }
                    try { _WriteSemaphore.Release(); } catch (ObjectDisposedException) { }
                }

                Logger?.Invoke(_Header + "disposed");
            }
        }

        private void EnableKeepalives()
        {
            // issues with definitions: https://github.com/dotnet/sdk/issues/14540

            try
            {
#if NETCOREAPP3_1_OR_GREATER || NET6_0_OR_GREATER

                // NETCOREAPP3_1_OR_GREATER catches .NET 5.0

                _Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);

                // Windows 10 version 1703 or later

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    && Environment.OSVersion.Version >= new Version(10, 0, 15063))
                {
                    _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);
                }

#elif NETFRAMEWORK

                byte[] keepAlive = new byte[12]; 
                Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveTime), 0, keepAlive, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveInterval), 0, keepAlive, 8, 4);
                _Client.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

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

        private void InitializeClient()
        {
            if (_Client != null)
            {
                _Client.Close();
                _IsConnected = false;
            }

            _Client = new System.Net.Sockets.TcpClient();
            _Client.NoDelay = true;

            _SslStream = null;

            if (_Ssl && _SslCertificate == null)
            {
                if (_AuthenticateServerIpOrHostOnly)
                {
                    // create an empty collection to pass during authentication
                    _SslCertificateCollection = new X509Certificate2Collection();
                }
                else
                {
                    _SslCertificate = LoadCertificateFromFile(_PfxCertFilename, _PfxPassword);

                    _SslCertificateCollection = new X509Certificate2Collection
                    {
                        _SslCertificate
                    };

                }
            }

            _Header = "[CavemanTcp.Client " + _ServerIp + ":" + _ServerPort + "] ";
        }

        private bool IsClientConnected()
        {
            Socket socket = _Client?.Client;

            if (socket == null || !_Client.Connected)
            {
                return false;
            }

            try
            {
                if (socket.Poll(0, SelectMode.SelectError))
                {
                    Logger?.Invoke(_Header + "socket poll reported an error");
                    return false;
                }

                if (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
                {
                    Logger?.Invoke(_Header + "socket poll detected remote disconnect");
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
                Logger?.Invoke(_Header + "socket exception while checking connectivity: " + se.Message);
                return false;
            }
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        { 
            return _Settings.AcceptInvalidCertificates;
        }
        
        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }
        
            Logger?.Invoke(_Header + $"Certificate error: {sslPolicyErrors}");
        
            if (chain != null && chain.ChainElements != null && chain.ChainElements.Count > 0)
            {
                var chainInfo = GetCertificateChainInformation(chain);
                Logger?.Invoke(_Header + chainInfo);
            }
            else
            {
                Logger?.Invoke(_Header + "No certificate chain could be established");
            }
        
            return false;
        }

        private string GetCertificateChainInformation(X509Chain chain)
        {
            StringBuilder sb = new StringBuilder();
            
            sb.AppendLine($"Chain element information ({chain.ChainElements.Count} found) ...");

            for (int i = 0; i < chain.ChainElements.Count; i++)
            {
                var elementInfo = GetCertificateChainElementInformation(chain.ChainElements[i], i);
                sb.AppendLine(elementInfo);
            }

            sb.AppendLine("End of chain element information");
            return sb.ToString();
        }

        private string GetCertificateChainElementInformation(X509ChainElement element, int index)
        {
            if (element == null)
            {
                return "No chain element information could be retrieved";
            }

            if (element.Certificate == null)
            {
                return "No chain element certificate information could be retrieved";
            } 
            
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Number: {index}");
            sb.AppendLine($"Subject: {element.Certificate.Subject}");
            sb.AppendLine($"Issuer: {element.Certificate.Issuer}");
            sb.AppendLine($"Thumbprint: {element.Certificate.Thumbprint}");
            sb.AppendLine($"Validity Period: {element.Certificate.NotBefore} to {element.Certificate.NotAfter}");

            if (element.ChainElementStatus != null && element.ChainElementStatus.Any())
            {
                sb.AppendLine($"Element chain status information ({element.ChainElementStatus.Length} found): ");
                foreach (X509ChainStatus status in element.ChainElementStatus)
                {
                    if (status.Status != X509ChainStatusFlags.NoError)
                    {
                        sb.AppendLine($"Status: {status.Status}");
                        sb.AppendLine($"Status Information: {status.StatusInformation}");
                    }
                }
            }

            sb.AppendLine("---");
            return sb.ToString();
        }

        private async Task ConnectionMonitor()
        {
            try
            {
                while (!_TokenSource.IsCancellationRequested)
                {
                    await Task.Delay(GetConnectionMonitorInterval(), _Token).ConfigureAwait(false);

                    if (!IsClientConnected())
                    {
                        Logger?.Invoke(_Header + "disconnection detected");
                        break;
                    }
                }
            }
            catch (SocketException se)
            {
                if (!_TokenSource.IsCancellationRequested)
                    Logger?.Invoke(_Header + "socket exception" + Environment.NewLine + se.ToString());
            }
            catch (TaskCanceledException tce)
            {
                if (!_TokenSource.IsCancellationRequested)
                    Logger?.Invoke(_Header + "task canceled exception" + Environment.NewLine + tce.ToString());
            }
            catch (OperationCanceledException oce)
            {
                if (!_TokenSource.IsCancellationRequested)
                    Logger?.Invoke(_Header + "operation canceled exception" + Environment.NewLine + oce.ToString());
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "connection monitor exception:" + Environment.NewLine + e.ToString());
                _Events.HandleExceptionEncountered(this, e);
            }
            finally
            {
                Logger?.Invoke(_Header + "disconnected");
                Dispose(true);
            }
        }

        private TimeSpan GetConnectionMonitorInterval()
        {
            if (_Settings.PollIntervalMicroSeconds < 1) return _DefaultConnectionMonitorInterval;
            return TimeSpan.FromTicks(_Settings.PollIntervalMicroSeconds * 10L);
        }

        #endregion

        #region Send

        private WriteResult SendBytesWithoutTimeoutInternal(byte[] data)
        {
            return SendBytesInternalAsync(data, -1, CancellationToken.None).GetAwaiter().GetResult();
        }

        private WriteResult SendBytesWithTimeoutInternal(int timeoutMs, byte[] data)
        {
            return SendBytesInternalAsync(data, timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        }

        private WriteResult SendWithoutTimeoutInternal(long contentLength, Stream stream)
        {
            return SendStreamInternalAsync(contentLength, stream, -1, CancellationToken.None).GetAwaiter().GetResult();
        }

        private WriteResult SendWithTimeoutInternal(int timeoutMs, long contentLength, Stream stream)
        {
            return SendStreamInternalAsync(contentLength, stream, timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        }

        private Task<WriteResult> SendBytesWithoutTimeoutInternalAsync(byte[] data, CancellationToken token)
        {
            return SendBytesInternalAsync(data, -1, token);
        }

        private Task<WriteResult> SendBytesWithTimeoutInternalAsync(int timeoutMs, byte[] data, CancellationToken token)
        {
            return SendBytesInternalAsync(data, timeoutMs, token);
        }

        private async Task<WriteResult> SendWithoutTimeoutInternalAsync(long contentLength, Stream stream, CancellationToken token)
        {
            return await SendStreamInternalAsync(contentLength, stream, -1, token).ConfigureAwait(false);
        }

        private async Task<WriteResult> SendWithTimeoutInternalAsync(int timeoutMs, long contentLength, Stream stream, CancellationToken token)
        {
            return await SendStreamInternalAsync(contentLength, stream, timeoutMs, token).ConfigureAwait(false);
        }

        private async Task<WriteResult> SendBytesInternalAsync(byte[] data, int timeoutMs, CancellationToken token)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            return await ExecuteWriteAsync(
                timeoutMs,
                token,
                async (transport, cancellationToken) =>
                {
                    await transport.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                    return data.Length;
                }).ConfigureAwait(false);
        }

        private async Task<WriteResult> SendStreamInternalAsync(long contentLength, Stream stream, int timeoutMs, CancellationToken token)
        {
            return await ExecuteWriteAsync(
                timeoutMs,
                token,
                async (transport, cancellationToken) =>
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

        private async Task<WriteResult> ExecuteWriteAsync(int timeoutMs, CancellationToken callerToken, Func<Stream, CancellationToken, Task<long>> writer)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);
            bool semaphoreHeld = false;

            using (CancellationTokenSource linkedCts = CreateOperationTokenSource(callerToken, timeoutMs))
            {
                try
                {
                    await _WriteSemaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                    semaphoreHeld = true;

                    Stream transport = GetActiveTransportStream();
                    long bytesWritten = await writer(transport, linkedCts.Token).ConfigureAwait(false);

                    result.BytesWritten = bytesWritten;
                    _Statistics.AddSentBytes(bytesWritten);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.Status = GetWriteCancellationStatus(timeoutMs, callerToken);
                    if (result.Status == WriteResultStatus.Disconnected) _IsConnected = false;
                    return result;
                }
                catch (Exception)
                {
                    result.Status = WriteResultStatus.Disconnected;
                    _IsConnected = false;
                    return result;
                }
                finally
                {
                    if (semaphoreHeld) _WriteSemaphore.Release();
                }
            }
        }

        #endregion

        #region Read

        private ReadResult ReadWithoutTimeoutInternal(long count)
        {
            return ReadInternalAsync(count, -1, CancellationToken.None).GetAwaiter().GetResult();
        }

        private ReadResult ReadWithTimeoutInternal(int timeoutMs, long count)
        {
            return ReadInternalAsync(count, timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        }

        private async Task<ReadResult> ReadWithoutTimeoutInternalAsync(long count, CancellationToken token)
        {
            return await ReadInternalAsync(count, -1, token).ConfigureAwait(false);
        }

        private async Task<ReadResult> ReadWithTimeoutInternalAsync(int timeoutMs, long count, CancellationToken token)
        {
            return await ReadInternalAsync(count, timeoutMs, token).ConfigureAwait(false);
        }

        private async Task<ReadResult> ReadInternalAsync(long count, int timeoutMs, CancellationToken token)
        {
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);
            bool semaphoreHeld = false;

            if (count > Int32.MaxValue) throw new ArgumentOutOfRangeException(nameof(count), "Count must not exceed Int32.MaxValue.");

            int totalCount = (int)count;
            byte[] data = totalCount > 0 ? new byte[totalCount] : Array.Empty<byte>();
            int offset = 0;

            using (CancellationTokenSource linkedCts = CreateOperationTokenSource(token, timeoutMs))
            {
                try
                {
                    await _ReadSemaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                    semaphoreHeld = true;

                    Stream transport = GetActiveTransportStream();

                    while (offset < totalCount)
                    {
                        int readLength = Math.Min(_Settings.StreamBufferSize, totalCount - offset);
                        int bytesRead = await transport.ReadAsync(data, offset, readLength, linkedCts.Token).ConfigureAwait(false);
                        if (bytesRead <= 0)
                        {
                            _IsConnected = false;
                            return CreateReadResult(ReadResultStatus.Disconnected, data, offset);
                        }

                        offset += bytesRead;
                        _Statistics.AddReceivedBytes(bytesRead);
                    }

                    return CreateReadResult(ReadResultStatus.Success, data, offset);
                }
                catch (OperationCanceledException)
                {
                    ReadResultStatus status = GetReadCancellationStatus(timeoutMs, token);
                    if (status == ReadResultStatus.Disconnected) _IsConnected = false;
                    return CreateReadResult(status, data, offset);
                }
                catch (Exception)
                {
                    _IsConnected = false;
                    return CreateReadResult(ReadResultStatus.Disconnected, data, offset);
                }
                finally
                {
                    if (semaphoreHeld) _ReadSemaphore.Release();
                }
            }
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

        #endregion

        private CancellationTokenSource CreateOperationTokenSource(CancellationToken callerToken, int timeoutMs)
        {
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Token, callerToken);
            if (timeoutMs > 0) linkedCts.CancelAfter(timeoutMs);
            return linkedCts;
        }

        private WriteResultStatus GetWriteCancellationStatus(int timeoutMs, CancellationToken callerToken)
        {
            if (callerToken.IsCancellationRequested) return WriteResultStatus.Canceled;
            if (_Token.IsCancellationRequested || _Disposed || !_IsConnected) return WriteResultStatus.Disconnected;
            if (timeoutMs > 0) return WriteResultStatus.Timeout;
            return WriteResultStatus.Canceled;
        }

        private ReadResultStatus GetReadCancellationStatus(int timeoutMs, CancellationToken callerToken)
        {
            if (callerToken.IsCancellationRequested) return ReadResultStatus.Canceled;
            if (_Token.IsCancellationRequested || _Disposed || !_IsConnected) return ReadResultStatus.Disconnected;
            if (timeoutMs > 0) return ReadResultStatus.Timeout;
            return ReadResultStatus.Canceled;
        }

        private Stream GetActiveTransportStream()
        {
            if (_Ssl)
            {
                if (_SslStream == null) throw new IOException("SSL stream is not connected.");
                return _SslStream;
            }

            if (_NetworkStream == null) throw new IOException("Network stream is not connected.");
            return _NetworkStream;
        }

        #endregion
    }
}
