namespace CavemanTcp
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// CavemanTcp server events.
    /// </summary>
    public class CavemanTcpServerEvents
    {
        #region Public-Members

        /// <summary>
        /// Event to fire when a client connects.  A string containing the client IP:port will be passed.
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs> ClientConnected
        {
            add => _ClientConnected += value;
            remove => _ClientConnected -= value;
        }

        /// <summary>
        /// Event to fire when a client disconnects.  A string containing the client IP:port will be passed.
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected
        {
            add => _ClientDisconnected += value;
            remove => _ClientDisconnected -= value;
        }
        
        /// <summary>
        /// Event to fire when an exception is encountered.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ExceptionEncountered
        {
            add => _ExceptionEncountered += value;
            remove => _ExceptionEncountered -= value;
        }


        #endregion

        #region Private-Members

        private EventHandler<ClientConnectedEventArgs> _ClientConnected;
        private EventHandler<ClientDisconnectedEventArgs> _ClientDisconnected;
        private EventHandler<ExceptionEventArgs> _ExceptionEncountered;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public CavemanTcpServerEvents()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Internal-Methods
        internal void ClearAllEventHandlers()
        {
            _ClientConnected = null;
            _ClientDisconnected = null;
            _ExceptionEncountered = null;
        }

        internal void HandleClientConnected(object sender, ClientConnectedEventArgs args)
        { 
            WrappedEventHandler(() => _ClientConnected?.Invoke(sender, args), "ClientConnected", sender);
        }

        internal void HandleClientDisconnected(object sender, ClientDisconnectedEventArgs args)
        { 
            WrappedEventHandler(() => _ClientDisconnected?.Invoke(sender, args), "ClientDisconnected", sender);
        }

        internal void HandleExceptionEncountered(object sender, Exception e)
        {
            ExceptionEventArgs args = new ExceptionEventArgs(e);
            WrappedEventHandler(() => _ExceptionEncountered?.Invoke(sender, args), "ExceptionEncountered", sender);
        }

        #endregion

        #region Private-Methods

        private void WrappedEventHandler(Action action, string handler, object sender)
        {
            if (action == null) return;

            Action<string> logger = ((CavemanTcpServer)sender).Logger;

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
