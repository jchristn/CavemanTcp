using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CavemanTcp
{
    internal class ClientMetadata : IDisposable
    {
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

        internal string IpPort
        {
            get { return _IpPort; }
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
        private SemaphoreSlim _ReadSemaphore = new SemaphoreSlim(1);
        private SemaphoreSlim _WriteSemaphore = new SemaphoreSlim(1);

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

        #endregion

        #region Private-Methods

        #endregion
    }
}
