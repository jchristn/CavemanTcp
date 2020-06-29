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
    public class TcpClient : IDisposable
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
        /// Buffer size to use while interacting with streams.
        /// </summary>
        public int StreamBufferSize
        {
            get
            {
                return _StreamBufferSize;
            }
            set
            {
                if (value < 1) throw new ArgumentException("StreamBufferSize must be greater than zero.");
                _StreamBufferSize = value;
            }
        }

        /// <summary>
        /// Event to fire when the client connects.
        /// </summary>
        public event EventHandler ClientConnected;

        /// <summary>
        /// Event to fire when the client disconnects.
        /// </summary>
        public event EventHandler ClientDisconnected;

        /// <summary>
        /// Enable or disable acceptance of invalid SSL certificates.
        /// </summary>
        public bool AcceptInvalidCertificates = true;

        /// <summary>
        /// Enable or disable mutual authentication of SSL client and server.
        /// </summary>
        public bool MutuallyAuthenticate = true;

        /// <summary>
        /// Method to invoke when sending log messages.
        /// </summary>
        public Action<string> Logger = null;

        /// <summary>
        /// CavemanTcp statistics.
        /// </summary>
        public Statistics Stats = new Statistics();

        #endregion

        #region Private-Members

        private bool _IsConnected = false;
        private int _StreamBufferSize = 65536;
        private string _Header = "[CavemanTcp.Client] ";
        private string _ServerIp = null;
        private int _ServerPort = 0;
        private IPAddress _IPAddress;
        private System.Net.Sockets.TcpClient _TcpClient = null;
        private NetworkStream _NetworkStream = null;

        private bool _Ssl = false;
        private string _PfxCertFilename = null;
        private string _PfxPassword = null;
        private SslStream _SslStream = null;
        private X509Certificate2 _SslCert = null;
        private X509Certificate2Collection _SslCertCollection;

        private SemaphoreSlim _WriteSemaphore = new SemaphoreSlim(1);
        private SemaphoreSlim _ReadSemaphore = new SemaphoreSlim(1);
         
        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiates the TCP client.  Set the Connected, Disconnected, and DataReceived callbacks.  Once set, use Connect() to connect to the server.
        /// </summary>
        /// <param name="serverIpOrHostname">The server IP address or hostname.</param>
        /// <param name="port">The TCP port on which to connect.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="pfxCertFilename">The filename of the PFX certificate file.</param>
        /// <param name="pfxPassword">The password to the PFX certificate file.</param>
        public TcpClient(string serverIpOrHostname, int port, bool ssl, string pfxCertFilename, string pfxPassword)
        {
            if (String.IsNullOrEmpty(serverIpOrHostname)) throw new ArgumentNullException(nameof(serverIpOrHostname));
            if (port < 0) throw new ArgumentException("Port must be zero or greater.");
               
            _ServerIp = serverIpOrHostname;

            if (!IPAddress.TryParse(_ServerIp, out _IPAddress))
            {
                _IPAddress = Dns.GetHostEntry(serverIpOrHostname).AddressList[0];
            }
            
            _ServerPort = port;
            _Ssl = ssl;
            _PfxCertFilename = pfxCertFilename;
            _PfxPassword = pfxPassword; 
            _TcpClient = new System.Net.Sockets.TcpClient(); 
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

            IAsyncResult ar = _TcpClient.BeginConnect(_ServerIp, _ServerPort, null, null);
            WaitHandle wh = ar.AsyncWaitHandle;

            try
            {
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds), false))
                {
                    _TcpClient.Close();
                    throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                }

                _TcpClient.EndConnect(ar); 
                _NetworkStream = _TcpClient.GetStream();

                if (_Ssl)
                {
                    if (AcceptInvalidCertificates)
                    {
                        // accept invalid certs
                        _SslStream = new SslStream(_NetworkStream, false, new RemoteCertificateValidationCallback(AcceptCertificate));
                    }
                    else
                    {
                        // do not accept invalid SSL certificates
                        _SslStream = new SslStream(_NetworkStream, false);
                    }

                    _SslStream.AuthenticateAsClient(_ServerIp, _SslCertCollection, SslProtocols.Tls12, !AcceptInvalidCertificates);

                    if (!_SslStream.IsEncrypted)
                    {
                        throw new AuthenticationException("Stream is not encrypted");
                    }

                    if (!_SslStream.IsAuthenticated)
                    {
                        throw new AuthenticationException("Stream is not authenticated");
                    }

                    if (MutuallyAuthenticate && !_SslStream.IsMutuallyAuthenticated)
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

            Stats = new Statistics();
            Logger?.Invoke("Starting connection monitor for: " + _ServerIp + ":" + _ServerPort);
            Task unawaited = Task.Run(() => ClientConnectionMonitor());
            ClientConnected?.Invoke(this, EventArgs.Empty);
            
            _IsConnected = true;
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(string data)
        {
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            ms.Write(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendInternal(bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(byte[] data)
        {
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream(); 
            ms.Write(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return SendInternal(data.Length, ms); 
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>WriteResult.</returns>
        public WriteResult Send(long contentLength, Stream stream)
        {
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return SendInternal(contentLength, stream);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string data)
        {
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(byte[] data)
        {
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(data.Length, ms);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(long contentLength, Stream stream)
        {
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendInternalAsync(contentLength, stream);
        }

        /// <summary>
        /// Read string from the server.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public ReadResult Read(int count)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream."); 
            return ReadInternal(count); 
        }
          
        /// <summary>
        /// Read string from the server.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadAsync(int count)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream."); 
            return await ReadInternalAsync(count);
        }
          
        /// <summary>
        /// Get direct access to the underlying client stream.
        /// </summary>
        /// <returns>Stream.</returns>
        public Stream GetStream()
        {
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");  
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

                    if (_TcpClient != null)
                    {
                        _TcpClient.Close();
                        _TcpClient.Dispose();
                        _TcpClient = null;
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

        private bool IsClientConnected()
        {
            if (_TcpClient == null) return false;
            if (!_TcpClient.Connected) return false;

            if ((_TcpClient.Client.Poll(0, SelectMode.SelectWrite)) && (!_TcpClient.Client.Poll(0, SelectMode.SelectError)))
            {
                byte[] buffer = new byte[1];
                if (_TcpClient.Client.Receive(buffer, SocketFlags.Peek) == 0)
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
            return AcceptInvalidCertificates;
        }

        private void ClientConnectionMonitor()
        {
            while (true)
            {
                Task.Delay(1000).Wait();
                 
                if (!IsClientConnected())
                {
                    Logger?.Invoke(_Header + "Client no longer connected to server");
                    ClientDisconnected?.Invoke(this, EventArgs.Empty);
                    Dispose(true);
                    break;
                }
            }
        }

        private WriteResult SendInternal(long contentLength, Stream stream)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            try
            {
                _WriteSemaphore.Wait(1); 

                if (contentLength > 0 && stream != null && stream.CanRead)
                { 
                    long bytesRemaining = contentLength;

                    while (bytesRemaining > 0)
                    {
                        byte[] buffer = new byte[_StreamBufferSize]; 
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
                            Stats.SentBytes += bytesRead;
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
        }

        private async Task<WriteResult> SendInternalAsync(long contentLength, Stream stream)
        {
            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            try
            {
                await _WriteSemaphore.WaitAsync(1);

                if (contentLength > 0 && stream != null && stream.CanRead)
                { 
                    long bytesRemaining = contentLength;

                    while (bytesRemaining > 0)
                    {
                        byte[] buffer = new byte[_StreamBufferSize];
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
                            Stats.SentBytes += bytesRead;
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
        }

        private ReadResult ReadInternal(long count)
        { 
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            try
            {
                _ReadSemaphore.Wait(1);
                MemoryStream ms = new MemoryStream();
                long bytesRemaining = count;

                while (bytesRemaining > 0)
                {
                    byte[] buffer = null;
                    if (bytesRemaining >= _StreamBufferSize) buffer = new byte[_StreamBufferSize];
                    else buffer = new byte[bytesRemaining];

                    int bytesRead = 0;
                    if (!_Ssl) bytesRead = _NetworkStream.Read(buffer, 0, buffer.Length);
                    else bytesRead = _SslStream.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        if (bytesRead == buffer.Length) ms.Write(buffer, 0, buffer.Length);
                        else ms.Write(buffer, 0, bytesRead);

                        result.BytesRead += bytesRead;
                        Stats.ReceivedBytes += bytesRead;
                        bytesRemaining -= bytesRead;
                    }
                }

                ms.Seek(0, SeekOrigin.Begin);
                result.DataStream = ms; 
                return result;
            }
            catch (ObjectDisposedException)
            {
                ClientDisconnected?.Invoke(this, EventArgs.Empty);
                _IsConnected = false;
                throw;
            }
            catch (Exception)
            {
                ClientDisconnected?.Invoke(this, EventArgs.Empty);
                _IsConnected = false;
                throw;
            }
            finally
            {
                _ReadSemaphore.Release();
            }
        }

        private async Task<ReadResult> ReadInternalAsync(long count)
        {
            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            try
            {
                await _ReadSemaphore.WaitAsync(1);
                MemoryStream ms = new MemoryStream();
                long bytesRemaining = count;

                while (bytesRemaining > 0)
                {
                    byte[] buffer = null;
                    if (bytesRemaining >= _StreamBufferSize) buffer = new byte[_StreamBufferSize];
                    else buffer = new byte[bytesRemaining];

                    int bytesRead = 0;
                    if (!_Ssl) bytesRead = await _NetworkStream.ReadAsync(buffer, 0, buffer.Length);
                    else bytesRead = await _SslStream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        if (bytesRead == buffer.Length) await ms.WriteAsync(buffer, 0, buffer.Length);
                        else await ms.WriteAsync(buffer, 0, bytesRead);

                        result.BytesRead += bytesRead; 
                        Stats.ReceivedBytes += bytesRead;
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
        }

        #endregion
    }
}
