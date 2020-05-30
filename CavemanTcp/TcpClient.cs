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

        private string _Header = "[CavemanTcp.Client] ";
        private string _ServerIp = null;
        private int _ServerPort = 0;
        private IPAddress _IPAddress;
        private System.Net.Sockets.TcpClient _TcpClient;
        private NetworkStream _NetworkStream;

        private bool _Ssl;
        private string _PfxCertFilename;
        private string _PfxPassword;
        private SslStream _SslStream;
        private X509Certificate2 _SslCert;
        private X509Certificate2Collection _SslCertCollection;

        private readonly object _SendLock = new object();  
         
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
        }

        /// <summary>
        /// Send data to the server.
        /// </summary> 
        /// <param name="data">Byte array containing data to send.</param>
        public void Send(byte[] data)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");

            try
            {
                lock (_SendLock)
                {
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
                }
            }
            catch (Exception)
            {
                ClientDisconnected?.Invoke(this, EventArgs.Empty);
                throw;
            }

            Stats.SentBytes += data.Length;
        }
         
        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String data.</param>
        public void Send(string data)
        {
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            Send(Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Read data from the server.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>Byte array.</returns>
        public byte[] Read(int count)
        {
            if (count < 1) throw new ArgumentException("Count must be greater than zero.");
            if (_TcpClient == null || !_TcpClient.Connected) throw new IOException("Client is not connected.");
            if (!_NetworkStream.CanRead) throw new IOException("Cannot read from network stream.");
            if (_Ssl && !_SslStream.CanRead) throw new IOException("Cannot read from SSL stream.");

            byte[] buffer = new byte[count];
            int bytesRemaining = count;
            int read = 0;

            try
            {
                if (!_Ssl)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        while (bytesRemaining > 0)
                        {
                            read = _NetworkStream.Read(buffer, 0, buffer.Length);
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
                            read = _SslStream.Read(buffer, 0, buffer.Length);

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
                ClientDisconnected?.Invoke(this, EventArgs.Empty);
                throw;
            }
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
                if (_SslStream != null)
                {
                    _SslStream.Close();
                    _SslStream.Dispose();
                    _SslStream = null;
                }

                if (_NetworkStream != null)
                {
                    _NetworkStream.Close();
                    _NetworkStream.Dispose();
                    _NetworkStream = null;
                }

                if (_TcpClient != null)
                {
                    _TcpClient.Close();
                    _TcpClient.Dispose();
                    _TcpClient = null;
                } 
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

        #endregion
    }
}
