using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CavemanTcp;

namespace Test.ServerAsync
{
    class Program
    {
        static bool _RunForever = true;
        static CavemanTcpServer _Server = null;
        static string _LastClient = null;

        static void Main(string[] args)
        {
            InitializeServer();
            _Server.Start();

            Console.WriteLine("Listening on tcp://127.0.0.1:8000");

            while (_RunForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) continue;

                if (userInput.Equals("?"))
                {
                    Console.WriteLine("-- Available Commands --");
                    Console.WriteLine("");
                    Console.WriteLine("   cls                          Clear the screen");
                    Console.WriteLine("   q                            Quit the program");
                    Console.WriteLine("   start                        Start listening for connections (listening: " + (_Server != null ? _Server.IsListening.ToString() : "false") + ")");
                    Console.WriteLine("   stop                         Stop listening for connections  (listening: " + (_Server != null ? _Server.IsListening.ToString() : "false") + ")");
                    Console.WriteLine("   list                         List connected clients");
                    Console.WriteLine("   send [ipport] [data]         Send data to a specific client");
                    Console.WriteLine("   sendt [ms] [ipport] [data]   Send data to a specific client with specified timeout");
                    Console.WriteLine("   read [ipport] [count]        Read [count] bytes from a specific client");
                    Console.WriteLine("   readt [ms] [ipport] [count]  Read [count] bytes from a specific client with specified timeout");
                    Console.WriteLine("   kick [ipport]                Disconnect a specific client from the server");
                    Console.WriteLine("   dispose                      Dispose of the server");
                    Console.WriteLine("   stats                        Retrieve statistics");
                    Console.WriteLine("");
                    continue;
                }

                if (userInput.Equals("c") || userInput.Equals("cls"))
                {
                    Console.Clear();
                    continue;
                }

                if (userInput.Equals("q"))
                {
                    _RunForever = false;
                    break;
                }

                if (userInput.Equals("start"))
                { 
                    _Server.Start();
                }

                if (userInput.Equals("stop"))
                {
                    _Server.Stop();
                }

                if (userInput.Equals("list"))
                {
                    List<string> clients = _Server.GetClients().ToList();
                    if (clients != null)
                    {
                        Console.WriteLine("Clients: " + clients.Count);
                        foreach (string curr in clients)
                        {
                            Console.WriteLine("  " + curr);
                        }
                    }
                    else
                    {
                        Console.WriteLine("(null)");
                    }
                }

                if (userInput.StartsWith("send "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                    string ipPort = parts[1];
                    if (ipPort.Equals("last")) ipPort = _LastClient;
                    string data = parts[2];

                    WriteResult wr = _Server.SendAsync(ipPort, data).Result;
                    if (wr.Status == WriteResultStatus.Success)
                        Console.WriteLine("Success");
                    else
                        Console.WriteLine("Non-success status: " + wr.Status);
                }

                if (userInput.StartsWith("sendt "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
                    int timeoutMs = Convert.ToInt32(parts[1]);
                    string ipPort = parts[2];
                    if (ipPort.Equals("last")) ipPort = _LastClient;
                    string data = parts[3];

                    WriteResult wr = _Server.SendWithTimeoutAsync(timeoutMs, ipPort, data).Result;
                    if (wr.Status == WriteResultStatus.Success)
                        Console.WriteLine("Success");
                    else
                        Console.WriteLine("Non-success status: " + wr.Status);
                }

                if (userInput.StartsWith("read "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                    string ipPort = parts[1];
                    if (ipPort.Equals("last")) ipPort = _LastClient;
                    int count = Convert.ToInt32(parts[2]);

                    ReadResult rr = _Server.ReadAsync(ipPort, count).Result;
                    if (rr.Status == ReadResultStatus.Success)
                        Console.WriteLine("Retrieved " + rr.BytesRead + " bytes: " + Encoding.UTF8.GetString(rr.Data));
                    else
                        Console.WriteLine("Non-success status: " + rr.Status.ToString());
                }

                if (userInput.StartsWith("readt "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
                    int timeoutMs = Convert.ToInt32(parts[1]);
                    string ipPort = parts[2];
                    if (ipPort.Equals("last")) ipPort = _LastClient;
                    int count = Convert.ToInt32(parts[3]);

                    ReadResult rr = _Server.ReadWithTimeoutAsync(timeoutMs, ipPort, count).Result;
                    if (rr.Status == ReadResultStatus.Success)
                        Console.WriteLine("Retrieved " + rr.BytesRead + " bytes: " + Encoding.UTF8.GetString(rr.Data));
                    else
                        Console.WriteLine("Non-success status: " + rr.Status.ToString());
                }

                if (userInput.StartsWith("kick "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    string ipPort = parts[1];

                    _Server.DisconnectClient(ipPort);
                }

                if (userInput.Equals("dispose"))
                {
                    _Server.Dispose();
                }

                if (userInput.Equals("stats"))
                {
                    Console.WriteLine(_Server.Statistics);
                }
            }
        }

        static void InitializeServer()
        {
            _Server = new CavemanTcpServer("127.0.0.1", 8000, false, null, null);
            _Server.Logger = Logger;

            _Server.Events.ClientConnected += (s, e) =>
            {
                Console.WriteLine("Client " + e.IpPort + " connected to server");
                _LastClient = e.IpPort;
            };

            _Server.Events.ClientDisconnected += (s, e) =>
            {
                Console.WriteLine("Client " + e.IpPort + " disconnected from server");
            };
        }

        static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }
    }
}
