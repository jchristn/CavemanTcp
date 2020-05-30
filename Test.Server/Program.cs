using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CavemanTcp;

namespace Test.Server
{
    class Program
    {
        static bool _RunForever = true;
        static TcpServer _Server = null;
        static string _LastClient = null;

        static void Main(string[] args)
        {
            _Server = new TcpServer("127.0.0.1", 8000, false, null, null);
            _Server.Logger = Logger;

            _Server.ClientConnected += (s, e) => 
            { 
                Console.WriteLine("Client " + e.IpPort + " connected to server");
                _LastClient = e.IpPort;
            };

            _Server.ClientDisconnected += (s, e) => 
            { 
                Console.WriteLine("Client " + e.IpPort + " disconnected from server"); 
            }; 

            _Server.Start(); 

            while (_RunForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) continue;

                if (userInput.Equals("?"))
                {
                    Console.WriteLine("-- Available Commands --");
                    Console.WriteLine("");
                    Console.WriteLine("   cls                     Clear the screen");
                    Console.WriteLine("   q                       Quit the program");
                    Console.WriteLine("   list                    List connected clients");
                    Console.WriteLine("   send [ipport] [data]    Send data to a specific client");
                    Console.WriteLine("   read [ipport] [count]   Read [count] bytes from a specific client");
                    Console.WriteLine("   kick [ipport]           Disconnect a specific client from the server");
                    Console.WriteLine("   dispose                 Dispose of the server");
                    Console.WriteLine("   stats                   Retrieve statistics");
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
                    string data = parts[2];

                    _Server.Send(ipPort, data);
                }

                if (userInput.StartsWith("read "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                    string ipPort = parts[1];
                    int count = Convert.ToInt32(parts[2]);

                    byte[] data = _Server.Read(ipPort, count);
                    Console.WriteLine("Retrieved " + data.Length + " bytes: " + Encoding.UTF8.GetString(data));
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
                    _RunForever = false;
                }

                if (userInput.Equals("stats"))
                {
                    Console.WriteLine(_Server.Stats);
                }
            }
        }

        static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }
    }
}
