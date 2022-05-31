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
        internal ClientConnectedEventArgs(string ipPort)
        {
            IpPort = ipPort;
        }

        /// <summary>
        /// The IP address and port number of the connected client socket.
        /// </summary>
        public string IpPort { get; }
    }
}
