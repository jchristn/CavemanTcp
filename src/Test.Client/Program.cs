using System;
using System.Text;
using CavemanTcp;

namespace Test.Client
{
    class Program
    {
        static bool _RunForever = true;
        static string _Hostname = "127.0.0.1";
        static int _Port = 9000;
        static bool _Ssl = false;
        static CavemanTcpClient _Client = null;

        static void Main(string[] args)
        {
            InitializeClient(); 
            Console.WriteLine("Connecting to tcp://" +_Hostname + ":" + _Port);
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
                    Console.WriteLine("   cls                  Clear the screen");
                    Console.WriteLine("   q                    Quit the program");
                    Console.WriteLine("   send [data]          Send data to the server");
                    Console.WriteLine("   sendt [ms] [data]    Send data to the server with specified timeout");
                    Console.WriteLine("   read [count]         Read [count] bytes from the server");
                    Console.WriteLine("   readt [ms] [count]   Read [count] bytes from the server with specified timeout");
                    Console.WriteLine("   dispose              Dispose of the client");
                    Console.WriteLine("   start                Start the client (connected: " + (_Client != null ? _Client.IsConnected.ToString() : "false") + ")");
                    Console.WriteLine("   connect              Connect to the server");
                    Console.WriteLine("   disconnect           Disconnect from the server");
                    Console.WriteLine("   stats                Retrieve statistics");
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

                    WriteResult wr = _Client.Send(data);
                    if (wr.Status == WriteResultStatus.Success)
                        Console.WriteLine("Success");
                    else
                        Console.WriteLine("Non-success status: " + wr.Status);
                }

                if (userInput.StartsWith("sendt "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                    int timeoutMs = Convert.ToInt32(parts[1]);
                    string data = parts[2];

                    WriteResult wr = _Client.SendWithTimeout(timeoutMs, data);
                    if (wr.Status == WriteResultStatus.Success)
                        Console.WriteLine("Success");
                    else
                        Console.WriteLine("Non-success status: " + wr.Status);
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

                if (userInput.StartsWith("readt "))
                {
                    string[] parts = userInput.Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                    int timeoutMs = Convert.ToInt32(parts[1]);
                    int count = Convert.ToInt32(parts[2]);

                    ReadResult rr = _Client.ReadWithTimeout(timeoutMs, count);
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

                if (userInput.Equals("connect"))
                {
                    _Client.Connect(10);
                }

                if (userInput.Equals("disconnect"))
                {
                    _Client.Disconnect();
                }

                if (userInput.Equals("stats"))
                {
                    Console.WriteLine(_Client.Statistics);
                }
            }
        }
         
        static void InitializeClient()
        {
            _Client = new CavemanTcpClient(_Hostname, _Port, _Ssl);
            _Client.Logger = Logger;

            _Client.Events.ClientConnected += (s, e) =>
            {
                Console.WriteLine("Connected to server");
            };

            _Client.Events.ClientDisconnected += (s, e) =>
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
