using System;
using System.Collections.Generic;
using System.Text;

namespace CavemanTcp
{
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

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ClientConnectedEventArgs(ClientMetadata client)
        {
            Client = client;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
