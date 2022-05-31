using System;
using System.Collections.Generic;
using System.Text;

namespace CavemanTcp
{
    /// <summary>
    /// Arguments for situations where client connections are declined.
    /// </summary>
    public class ClientDeclinedEventArgs : EventArgs
    {
        internal ClientDeclinedEventArgs(string ipPort, DisconnectReason reason = DisconnectReason.ConnectionDeclined)
        {
            IpPort = ipPort;
            Reason = reason;
        }

        /// <summary>
        /// The IP address and port number of the disconnected client socket.
        /// </summary>
        public string IpPort { get; }

        /// <summary>
        /// The reason for the disconnection.
        /// </summary>
        public DisconnectReason Reason { get; }
    }
}
