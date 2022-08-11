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
         
        /// <summary>
        /// Event to fire when an exception is encountered.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ExceptionEncountered; 

        #endregion

        #region Private-Members

        private bool _ClientConnectedFiring = false;
        private bool _ClientDisconnectedFiring = false;

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
            if (_ClientConnectedFiring) return;

            _ClientConnectedFiring = true;
            WrappedEventHandler(() => ClientConnected?.Invoke(sender, EventArgs.Empty), "ClientConnected", sender);
            _ClientConnectedFiring = false;
        }

        internal void HandleClientDisconnected(object sender)
        {
            if (_ClientDisconnectedFiring) return;

            _ClientDisconnectedFiring = true; 
            WrappedEventHandler(() => ClientDisconnected?.Invoke(sender, EventArgs.Empty), "ClientDisconnected", sender);
            _ClientDisconnectedFiring = false;
        }

        internal void HandleExceptionEncountered(object sender, Exception e)
        {
            ExceptionEventArgs args = new ExceptionEventArgs(e);
            WrappedEventHandler(() => ExceptionEncountered?.Invoke(sender, args), "ExceptionEncountered", sender);
        }

        #endregion

        #region Private-Methods

        private void WrappedEventHandler(Action action, string handler, object sender)
        {
            if (action == null) return;

            Action<string> logger = ((CavemanTcpClient)sender).Logger;

            try
            {
                action.Invoke();
            }
            catch (Exception e)
            {
                logger?.Invoke("Event handler exception in " + handler + ": " + e.Message);
            }
        }

        #endregion
    }
}
