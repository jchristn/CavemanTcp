using System;
using System.Collections.Generic;
using System.Text;

namespace CavemanTcp
{
    /// <summary>
    /// CavemanTcp client settings.
    /// </summary>
    public class CavemanTcpClientSettings
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
        /// Enable or disable acceptance of invalid SSL certificates.
        /// </summary>
        public bool AcceptInvalidCertificates { get; set; } = true;

        /// <summary>
        /// Enable or disable this to tell the TcpClient perform certificate revocation checks.  This is not to be confused
        ///  with AcceptInvalidCertificates setting which decides whether to ignore issues that arise in the call back during the
        ///  authentication phase.
        /// 
        /// With this setting on, in addition to the certificate validation the client performs a revocation check by using either
        ///  the Certificate Revocation List (CRL) or the Online Certificate Status Protocol (OCSP).
        ///  This may not always be needed, which is why this option is exposed here.
        /// 
        /// By default this value is set to false.  If AcceptInvalidCertificates is set to true, then this value will be
        ///  set to false at runtime. 
        /// </summary>
        public bool CheckCertificateRevocation { get; set; } = false;

        /// <summary>
        /// Enable or disable mutual authentication of SSL client and server.
        /// </summary>
        public bool MutuallyAuthenticate { get; set; } = false;

        /// <summary>
        /// Enable or disable connection monitor.
        /// </summary>
        public bool EnableConnectionMonitor { get; set; } = true;

        /// <summary>
        /// Connection monitor polling interval, in microseconds.
        /// </summary>
        public int PollIntervalMicroSeconds
        {
            get
            {
                return _PollIntervalMicroseconds;
            }
            set
            {
                if (value > -1 || value < 1) throw new ArgumentException("Poll interval microseconds must be -1 (indefinite) or greater than zero.");
                _PollIntervalMicroseconds = value;
            }
        }

        #endregion

        #region Private-Members

        private int _StreamBufferSize = 65536;
        private int _PollIntervalMicroseconds = -1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public CavemanTcpClientSettings()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
