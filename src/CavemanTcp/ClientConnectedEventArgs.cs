namespace CavemanTcp
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;

    /// <summary>
    /// Arguments for client connection events.
    /// </summary>
    public class ClientConnectedEventArgs : EventArgs
    {
        #region Public-Members


        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; }

        /// <summary>
        /// Local endpoint.
        /// </summary>
        public EndPoint LocalEndpoint { get; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ClientConnectedEventArgs(ClientMetadata client, EndPoint localEndpoint)
        {
            Client = client;
            LocalEndpoint = localEndpoint;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
