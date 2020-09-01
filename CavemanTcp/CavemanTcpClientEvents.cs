using System;
using System.Collections.Generic;
using System.Text;

namespace CavemanTcp
{
    /// <summary>
    /// CavemanTcp client events.
    /// </summary>
    public class CavemanTcpClientEvents
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

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public CavemanTcpClientEvents()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Internal-Methods

        internal void HandleClientConnected(object sender)
        {
            ClientConnected?.Invoke(sender, EventArgs.Empty);
        }

        internal void HandleClientDisconnected(object sender)
        {
            ClientDisconnected?.Invoke(sender, EventArgs.Empty);
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
