using System;
using System.Collections.Generic;
using System.Text;

namespace CavemanTcp
{
    /// <summary>
    /// CavemanTcp server callbacks.
    /// </summary>
    public class CavemanTcpServerCallbacks
    {
        #region Public-Members

        /// <summary>
        /// Callback to invoke when a connection is received to permit or deny the connection.
        /// </summary>
        public Func<string, int, bool> AuthorizeConnection = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public CavemanTcpServerCallbacks()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Internal-Methods
         
        #endregion

        #region Private-Methods

        #endregion
    }
}
