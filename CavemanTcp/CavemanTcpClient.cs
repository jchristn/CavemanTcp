using System;
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
        private X509Certificate2 _SslCert = null;
        private X509Certificate2Collection _SslCertCollection;

        private SemaphoreSlim _WriteSemaphore = new SemaphoreSlim(1, 1);
        private SemaphoreSlim _ReadSemaphore = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;
        private Task _ConnectionMonitor = null;

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
        /// Instantiates the TCP client without SSL.  Set the Connected, Disconnected, and DataReceived callbacks.  Once set, use Connect() to connect to the server.
        /// </summary>
        /// <param name="serverIpOrHostname">The server IP address or hostname.</param>
        /// <param name="port">The TCP port on which to connect.</param>
        public CavemanTcpClient(string serverIpOrHostname, int port)
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
                    }
                    else
                    {
                        // do not accept invalid SSL certificates
                        _SslStream = new SslStream(_NetworkStream, false);
                    }

                    _SslStream.AuthenticateAsClient(_ServerIp, _SslCertCollection, SslProtocols.Tls12, !_Settings.AcceptInvalidCertificates);

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
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                wh.Close();
            }

            _Statistics = new CavemanTcpStatistics();
            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token; 
            _ConnectionMonitor = Task.Run(() => ConnectionMonitor(), _Token); 
            _Events.HandleClientConnected(this); 
            _IsConnected = true;
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected) return;
            Dispose(true);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(string data)
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            ms.Write(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendWithoutTimeoutInternal(bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(byte[] data)
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream(); 
            ms.Write(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendWithoutTimeoutInternal(data.Length, ms); 
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(long contentLength, Stream stream)
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithoutTimeoutInternal(contentLength, stream);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="data">String data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult SendWithTimeout(int timeoutMs, string data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            ms.Write(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendWithTimeoutInternal(timeoutMs, bytes.Length, ms);
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
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendWithTimeoutInternal(timeoutMs, data.Length, ms);
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
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendWithTimeoutInternal(timeoutMs, contentLength, stream);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string data, CancellationToken token = default)
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            if (token == default(CancellationToken)) token = _Token;
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendWithoutTimeoutInternalAsync(bytes.Length, ms, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="data">Byte array containing data to send.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(byte[] data, CancellationToken token = default)
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (token == default(CancellationToken)) token = _Token;
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendWithoutTimeoutInternalAsync(data.Length, ms, token).ConfigureAwait(false);
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
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            if (token == default(CancellationToken)) token = _Token;
            return await SendWithoutTimeoutInternalAsync(contentLength, stream, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="data">String data.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string data, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            if (token == default(CancellationToken)) token = _Token;
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendWithTimeoutInternalAsync(timeoutMs, bytes.Length, ms, token).ConfigureAwait(false);
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
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (token == default(CancellationToken)) token = _Token;
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendWithTimeoutInternalAsync(timeoutMs, data.Length, ms, token).ConfigureAwait(false);
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
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (token == default(CancellationToken)) token = _Token;
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendWithTimeoutInternalAsync(timeoutMs, contentLength, stream, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read string from the server.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult Read(int count)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream."); 
            return ReadWithTimeoutInternal(-1, count); 
        }

        /// <summary>
        /// Read string from the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult ReadWithTimeout(int timeoutMs, int count)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream.");
            return ReadWithTimeoutInternal(timeoutMs, count);
        }

        /// <summary>
        /// Read string from the server.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadAsync(int count, CancellationToken token = default)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream.");
            if (token == default(CancellationToken)) token = _Token;
            return await ReadWithoutTimeoutInternalAsync(count, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read string from the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="token">Cancellation token for canceling the request.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadWithTimeoutAsync(int timeoutMs, int count, CancellationToken token = default)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream.");
            if (token == default(CancellationToken)) token = _Token;
            return await ReadWithTimeoutInternalAsync(timeoutMs, count, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Get direct access to the underlying client stream.
        /// </summary>
        /// <returns>Stream.</returns>
        public Stream GetStream()
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");  
            if (!_Ssl) return _NetworkStream; 
            else return _SslStream; 
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Dispose of the TCP client.
        /// </summary>
        /// <param name="disposing">Dispose of resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _WriteSemaphore.Wait();
                    _ReadSemaphore.Wait();

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
                }
                finally
                {
                    _ReadSemaphore.Release();
                    _WriteSemaphore.Release();
                }

                _IsConnected = false;
            }
        }

        private void EnableKeepalives()
        {
            try
            {
#if (NETCOREAPP3_0 || NETCOREAPP3_1 || NET5_0)

                _Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);

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
            _SslStream = null;
            _SslCert = null;
            _SslCertCollection = null;

            if (_Ssl)
            {
                if (String.IsNullOrEmpty(_PfxPassword))
                {
                    _SslCert = new X509Certificate2(_PfxCertFilename);
                }
                else
                {
                    _SslCert = new X509Certificate2(_PfxCertFilename, _PfxPassword);
                }

                _SslCertCollection = new X509Certificate2Collection
                {
                    _SslCert
                };
            }

            _Header = "[CavemanTcp.Client " + _ServerIp + ":" + _ServerPort + "] ";
        }

        private bool IsClientConnected()
        {
            if (_Client == null) return false;
            if (!_Client.Connected) return false;

            if ((_Client.Client.Poll(0, SelectMode.SelectWrite)) && (!_Client.Client.Poll(0, SelectMode.SelectError)))
            {
                byte[] buffer = new byte[1];
                if (_Client.Client.Receive(buffer, SocketFlags.Peek) == 0)
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

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        { 
            return _Settings.AcceptInvalidCertificates;
        }

        private async Task ConnectionMonitor()
        {
            try
            {
                while (!_TokenSource.IsCancellationRequested)
                {
                    await Task.Delay(1000).ConfigureAwait(false);

                    if (!IsClientConnected())
                    {
                        break;
                    }
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
                Logger?.Invoke(_Header + "connection monitor exception:" + Environment.NewLine + e.ToString());
            }
            finally
            {
                Logger?.Invoke(_Header + "disconnected");
                _Events.HandleClientDisconnected(this);
                Dispose(true); 
            }
        }

        #endregion

        #region Send

        private WriteResult SendWithoutTimeoutInternal(long contentLength, Stream stream)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);
             
            try
            {
                while (!_WriteSemaphore.Wait(10))
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
                                _NetworkStream.Write(buffer, 0, bytesRead);
                                _NetworkStream.Flush();
                            }
                            else
                            {
                                _SslStream.Write(buffer, 0, bytesRead);
                                _SslStream.Flush();
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
                _IsConnected = false;
                return result;
            }
            finally
            {
                _WriteSemaphore.Release();
            } 
        }

        private WriteResult SendWithTimeoutInternal(int timeoutMs, long contentLength, Stream stream)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            Task<WriteResult> task = Task.Run(() =>
            {
                try
                {
                    while (!_WriteSemaphore.Wait(10))
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
                                    _NetworkStream.Write(buffer, 0, bytesRead);
                                    _NetworkStream.Flush();
                                }
                                else
                                {
                                    _SslStream.Write(buffer, 0, bytesRead);
                                    _SslStream.Flush();
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
                    _IsConnected = false;
                    return result;
                }
                finally
                {
                    _WriteSemaphore.Release();
                }
            }, _Token);

            bool success = task.Wait(TimeSpan.FromMilliseconds(timeoutMs));

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

        private async Task<WriteResult> SendWithoutTimeoutInternalAsync(long contentLength, Stream stream, CancellationToken token)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);
             
            try
            {
                while (true)
                {
                    bool success = await _WriteSemaphore.WaitAsync(10).ConfigureAwait(false);
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
                                await _NetworkStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                await _NetworkStream.FlushAsync(token).ConfigureAwait(false);
                            }
                            else
                            {
                                await _SslStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                await _SslStream.FlushAsync(token).ConfigureAwait(false);
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
                _IsConnected = false;
                return result;
            }
            finally
            {
                _WriteSemaphore.Release();
            } 
        }

        private async Task<WriteResult> SendWithTimeoutInternalAsync(int timeoutMs, long contentLength, Stream stream, CancellationToken token)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            CancellationTokenSource timeoutCts = new CancellationTokenSource();
            CancellationToken timeoutToken = timeoutCts.Token;

            Task<WriteResult> task = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        bool success = await _WriteSemaphore.WaitAsync(10, token).ConfigureAwait(false);
                        if (success) break;
                        await Task.Delay(10, token).ConfigureAwait(false);
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
                                    await _NetworkStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                    await _NetworkStream.FlushAsync(token).ConfigureAwait(false);
                                }
                                else
                                {
                                    await _SslStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                                    await _SslStream.FlushAsync(token).ConfigureAwait(false);
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
                    _IsConnected = false;
                    return result;
                }
                finally
                {
                    _WriteSemaphore.Release();
                }
            },
            token);

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

        private ReadResult ReadWithoutTimeoutInternal(long count)
        {
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);
             
            try
            {
                while (!_ReadSemaphore.Wait(10))
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
                    if (!_Ssl) bytesRead = _NetworkStream.Read(buffer, 0, buffer.Length);
                    else bytesRead = _SslStream.Read(buffer, 0, buffer.Length);

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
                _IsConnected = false;
                _Events.HandleClientDisconnected(this);
                throw;
            }
            finally
            {
                _ReadSemaphore.Release();
            } 
        }

        private ReadResult ReadWithTimeoutInternal(int timeoutMs, long count)
        {
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            Task<ReadResult> task = Task.Run(() =>
            {
                try
                {
                    while (!_ReadSemaphore.Wait(10))
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
                        if (!_Ssl) bytesRead = _NetworkStream.Read(buffer, 0, buffer.Length);
                        else bytesRead = _SslStream.Read(buffer, 0, buffer.Length);

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
                    _IsConnected = false;
                    _Events.HandleClientDisconnected(this);
                    throw;
                }
                finally
                {
                    _ReadSemaphore.Release();
                }
            }, _Token);

            bool success = task.Wait(TimeSpan.FromMilliseconds(timeoutMs));

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

        private async Task<ReadResult> ReadWithoutTimeoutInternalAsync(long count, CancellationToken token)
        {
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);
              
            try
            {
                while (true)
                {
                    bool success = await _ReadSemaphore.WaitAsync(10, token).ConfigureAwait(false);
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
                    if (!_Ssl) bytesRead = await _NetworkStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    else bytesRead = await _SslStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

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
                result.DataStream = null;
                return result;
            }
            finally
            {
                _ReadSemaphore.Release();
            } 
        }

        private async Task<ReadResult> ReadWithTimeoutInternalAsync(int timeoutMs, long count, CancellationToken token)
        {
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            CancellationTokenSource timeoutCts = new CancellationTokenSource();
            CancellationToken timeoutToken = timeoutCts.Token;

            Task<ReadResult> task = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        bool success = await _ReadSemaphore.WaitAsync(10, token).ConfigureAwait(false);
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
                        if (!_Ssl) bytesRead = await _NetworkStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        else bytesRead = await _SslStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);

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
                    result.DataStream = null;
                    return result;
                }
                finally
                {
                    _ReadSemaphore.Release();
                }
            },
            token);

            Task delay = Task.Delay(timeoutMs, token);
            Task first = await Task.WhenAny(task, delay).ConfigureAwait(false);
            timeoutCts.Cancel();

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

        #endregion
    }
}
