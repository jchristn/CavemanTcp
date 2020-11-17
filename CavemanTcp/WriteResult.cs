using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CavemanTcp
{
    /// <summary>
    /// Result of a write operation.
    /// </summary>
    public class WriteResult
    {
        /// <summary>
        /// Status of the write operation.
        /// </summary>
        public WriteResultStatus Status = WriteResultStatus.Success;

        /// <summary>
        /// Number of bytes written.
        /// </summary>
        public long BytesWritten;

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public WriteResult()
        {

        }

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        /// <param name="status">Status of the write operation.</param>
        /// <param name="bytesWritten">Number of bytes written.</param>
        public WriteResult(WriteResultStatus status, long bytesWritten)
        {
            Status = status;
            BytesWritten = bytesWritten;
        }
    }

    /// <summary>
    /// Write result status.
    /// </summary>
    public enum WriteResultStatus
    { 
        /// <summary>
        /// The requested client was not found (only applicable for server read requests).
        /// </summary>
        ClientNotFound,
        /// <summary>
        /// The write operation was successful.
        /// </summary>
        Success,
        /// <summary>
        /// The operation timed out (reserved for future use).
        /// </summary>
        Timeout,
        /// <summary>
        /// The connection was lost. 
        /// </summary>
        Disconnected,
        /// <summary>
        /// The request was canceled.
        /// </summary>
        Canceled
    }
}
