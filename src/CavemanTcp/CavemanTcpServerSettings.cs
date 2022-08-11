using System;
using System.Collections.Generic;
using System.Text;

namespace CavemanTcp
{
    /// <summary>
    /// CavemanTcp server settings.
    /// </summary>
    public class CavemanTcpServerSettings
    {
        #region Public-Members

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
        /// Enable client connection monitoring, which checks connectivity every second.
        /// </summary>
        public bool MonitorClientConnections = true;

        /// <summary>
        /// Enable or disable acceptance of invalid SSL certificates.
        /// </summary>
        public bool AcceptInvalidCertificates = true;

        /// <summary>
        /// Enable or disable mutual authentication of SSL client and server.
        /// </summary>
        public bool MutuallyAuthenticate = false;

        /// <summary>
        /// Maximum number of connections the server will accept.
        /// Default is 4096.  Value must be greater than zero.
        /// </summary>
        public int MaxConnections
        {
            get
            {
                return _MaxConnections;
            }
            set
            {
                if (value < 1) throw new ArgumentException("Max connections must be greater than zero.");
                _MaxConnections = value;
            }
        }

        /// <summary>
        /// The list of permitted IP addresses from which connections can be received.
        /// </summary>
        public List<string> PermittedIPs
        {
            get
            {
                return _PermittedIPs;
            }
            set
            {
                if (value == null) _PermittedIPs = new List<string>();
                else _PermittedIPs = value;
            }
        }

        /// <summary>
        /// The list of blocked IP addresses from which connections will be declined.
        /// </summary>
        public List<string> BlockedIPs
        {
            get
            {
                return _BlockedIPs;
            }
            set
            {
                if (value == null) _BlockedIPs = new List<string>();
                else _BlockedIPs = value;
            }
        }

        #endregion

        #region Private-Members

        private int _StreamBufferSize = 65536;
        private int _MaxConnections = 4096;
        private List<string> _PermittedIPs = new List<string>();
        private List<string> _BlockedIPs = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public CavemanTcpServerSettings()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
