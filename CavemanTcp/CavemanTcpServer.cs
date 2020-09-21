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

        private CavemanTcpServerSettings _Settings = new CavemanTcpServerSettings();
        private CavemanTcpServerEvents _Events = new CavemanTcpServerEvents();
        private CavemanTcpKeepaliveSettings _Keepalive = new CavemanTcpKeepaliveSettings();

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

        private readonly object _ClientsLock = new object();
        private Dictionary<string, ClientMetadata> _Clients = new Dictionary<string, ClientMetadata>();
         
        #endregion

        #region tructors-and-Factories

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
            _Token = _TokenSource.Token;
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
        /// Start the TCP server and begin accepting connections.
        /// </summary>
        public void Start()
        {
            if (_IsListening) throw new InvalidOperationException("TcpServer is already running.");

            _Listener = new TcpListener(_IPAddress, _Port);

            if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives();

            _Listener.Start();

            _Clients = new Dictionary<string, ClientMetadata>();

            Statistics = new CavemanTcpStatistics();
            Task.Run(() => AcceptConnections(), _Token);

            _IsListening = true;
        }

        /// <summary>
        /// Retrieve a list of client IP:port connected to the server.
        /// </summary>
        /// <returns>IEnumerable of strings, each containing client IP:port.</returns>
        public IEnumerable<string> GetClients()
        {
            List<string> clients = new List<string>(_Clients.Keys);
            return clients;
        }

        /// <summary>
        /// Determines if a client is connected by its IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <returns>True if connected.</returns>
        public bool IsConnected(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));

            lock (_ClientsLock)
            {
                return (_Clients.ContainsKey(ipPort));
            }
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
            return SendInternal(-1, ipPort, bytes.Length, ms);
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
            return SendInternal(-1, ipPort, data.Length, ms);
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
            return SendInternal(-1, ipPort, contentLength, stream);
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
            return SendInternal(timeoutMs, ipPort, bytes.Length, ms);
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
            return SendInternal(timeoutMs, ipPort, data.Length, ms);
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
            return SendInternal(timeoutMs, ipPort, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">String containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string ipPort, string data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(-1, ipPort, bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string ipPort, byte[] data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(-1, ipPort, data.Length, ms);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendAsync(string ipPort, long contentLength, Stream stream)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort)); 
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendInternalAsync(-1, ipPort, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">String containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, string data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            await ms.WriteAsync(bytes, 0, bytes.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(timeoutMs, ipPort, bytes.Length, ms);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">Byte array containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, byte[] data)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            MemoryStream ms = new MemoryStream();
            await ms.WriteAsync(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return await SendInternalAsync(timeoutMs, ipPort, data.Length, ms);
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="contentLength">Number of bytes to send from the stream.</param>
        /// <param name="stream">Stream containing data to send.</param>
        /// <returns>WriteResult.</returns>
        public async Task<WriteResult> SendWithTimeoutAsync(int timeoutMs, string ipPort, long contentLength, Stream stream)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (contentLength < 1) throw new ArgumentException("No data supplied in stream.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Cannot read from supplied stream.");
            return await SendInternalAsync(timeoutMs, ipPort, contentLength, stream);
        }

        /// <summary>
        /// Disconnects the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the client.</param>
        public void DisconnectClient(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));

            lock (_ClientsLock)
            { 
                if (_Clients.ContainsKey(ipPort))
                {
                    ClientMetadata client = _Clients[ipPort];
                    client.Dispose();
                    _Clients.Remove(ipPort);
                }

                Logger?.Invoke(_Header + "Removed: " + ipPort); 
            }

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
            return ReadInternal(-1, ipPort, count);
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
            return ReadInternal(timeoutMs, ipPort, count);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadAsync(string ipPort, int count)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return await ReadInternalAsync(-1, ipPort, count);
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="timeoutMs">The number of milliseconds to wait before timing out the operation.  -1 indicates no timeout, otherwise the value must be a non-zero positive integer.</param>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>ReadResult.</returns>
        public async Task<ReadResult> ReadWithTimeoutAsync(int timeoutMs, string ipPort, int count)
        {
            if (timeoutMs < -1 || timeoutMs == 0) throw new ArgumentException("TimeoutMs must be -1 (no timeout) or a positive integer.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            return await ReadInternalAsync(timeoutMs, ipPort, count);
        }

        /// <summary>
        /// Get direct access to the underlying client stream.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <returns>Stream.</returns>
        public Stream GetStream(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));

            ClientMetadata client = null;

            lock (_ClientsLock)
            {
                if (!_Clients.ContainsKey(ipPort))
                {
                    throw new KeyNotFoundException("Client with IP:port of " + ipPort + " not found.");
                }

                client = _Clients[ipPort];
            }

            if (!_Ssl) return client.NetworkStream;
            else return client.SslStream;
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
                try
                {
                    if (_Clients != null && _Clients.Count > 0)
                    {
                        foreach (KeyValuePair<string, ClientMetadata> curr in _Clients)
                        {
                            curr.Value.Dispose();
                            Logger?.Invoke(_Header + "Disconnected client: " + curr.Key);
                        }
                    }

                    _TokenSource.Cancel();
                    _TokenSource.Dispose();

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
                    Logger?.Invoke(_Header + "Dispose exception:" +
                        Environment.NewLine +
                        e.ToString() +
                        Environment.NewLine);
                }

                _IsListening = false;
            }
        }
         
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

        private async void AcceptConnections()
        {
            while (!_Token.IsCancellationRequested)
            {
                ClientMetadata client = null;

                try
                {
                    System.Net.Sockets.TcpClient tcpClient = await _Listener.AcceptTcpClientAsync(); 
                    client = new ClientMetadata(tcpClient); 
                    Task clientTask = Task.Run(async () =>
                    {
                        string clientIp = tcpClient.Client.RemoteEndPoint.ToString();

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

                            bool success = await StartTls(client);
                            if (!success)
                            {
                                client.Dispose();
                                return;
                            }
                        }

                        lock (_Clients)
                        {
                            _Clients.Add(clientIp, client);
                        }

                        if (_Settings.MonitorClientConnections)
                        {
                            Logger?.Invoke(_Header + "Starting connection monitor for: " + clientIp);
                            Task clientMonitorTask = Task.Run(() => ClientConnectionMonitor(client), client.Token);
                        }

                        _Events.HandleClientConnected(this, new ClientConnectedEventArgs(clientIp));
                    }, 
                    client.Token);

                }
                catch (OperationCanceledException)
                {
                    if (client != null) client.Dispose();
                    return;
                }
                catch (ObjectDisposedException)
                {
                    if (client != null) client.Dispose();
                    continue;
                }
                catch (Exception e)
                {
                    if (client != null) client.Dispose();
                    Logger?.Invoke(_Header + "Exception while awaiting connections: " + e.ToString());
                    continue;
                } 
            }

            _IsListening = false;
        }

        private async Task<bool> StartTls(ClientMetadata client)
        {
            try
            { 
                await client.SslStream.AuthenticateAsServerAsync(
                    _SslCertificate,
                    _Settings.MutuallyAuthenticate, 
                    SslProtocols.Tls12, 
                    !_Settings.AcceptInvalidCertificates);

                if (!client.SslStream.IsEncrypted)
                {
                    Logger?.Invoke(_Header + "Client " + client.IpPort + " not encrypted, disconnecting"); 
                    return false;
                }

                if (!client.SslStream.IsAuthenticated)
                {
                    Logger?.Invoke(_Header + "Client " + client.IpPort + " not SSL/TLS authenticated, disconnecting"); 
                    return false;
                }

                if (_Settings.MutuallyAuthenticate && !client.SslStream.IsMutuallyAuthenticated)
                {
                    Logger?.Invoke(_Header + "Client " + client.IpPort + " failed mutual authentication, disconnecting"); 
                    return false;
                }
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "Client " + client.IpPort + " SSL/TLS exception: " + Environment.NewLine + e.ToString()); 
                return false;
            }

            return true;
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return _Settings.AcceptInvalidCertificates;
        }
          
        private void ClientConnectionMonitor(ClientMetadata client)
        {
            string ipPort = client.IpPort;

            while (true)
            {
                Task.Delay(1000).Wait();

                if (client == null || client.Client == null || !IsClientConnected(client.Client))
                {
                    Logger?.Invoke(_Header + "Client " + ipPort + " no longer connected");
                    DisconnectClient(ipPort);
                    break;
                }
            }
        }
         
        private WriteResult SendInternal(int timeoutMs, string ipPort, long contentLength, Stream stream)
        {
            ClientMetadata client = null;

            lock (_ClientsLock)
            {
                if (!_Clients.ContainsKey(ipPort))
                {
                    return new WriteResult(WriteResultStatus.ClientNotFound, 0);
                }

                client = _Clients[ipPort];
            }

            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            Task<WriteResult> task = Task.Run(() =>
            {
                try
                {
                    client.WriteSemaphore.Wait(1);

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
                                    client.NetworkStream.Write(data, 0, data.Length);
                                    client.NetworkStream.Flush();
                                }
                                else
                                {
                                    client.SslStream.Write(data, 0, data.Length);
                                    client.SslStream.Flush();
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
                    return result;
                }
                finally
                {
                    if (client != null) client.WriteSemaphore.Release();
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

        private async Task<WriteResult> SendInternalAsync(int timeoutMs, string ipPort, long contentLength, Stream stream)
        {
            ClientMetadata client = null;

            lock (_ClientsLock)
            {
                if (!_Clients.ContainsKey(ipPort))
                {
                    return new WriteResult(WriteResultStatus.ClientNotFound, 0);
                }

                client = _Clients[ipPort];
            }

            WriteResult result = new WriteResult(WriteResultStatus.Success, 0);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            Task<WriteResult> task = Task.Run(async () =>
            {
                try
                {
                    client.WriteSemaphore.Wait(1);

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
                                    await client.NetworkStream.WriteAsync(data, 0, data.Length);
                                    await client.NetworkStream.FlushAsync();
                                }
                                else
                                {
                                    await client.SslStream.WriteAsync(data, 0, data.Length);
                                    await client.SslStream.FlushAsync();
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
                    return result;
                }
                finally
                {
                    if (client != null) client.WriteSemaphore.Release();
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
         
        private ReadResult ReadInternal(int timeoutMs, string ipPort, long count)
        {
            if (count < 1) return new ReadResult(ReadResultStatus.Success, 0, null);

            ClientMetadata client = null;

            lock (_ClientsLock)
            {
                if (!_Clients.ContainsKey(ipPort))
                {
                    return new ReadResult(ReadResultStatus.ClientNotFound, 0, null); 
                }

                client = _Clients[ipPort];
            }

            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            Task<ReadResult> task = Task.Run(() =>
            {
                try
                {
                    client.ReadSemaphore.Wait(1);
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
                    result.Status = ReadResultStatus.Disconnected;
                    result.DataStream = null;
                    return result;
                }
                finally
                {
                    if (client != null) client.ReadSemaphore.Release();
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

        private async Task<ReadResult> ReadInternalAsync(int timeoutMs, string ipPort, long count)
        {
            if (count < 1) return new ReadResult(ReadResultStatus.Success, 0, null);

            ClientMetadata client = null;

            lock (_ClientsLock)
            {
                if (!_Clients.ContainsKey(ipPort))
                {
                    return new ReadResult(ReadResultStatus.ClientNotFound, 0, null);
                }

                client = _Clients[ipPort];
            }

            ReadResult result = new ReadResult(ReadResultStatus.Success, 0, null);

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            Task<ReadResult> task = Task.Run(async () =>
            {
                try
                {
                    await client.ReadSemaphore.WaitAsync(1);
                    MemoryStream ms = new MemoryStream();
                    long bytesRemaining = count;

                    while (bytesRemaining > 0)
                    {
                        byte[] buffer = null;
                        if (bytesRemaining >= _Settings.StreamBufferSize) buffer = new byte[_Settings.StreamBufferSize];
                        else buffer = new byte[bytesRemaining];

                        int bytesRead = 0;
                        if (!_Ssl) bytesRead = await client.NetworkStream.ReadAsync(buffer, 0, buffer.Length);
                        else bytesRead = await client.SslStream.ReadAsync(buffer, 0, buffer.Length);

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
                catch (Exception)
                {
                    result.Status = ReadResultStatus.Disconnected;
                    result.BytesRead = 0;
                    result.DataStream = null;
                    return result;
                }
                finally
                {
                    if (client != null) client.ReadSemaphore.Release();
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
            try
            {
#if NETCOREAPP

                _Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);

#elif NETFRAMEWORK

            byte[] keepAlive = new byte[12];

            // Turn keepalive on
            Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4);

            // Set TCP keepalive time
            Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveTime), 0, keepAlive, 4, 4); 

            // Set TCP keepalive interval
            Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveInterval), 0, keepAlive, 8, 4); 

            // Set keepalive settings on the underlying Socket
            _Listener.Server.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

#elif NETSTANDARD

#endif
            }
            catch (Exception)
            {
                Logger?.Invoke(_Header + "keepalives not supported on this platform, disabled");
            }
        }

        #endregion
    }
}
