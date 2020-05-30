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
    public class TcpServer : IDisposable
    {
        #region Public-Members
          
        /// <summary>
        /// Enable or disable acceptance of invalid SSL certificates.
        /// </summary>
        public bool AcceptInvalidCertificates = true;

        /// <summary>
        /// Enable or disable mutual authentication of SSL client and server.
        /// </summary>
        public bool MutuallyAuthenticate = true;

        /// <summary>
        /// Event to fire when a client connects.  A string containing the client IP:port will be passed.
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs> ClientConnected;

        /// <summary>
        /// Event to fire when a client disconnects.  A string containing the client IP:port will be passed.
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;

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

        private string _Header = "[CavemanTcp.Server] ";
        private bool _Running = false;
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

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiates the TCP server.  Set the ClientConnected, ClientDisconnected, and DataReceived callbacks.  Once set, use Start() to begin listening for connections.
        /// </summary>
        /// <param name="listenerIp">The listener IP address or hostname.</param>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="pfxCertFilename">The filename of the PFX certificate file.</param>
        /// <param name="pfxPassword">The password to the PFX certificate file.</param>
        public TcpServer(string listenerIp, int port, bool ssl, string pfxCertFilename, string pfxPassword)
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
            _Running = false;

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
            if (_Running) throw new InvalidOperationException("TcpServer is already running.");

            _Listener = new TcpListener(_IPAddress, _Port);
            _Listener.Start();

            _Clients = new Dictionary<string, ClientMetadata>();

            Stats = new Statistics();
            Task.Run(() => AcceptConnections(), _Token);
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
        /// <param name="data">Byte array containing data to send.</param>
        public void Send(string ipPort, byte[] data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));

            ClientMetadata client = null;

            lock (_ClientsLock)
            {
                if (!_Clients.ContainsKey(ipPort))
                {
                    throw new KeyNotFoundException("Client with IP:port of " + ipPort + " not found.");
                }

                client = _Clients[ipPort];
            }

            lock (client.SendLock)
            {
                try
                {
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
                }
                catch (Exception)
                {
                    ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(ipPort, DisconnectReason.Normal));
                    throw;
                }
            }

            Stats.SentBytes += data.Length;
        }

        /// <summary>
        /// Send data to the specified client by IP:port.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="data">String containing data to send.</param>
        public void Send(string ipPort, string data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            Send(ipPort, Encoding.UTF8.GetBytes(data));
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
                if (!_Clients.ContainsKey(ipPort))
                {
                    Logger?.Invoke(_Header + "Unable to find client: " + ipPort);
                }
                else
                {
                    Logger?.Invoke(_Header + "Kicking: " + ipPort);

                    ClientMetadata client = _Clients[ipPort];
                    client.Dispose();

                    _Clients.Remove(ipPort);
                }

                Logger?.Invoke(_Header + "Disposed: " + ipPort); 
            }

            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(ipPort, DisconnectReason.Kicked));
        }

        /// <summary>
        /// Read data from a given client.
        /// </summary>
        /// <param name="ipPort">The client IP:port string.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>Byte array.</returns>
        public byte[] Read(string ipPort, int count)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");

            int bytesRemaining = count; 
            ClientMetadata client = null;

            lock (_ClientsLock)
            {
                if (!_Clients.ContainsKey(ipPort))
                {
                    throw new KeyNotFoundException("Client with IP:port of " + ipPort + " not found.");
                }

                client = _Clients[ipPort];
            }
              
            if (client.Token.IsCancellationRequested) throw new OperationCanceledException();
            if (!client.NetworkStream.CanRead) throw new IOException("Cannot read from network stream for client " + ipPort + ".");
            if (_Ssl && !client.SslStream.CanRead) throw new IOException("Cannot read from SSL stream for client " + ipPort + ".");

            byte[] buffer = new byte[count];
            int read = 0;

            try
            {
                if (!_Ssl)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        while (bytesRemaining > 0)
                        {
                            read = client.NetworkStream.Read(buffer, 0, buffer.Length);
                            if (read > 0)
                            {
                                ms.Write(buffer, 0, read);
                                bytesRemaining -= read;
                            }
                            else
                            {
                                throw new SocketException();
                            }
                        }

                        byte[] data = ms.ToArray();
                        Stats.ReceivedBytes += data.Length;
                        return data;
                    }
                }
                else
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        while (bytesRemaining > 0)
                        {
                            read = client.SslStream.Read(buffer, 0, buffer.Length);
                            if (read > 0)
                            {
                                ms.Write(buffer, 0, read); 
                                bytesRemaining -= read;
                            }
                            else
                            {
                                throw new SocketException();
                            }
                        }

                        byte[] data = ms.ToArray();
                        Stats.ReceivedBytes += data.Length;
                        return data;
                    }
                }
            }
            catch (Exception)
            {
                ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(ipPort, DisconnectReason.Normal));
                throw;
            }
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
                    string clientIp = tcpClient.Client.RemoteEndPoint.ToString();

                    client = new ClientMetadata(tcpClient);

                    if (_Ssl)
                    {
                        if (AcceptInvalidCertificates)
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
                            continue;
                        }
                    }

                    lock (_Clients)
                    {
                        _Clients.Add(clientIp, client);
                    }
                     
                    Logger?.Invoke("Starting connection monitor for: " + clientIp);
                    Task unawaited = Task.Run(() => ClientConnectionMonitor(client), client.Token);
                    ClientConnected?.Invoke(this, new ClientConnectedEventArgs(clientIp));
                }
                catch (OperationCanceledException)
                {
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
        }

        private async Task<bool> StartTls(ClientMetadata client)
        {
            try
            { 
                await client.SslStream.AuthenticateAsServerAsync(
                    _SslCertificate, 
                    MutuallyAuthenticate, 
                    SslProtocols.Tls12, 
                    !AcceptInvalidCertificates);

                if (!client.SslStream.IsEncrypted)
                {
                    Logger?.Invoke(_Header + "Client " + client.IpPort + " not encrypted, disconnecting");
                    client.Dispose();
                    return false;
                }

                if (!client.SslStream.IsAuthenticated)
                {
                    Logger?.Invoke(_Header + "Client " + client.IpPort + " not SSL/TLS authenticated, disconnecting");
                    client.Dispose();
                    return false;
                }

                if (MutuallyAuthenticate && !client.SslStream.IsMutuallyAuthenticated)
                {
                    Logger?.Invoke(_Header + "Client " + client.IpPort + " failed mutual authentication, disconnecting");
                    client.Dispose();
                    return false;
                }
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "Client " + client.IpPort + " SSL/TLS exception: " + Environment.NewLine + e.ToString());
                client.Dispose();
                return false;
            }

            return true;
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return AcceptInvalidCertificates;
        }
          
        private void ClientConnectionMonitor(ClientMetadata client)
        {
            while (true)
            {
                Task.Delay(1000).Wait();
                if (!IsClientConnected(client.Client))
                {
                    Logger?.Invoke(_Header + "Client " + client.IpPort + " no longer connected");
                    DisconnectClient(client.IpPort);
                    break;
                }
            }
        }

        #endregion
    }
}
