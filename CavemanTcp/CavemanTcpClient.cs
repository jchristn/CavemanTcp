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
        public CavemanTcpStatistics Statistics = new CavemanTcpStatistics();

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

        #endregion

        #region Constructors-and-Factories

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
            }

            InitializeClient(ssl, pfxCertFilename, pfxPassword); 
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
            }

            _ServerPort = port;

            InitializeClient(ssl, pfxCertFilename, pfxPassword); 
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
            if (timeoutSeconds < 1) throw new ArgumentException("TimeoutSeconds must be greater than zero seconds.");

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

            Statistics = new CavemanTcpStatistics();
            Logger?.Invoke("Starting connection monitor for: " + _ServerIp + ":" + _ServerPort);
            Task unawaited = Task.Run(() => ClientConnectionMonitor());
            
            _Events.HandleClientConnected(this);
            
            _IsConnected = true;
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
            return SendInternal(-1, bytes.Length, ms);
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
            return SendInternal(-1, data.Length, ms); 
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
            return SendInternal(-1, contentLength, stream);
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
            return SendInternal(timeoutMs, bytes.Length, ms);
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
            return SendInternal(timeoutMs, data.Length, ms);
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
            return SendInternal(timeoutMs, contentLength, stream);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string data)
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(-1, bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(byte[] data)
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(-1, data.Length, ms);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(long contentLength, Stream stream)
        {
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendInternalAsync(-1, contentLength, stream);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="data">String data.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(timeoutMs, bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, byte[] data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(timeoutMs, data.Length, ms);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, long contentLength, Stream stream)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendInternalAsync(timeoutMs, contentLength, stream);
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
            return ReadInternal(-1, count); 
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
            return ReadInternal(timeoutMs, count);
        }

        /// <summary>
        /// Read string from the server.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadAsync(int count)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream."); 
            return await ReadInternalAsync(-1, count);
        }

        /// <summary>
        /// Read string from the server.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadWithTimeoutAsync(int timeoutMs, int count)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_Client == null || !_Client.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream.");
            return await ReadInternalAsync(timeoutMs, count);
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

        private void InitializeClient(bool ssl, string pfxCertFilename, string pfxPassword)
        {
            _Ssl = ssl;
            _PfxCertFilename = pfxCertFilename;
            _PfxPassword = pfxPassword;
            _Client = new System.Net.Sockets.TcpClient();
            _SslStream = null;
            _SslCert = null;
            _SslCertCollection = null;

            if (_Ssl)
            {
                if (String.IsNullOrEmpty(pfxPassword))
                {
                    _SslCert = new X509Certificate2(pfxCertFilename);
                }
                else
                {
                    _SslCert = new X509Certificate2(pfxCertFilename, pfxPassword);
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

        private void ClientConnectionMonitor()
        {
            while (true)
            {
                Task.Delay(1000).Wait();
                 
                if (!IsClientConnected())
                {
                    Logger?.Invoke(_Header + "Client no longer connected to server");
                    _Events.HandleClientDisconnected(this);
                    Dispose(true);
                    break;
                }
            }
        }

        private WriteResult SendInternal(int timeoutMs, long contentLength, Stream stream)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            Task<WriteResult> task = Task.Run(() =>
            { 
                try
                {
                    _WriteSemaphore.Wait(1);

                    if (contentLength > 0 && stream != null && stream.CanRead)
                    {
                        long bytesRemaining = contentLength;

                        while (bytesRemaining > 0)
                        {
                            byte[] buffer = new byte[_Settings.StreamBufferSize];
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                byte[] data = null;
                                if (bytesRead == buffer.Length)
                                {
                                    data = new byte[buffer.Length];
                                    Buffer.BlockCopy(buffer, 0, data, 0, buffer.Length);
                                }
                                else
                                {
                                    data = new byte[bytesRead];
                                    Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);
                                }

                                if (!_Ssl)
                                {
                                    _NetworkStream.Write(data, 0, data.Length);
                                    _NetworkStream.Flush();
                                }
                                else
                                {
                                    _SslStream.Write(data, 0, data.Length);
                                    _SslStream.Flush();
                                }

                                result.BytesWritten += bytesRead;
                                Statistics.SentBytes += bytesRead;
                                bytesRemaining -= bytesRead;
                            }
                        }
                    }

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
            });

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

        private async Task<WriteResult> SendInternalAsync(int timeoutMs, long contentLength, Stream stream)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            Task<WriteResult> task = Task.Run(async () =>
            {
                try
                {
                    await _WriteSemaphore.WaitAsync(1);

                    if (contentLength > 0 && stream != null && stream.CanRead)
                    {
                        long bytesRemaining = contentLength;

                        while (bytesRemaining > 0)
                        {
                            byte[] buffer = new byte[_Settings.StreamBufferSize];
                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                byte[] data = null;
                                if (bytesRead == buffer.Length)
                                {
                                    data = new byte[buffer.Length];
                                    Buffer.BlockCopy(buffer, 0, data, 0, buffer.Length);
                                }
                                else
                                {
                                    data = new byte[bytesRead];
                                    Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);
                                }

                                if (!_Ssl)
                                {
                                    _NetworkStream.Write(data, 0, data.Length);
                                    await _NetworkStream.FlushAsync();
                                }
                                else
                                {
                                    _SslStream.Write(data, 0, data.Length);
                                    await _SslStream.FlushAsync();
                                }

                                result.BytesWritten += bytesRead;
                                Statistics.SentBytes += bytesRead;
                                bytesRemaining -= bytesRead;
                            }
                        }
                    }

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

            Task delay = Task.Delay(timeoutMs, token);
            Task first = await Task.WhenAny(task, delay);
            tokenSource.Cancel();

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

        private ReadResult ReadInternal(int timeoutMs, long count)
        { 
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            Task<ReadResult> task = Task.Run(() =>
            {
                try
                {
                    _ReadSemaphore.Wait(1);
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
                            if (bytesRead == buffer.Length) ms.Write(buffer, 0, buffer.Length);
                            else ms.Write(buffer, 0, bytesRead);

                            result.BytesRead += bytesRead;
                            Statistics.ReceivedBytes += bytesRead;
                            bytesRemaining -= bytesRead;
                        }
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    result.DataStream = ms;
                    return result;
                }
                catch (Exception)
                { 
                    _IsConnected = false;
                    throw;
                }
                finally
                {
                    _Events.HandleClientDisconnected(this);
                    _ReadSemaphore.Release();
                }
            });

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

        private async Task<ReadResult> ReadInternalAsync(int timeoutMs, long count)
        {
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            Task<ReadResult> task = Task.Run(async () =>
            {
                try
                {
                    await _ReadSemaphore.WaitAsync(1);
                    MemoryStream ms = new MemoryStream();
                    long bytesRemaining = count;

                    while (bytesRemaining > 0)
                    {
                        byte[] buffer = null;
                        if (bytesRemaining >= _Settings.StreamBufferSize) buffer = new byte[_Settings.StreamBufferSize];
                        else buffer = new byte[bytesRemaining];

                        int bytesRead = 0;
                        if (!_Ssl) bytesRead = await _NetworkStream.ReadAsync(buffer, 0, buffer.Length);
                        else bytesRead = await _SslStream.ReadAsync(buffer, 0, buffer.Length);

                        if (bytesRead > 0)
                        {
                            if (bytesRead == buffer.Length) await ms.WriteAsync(buffer, 0, buffer.Length);
                            else await ms.WriteAsync(buffer, 0, bytesRead);

                            result.BytesRead += bytesRead;
                            Statistics.ReceivedBytes += bytesRead;
                            bytesRemaining -= bytesRead;
                        }
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    result.DataStream = ms;
                    return result;
                }
                catch (ObjectDisposedException)
                {
                    result.Status = ReadResultStatus.Disconnected;
                    result.DataStream = null;
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
            Task first = await Task.WhenAny(task, delay);
            tokenSource.Cancel();

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

        private void EnableKeepalives()
        {
#if NETCOREAPP

            _Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime); 
            _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);
            _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);

#elif NETFRAMEWORK 

            byte[] keepAlive = new byte[12];

            // Turn keepalive on
            Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4);

            // Set TCP keepalive time
            Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveTime), 0, keepAlive, 4, 4); 

            // Set TCP keepalive interval
            Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveInterval), 0, keepAlive, 8, 4); 

            // Set keepalive settings on the underlying Socket
            _Client.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

#elif NETSTANDARD

#endif
        }

        #endregion
    }
}
