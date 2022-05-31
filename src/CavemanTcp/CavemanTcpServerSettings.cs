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

        #endregion

        #region Private-Members

        private int _StreamBufferSize = 65536;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public CavemanTcpServerSettings()
        {

        }

        #endregion
    }
}
