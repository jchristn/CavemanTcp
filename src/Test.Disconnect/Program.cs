using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CavemanTcp;

namespace Test.Disconnect
{
    class Program
    {
        static CancellationTokenSource _TokenSource = new CancellationTokenSource();
        static CancellationToken _Token;

        static CavemanTcpServer _Server = null;
        static string _LastIpPort = null;

        static CavemanTcpClient _Client = null;
        static bool _RunForever = true;

        static void Main(string[] args)
        {
            _Token = _TokenSource.Token;

            Task.Run(() => StartEchoServer(), _Token);

            _Client = new CavemanTcpClient("127.0.0.1", 9000, false, null, null);
            _Client.Events.ClientConnected += (s, e) =>
            {
                Console.WriteLine("[Client] connected");
            };

            _Client.Events.ClientDisconnected += (s, e) =>
            {
                Console.WriteLine("[Client] disconnected");
            };

            _Client.Logger = Console.WriteLine;

            while (_RunForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) continue;

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("q         quit");
                        Console.WriteLine("cls       clear the screen");
                        Console.WriteLine("connect   connect to server");
                        Console.WriteLine("send      send clientecho to the server");
                        Console.WriteLine("read      read data from the server");
                        Console.WriteLine("");
                        break;
                    case "q":
                        _RunForever = false;
                        break;
                    case "cls":
                        Console.Clear();
                        break;
                    case "connect":
                        _Client.Connect(5000);
                        break;
                    case "send":
                        _Client.Send("clientecho");
                        break;
                    case "read":
                        ReadResult rr = _Client.Read(10);
                        if (rr.Status == ReadResultStatus.Success)
                        {
                            Console.WriteLine("[Client] read 10 bytes: " + Encoding.UTF8.GetString(rr.Data));
                        }
                        else
                        {
                            Console.WriteLine("*** [Client] read status: " + rr.Status.ToString());
                        }
                        break;
                }
            }
        }

        static void StartEchoServer()
        {
            Console.WriteLine("[Server] starting TCP/9000");

            _Server = new CavemanTcpServer("127.0.0.01", 9000, false, null, null);
            _Server.Events.ClientConnected += (s, e) => 
            {
                _LastIpPort = e.IpPort;
                Console.WriteLine("[Server] " + e.IpPort + " connected");
            };

            _Server.Events.ClientDisconnected += (s, e) =>
            {
                _LastIpPort = null;
                Console.WriteLine("[Server] " + e.IpPort + " disconnected");
            };

            _Server.Logger = Console.WriteLine;

            _Server.Start();

            Console.WriteLine("[Server] started TCP/9000");

            while (true)
            {
                if (String.IsNullOrEmpty(_LastIpPort))
                {
                    Task.Delay(100).Wait();
                    continue;
                }

                ReadResult rr = _Server.Read(_LastIpPort, 10);
                if (rr.Status == ReadResultStatus.Success)
                {
                    Console.WriteLine("[Server] received " + Encoding.UTF8.GetString(rr.Data));
                    WriteResult wr = _Server.Send(_LastIpPort, "serverecho");
                }
            }
        }
    }
}
