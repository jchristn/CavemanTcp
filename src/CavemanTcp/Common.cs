using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CavemanTcp
{
    internal static class Common
    {
        internal static SslProtocols GetSslProtocol
        {
            get
            {
                SslProtocols protocols = SslProtocols.Tls12;

#if NET5_0_OR_GREATER || NETCOREAPP3_1_OR_GREATER

                protocols |= SslProtocols.Tls13;

#endif

                return protocols;
            }
        }

        internal static byte[] StreamToBytes(Stream input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!input.CanRead) throw new InvalidOperationException("Input stream is not readable");

            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;

                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }

                return ms.ToArray();
            }
        }

        internal static void ParseIpPort(string ipPort, out string ip, out int port)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));

            ip = null;
            port = -1;

            int colonIndex = ipPort.LastIndexOf(':');
            if (colonIndex != -1)
            {
                ip = ipPort.Substring(0, colonIndex);
                port = Convert.ToInt32(ipPort.Substring(colonIndex + 1));
            }
        }

        /// <summary>
        /// Determines whether a byte array contains the specified sequence of bytes.
        /// </summary>
        /// <param name="caller">The byte array to be searched.</param>
        /// <param name="array">The byte to be found.</param>
        /// <returns>The first location of the sequence within the array, -1 if the sequence is not found.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal static int Contains(this byte[] caller, byte[] array)
        {
            byte startValue, endValue;
            int result, arrayLength, searchBoundary, j, startLocation, endOffset;

            if (caller == null)
                throw new ArgumentNullException($"{nameof(caller)}");
            if (array == null)
                throw new ArgumentNullException($"{nameof(array)}");
            if (caller.Length == 0 || array.Length == 0)
                throw new ArgumentException($"Argument {(caller.Length == 0 ? nameof(caller) : nameof(array))} is empty.");

            if (array.Length > caller.Length)
                return -1;

            startValue = array[0];
            arrayLength = array.Length;

            if (arrayLength > 1)
            {
                result = -1;
                endOffset = arrayLength - 1;
                endValue = array[endOffset];
                searchBoundary = caller.Length - arrayLength;
                startLocation = -1;

                while ((startLocation = Array.IndexOf(caller, startValue, startLocation + 1)) >= 0)
                {
                    if (startLocation > searchBoundary)
                        break;

                    if (caller[startLocation + endOffset] == endValue)
                    {
                        for (j = 1; j < endOffset && caller[startLocation + j] == array[j]; j++) { }

                        if (j == endOffset)
                        {
                            result = startLocation;
                            break;
                        }
                    }
                }
            }
            else
            {
                result = Array.IndexOf(caller, startValue);
            }

            return result;
        }
    }
}
