using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CavemanTcp
{
    /// <summary>
    /// Result of a read operation.
    /// </summary>
    public class ReadResult
    {
        /// <summary>
        /// Status of the read operation.
        /// </summary>
        public ReadResultStatus Status = ReadResultStatus.Success;

        /// <summary>
        /// Number of bytes read.
        /// </summary>
        public long BytesRead;

        /// <summary>
        /// Stream containing data.
        /// </summary>
        public MemoryStream DataStream;

        /// <summary>
        /// Byte data from the stream.  Using this property will fully read the data stream and it will no longer be readable.
        /// </summary>
        public byte[] Data
        {
            get
            {
                if (_Data != null)
                {
                    return _Data;
                }
                else
                {
                    if (BytesRead > 0 && DataStream != null && DataStream.CanRead)
                    {
                        _Data = Common.StreamToBytes(DataStream);
                        return _Data;
                    }

                    return null;
                }
            }
        }

        private byte[] _Data = null;

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public ReadResult()
        {

        }

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        /// <param name="status">Status of the read operation.</param>
        /// <param name="bytesRead">Number of bytes read.</param>
        /// <param name="data">Stream containing data.</param>
        public ReadResult(ReadResultStatus status, long bytesRead, MemoryStream data)
        {
            Status = status;
            BytesRead = bytesRead;
            DataStream = data;
        }
    }

    /// <summary>
    /// Read result status.
    /// </summary>
    public enum ReadResultStatus
    { 
        /// <summary>
        /// The requested client was not found (only applicable for server read requests).
        /// </summary>
        ClientNotFound,
        /// <summary>
        /// The read operation was successful.
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
