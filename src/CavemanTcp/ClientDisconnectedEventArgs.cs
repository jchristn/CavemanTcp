using System;
using System.Collections.Generic;
using System.Text;

namespace CavemanTcp
{
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
        /// The reason for the disconnection.
        /// </summary>
        public DisconnectReason Reason { get; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ClientDisconnectedEventArgs(ClientMetadata client, DisconnectReason reason)
        {
            Client = client;
            Reason = reason;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
