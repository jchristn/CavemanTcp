using System;
using System.Collections.Generic;
using System.Text;

namespace CavemanTcp
{
    /// <summary>
    /// Event arguments for when an exception is encountered. 
    /// </summary>
    public class ExceptionEventArgs
    {
        internal ExceptionEventArgs(Exception e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            Exception = e;
            if (Exception != null) Json = SerializationHelper.SerializeJson(e, true);
        }

        /// <summary>
        /// Exception.
        /// </summary>
        public Exception Exception { get; } = null;

        /// <summary>
        /// JSON representation of the exception.
        /// </summary>
        public string Json { get; } = null;
    }
}