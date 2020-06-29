using System;
using System.Text;
using CavemanTcp;

namespace Test.Client
{
    class Program
    {
        static bool _RunForever = true;
        static TcpClient _Client = null;

        static void Main(string[] args)
        {
            InitializeClient(); 
            Console.WriteLine("Connecting to tcp://127.0.0.1:8000");
            _Client.Connect(10);
            
            while (_RunForever)
            { 
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) continue;

                if (userInput.Equals("?"))
                {
                    Console.WriteLine("-- Available Commands --");
                    Console.WriteLine("");
                    Console.WriteLine("   cls             Clear the screen");
                    Console.WriteLine("   q               Quit the program");
                    Console.WriteLine("   send [data]     Send data to the server");
                    Console.WriteLine("   read [count]    Read [count] bytes from the server");
                    Console.WriteLine("   dispose         Dispose of the client");
                    Console.WriteLine("   start           Start the client (connected: " + (_Client != null ? _Client.IsConnected.ToString() : "false") + ")");
                    Console.WriteLine("   stats           Retrieve statistics");
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

                if (userInput.StartsWith("send "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    string data = parts[1];

                    _Client.Send(data);
                }

                if (userInput.StartsWith("read "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    int count = Convert.ToInt32(parts[1]); 

                    ReadResult rr = _Client.Read(count);
                    if (rr.Status == ReadResultStatus.Success)
                        Console.WriteLine("Retrieved " + rr.BytesRead + " bytes: " + Encoding.UTF8.GetString(rr.Data));
                    else
                        Console.WriteLine("Non-success status: " + rr.Status.ToString());
                }

                if (userInput.Equals("dispose"))
                {
                    _Client.Dispose();
                }

                if (userInput.Equals("start"))
                {
                    InitializeClient();
                    _Client.Connect(10);
                }

                if (userInput.Equals("stats"))
                {
                    Console.WriteLine(_Client.Stats);
                }
            }
        }
         
        static void InitializeClient()
        {
            _Client = new TcpClient("127.0.0.1", 8000, false, null, null);
            _Client.Logger = Logger;

            _Client.ClientConnected += (s, e) =>
            {
                Console.WriteLine("Connected to server");
            };

            _Client.ClientDisconnected += (s, e) =>
            {
                Console.WriteLine("Disconnected from server");
            };
        }

        static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }
    }
}
