using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CavemanTcp;

namespace Test.HttpLoopback
{
    class Program
    {
        static string _Hostname = "localhost";
        static int _Port = 9090;
        static CavemanTcpServer _Server = null;

        static string _HttpResponse =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 11\r\n" +
            "Server: CavemanTcp\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Date: " + DateTime.Now.ToString("ddd, dd MMM yyy HH’:’mm’:’ss ‘GMT’") + "\r\n" +
            "\r\n" +
            "Hello world";

        static void Main(string[] args)
        {
            ThreadPool.SetMinThreads(500, 500);
            ThreadPool.SetMaxThreads(2500, 2500);

            InitializeServer();
            _Server.Start();
            Console.WriteLine("CavemanTcp listening on http://" + _Hostname + ":" + _Port + "/");
            Console.WriteLine("ENTER to exit");
            Console.ReadLine();
        }

        static void InitializeServer()
        {
            _Server = new CavemanTcpServer(_Hostname, _Port);
            _Server.Settings.MonitorClientConnections = false; 
            _Server.Events.ClientConnected += ClientConnected;
        }

        static async void ClientConnected(object sender, ClientConnectedEventArgs args)
        {
            try
            {
                string data = await ReadFully(args.Client.Guid);
                await _Server.SendAsync(args.Client.Guid, _HttpResponse);
                _Server.DisconnectClient(args.Client.Guid);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        static async Task<string> ReadFully(Guid guid)
        {
            StringBuilder sb = new StringBuilder();
             
            ReadResult readInitial = await _Server.ReadAsync(guid, 18);
            if (readInitial.Status != ReadResultStatus.Success)
            { 
                throw new IOException("Unable to read data");
            }
             
            sb.Append(Encoding.ASCII.GetString(readInitial.Data));
            while (true)
            {
                string delimCheck = sb.ToString((sb.Length - 4), 4);
                if (delimCheck.EndsWith("\r\n\r\n"))
                { 
                    break;
                }
                else
                { 
                    ReadResult readSubsequent = await _Server.ReadAsync(guid, 1);
                    if (readSubsequent.Status != ReadResultStatus.Success)
                    { 
                        throw new IOException("Unable to read data");
                    }
                     
                    sb.Append((char)(readSubsequent.Data[0]));
                }
            }
             
            return sb.ToString();
        }

        static byte[] ByteArrayShiftLeft(byte[] bytes)
        {
            byte[] ret = new byte[bytes.Length];

            for (int i = 1; i < bytes.Length; i++)
            {
                ret[(i - 1)] = bytes[i];
            }

            ret[(bytes.Length - 1)] = 0x00;

            return ret;
        }
    }
}
