namespace CavemanTcp
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;

    /// <summary>
    /// Arguments for client disconnection events.
    /// </summary>
    public class ClientDisconnectedEventArgs : EventArgs
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

        /// <summary>
        /// The reason for the disconnection.
        /// </summary>
        public DisconnectReason Reason { get; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ClientDisconnectedEventArgs(ClientMetadata client, EndPoint localEndpoint, DisconnectReason reason)
        {
            Client = client;
            LocalEndpoint = localEndpoint;
            Reason = reason;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
