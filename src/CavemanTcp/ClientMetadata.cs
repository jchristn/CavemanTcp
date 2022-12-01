using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CavemanTcp
{
    /// <summary>
    /// Client metadata.
    /// </summary>
    public class ClientMetadata : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Globally-unique identifier for the connection.
        /// </summary>
        public Guid Guid { get; } = Guid.NewGuid();

        /// <summary>
        /// IP:port.
        /// </summary>
        public string IpPort
        {
            get { return _IpPort; }
        }

        /// <summary>
        /// Name for the client, managed by the developer (you).
        /// </summary>
        public string Name { get; set; } = null;

        /// <summary>
        /// Metadata for the client, managed by the developer (you).
        /// </summary>
        public object Metadata { get; set; } = null;

        #endregion

        #region Internal-Members

        internal System.Net.Sockets.TcpClient Client
        {
            get { return _TcpClient; }
        }
         
        internal NetworkStream NetworkStream
        {
            get { return _NetworkStream; }
        }

        internal SslStream SslStream
        {
            get { return _SslStream; }
            set { _SslStream = value; }
        }

        internal SemaphoreSlim ReadSemaphore
        {
            get { return _ReadSemaphore; }
        }

        internal SemaphoreSlim WriteSemaphore
        {
            get { return _WriteSemaphore; }
        }

        internal CancellationTokenSource TokenSource { get; set; }

        internal CancellationToken Token { get; set; }

        #endregion

        #region Private-Members
         
        private System.Net.Sockets.TcpClient _TcpClient = null;
        private NetworkStream _NetworkStream = null;
        private SslStream _SslStream = null;
        private string _IpPort = null;
        private SemaphoreSlim _ReadSemaphore = new SemaphoreSlim(1, 1);
        private SemaphoreSlim _WriteSemaphore = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        internal ClientMetadata(System.Net.Sockets.TcpClient tcp)
        {
            if (tcp == null) throw new ArgumentNullException(nameof(tcp));

            _TcpClient = tcp;
            _NetworkStream = tcp.GetStream();
            _IpPort = tcp.Client.RemoteEndPoint.ToString();
             
            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token; 
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the object and dispose of resources.
        /// </summary>
        public void Dispose()
        {
            if (TokenSource != null)
            {
                if (!TokenSource.IsCancellationRequested) TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = null;
            }

            if (_SslStream != null)
            {
                try
                {
                    _SslStream.Close();
                }
                catch (Exception)
                {

                }
            }

            if (_NetworkStream != null)
            {
                try
                {
                    _NetworkStream.Close();
                }
                catch (Exception)
                {

                }
            }

            if (_TcpClient != null)
            {
                try
                {
                    _TcpClient.Close();
                    _TcpClient.Dispose();
                    _TcpClient = null;
                }
                catch (Exception)
                {

                }
            }
        }

        /// <summary>
        /// Human-readable representation of the object.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string ret = "[";
            ret += Guid.ToString() + "|" + IpPort;
            if (!String.IsNullOrEmpty(Name)) ret += "|" + Name;
            ret += "]";
            return ret;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
