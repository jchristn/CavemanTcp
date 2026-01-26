using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CavemanTcp;

namespace Test.AutomatedTest
{
    class Program
    {
        static TestFramework _framework;
        static string _hostname = "127.0.0.1";
        static int _portCounter = 9500;
        static object _portLock = new object();

        static int GetNextPort()
        {
            lock (_portLock)
            {
                return _portCounter++;
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("====================================");
            Console.WriteLine("CavemanTcp Automated Test Suite");
            Console.WriteLine("====================================");
            Console.WriteLine();

            _framework = new TestFramework();

            // Run all tests
            RunAllTests();

            // Display summary
            Console.WriteLine();
            Console.WriteLine("====================================");
            Console.WriteLine("Test Summary");
            Console.WriteLine("====================================");
            _framework.PrintSummary();

            Console.WriteLine();
        }

        static void RunAllTests()
        {
            // Basic connection tests
            Test_BasicServerStartStop();
            Test_BasicClientConnection();
            Test_ClientServerConnection();

            // Send/Receive tests
            Test_ClientSendServerReceive();
            Test_ServerSendClientReceive();
            Test_BidirectionalCommunication();
            Test_LargeDataTransfer_SingleRead();
            Test_StreamingLargeDataTransfer();

            // Event tests
            Test_ClientConnectedEvent();
            Test_ClientDisconnectedEvent();
            Test_ServerClientConnectedEvent();
            Test_ServerClientDisconnectedEvent();

            // Statistics tests
            Test_ClientStatistics();
            Test_ServerStatistics();

            // Disconnection tests
            Test_PlannedClientDisconnect();
            Test_PlannedServerDisconnect();
            Test_UnplannedDisconnect();

            // Timeout tests
            Test_SendWithTimeout();
            Test_ReadWithTimeout();

            // Cancellation tests
            Test_ReadWithTimeoutAsync_Cancellation_Client();
            Test_ReadWithTimeoutAsync_Cancellation_Server();
            Test_ReadWithTimeoutAsync_Timeout_Client();

            // Async tests
            Test_AsyncSendReceive();

            // Error condition tests
            Test_ClientNotFound();
            Test_ReadFromDisconnectedClient();
            Test_WriteToDisconnectedClient();

            // Multiple client tests
            Test_MultipleClients();
        }

        #region Basic-Connection-Tests

        static void Test_BasicServerStartStop()
        {
            string testName = "Basic Server Start/Stop";
            CavemanTcpServer server = null;
            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                if (!server.IsListening)
                {
                    _framework.RecordFailure(testName, "Server not listening after Start()");
                    return;
                }

                server.Stop();
                Thread.Sleep(200);

                if (server.IsListening)
                {
                    _framework.RecordFailure(testName, "Server still listening after Stop()");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_BasicClientConnection()
        {
            string testName = "Basic Client Connection";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(100);

                if (!client.IsConnected)
                {
                    _framework.RecordFailure(testName, "Client not connected after Connect()");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ClientServerConnection()
        {
            string testName = "Client-Server Connection";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                if (clients.Count != 1)
                {
                    _framework.RecordFailure(testName, $"Expected 1 client, found {clients.Count}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Send-Receive-Tests

        static void Test_ClientSendServerReceive()
        {
            string testName = "Client Send -> Server Receive";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                string testData = "Hello from client!";
                byte[] dataBytes = Encoding.UTF8.GetBytes(testData);

                WriteResult wr = client.Send(dataBytes);
                if (wr.Status != WriteResultStatus.Success)
                {
                    _framework.RecordFailure(testName, $"Send failed with status: {wr.Status}");
                    return;
                }

                if (wr.BytesWritten != dataBytes.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {dataBytes.Length} bytes written, got {wr.BytesWritten}");
                    return;
                }

                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                ReadResult rr = server.Read(clientGuid, dataBytes.Length);
                if (rr.Status != ReadResultStatus.Success)
                {
                    _framework.RecordFailure(testName, $"Read failed with status: {rr.Status}");
                    return;
                }

                string receivedData = Encoding.UTF8.GetString(rr.Data);
                if (receivedData != testData)
                {
                    _framework.RecordFailure(testName, $"Expected '{testData}', received '{receivedData}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ServerSendClientReceive()
        {
            string testName = "Server Send -> Client Receive";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                string testData = "Hello from server!";
                byte[] dataBytes = Encoding.UTF8.GetBytes(testData);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                WriteResult wr = server.Send(clientGuid, dataBytes);
                if (wr.Status != WriteResultStatus.Success)
                {
                    _framework.RecordFailure(testName, $"Send failed with status: {wr.Status}");
                    return;
                }

                Thread.Sleep(200);

                ReadResult rr = client.Read(dataBytes.Length);
                if (rr.Status != ReadResultStatus.Success)
                {
                    _framework.RecordFailure(testName, $"Read failed with status: {rr.Status}");
                    return;
                }

                string receivedData = Encoding.UTF8.GetString(rr.Data);
                if (receivedData != testData)
                {
                    _framework.RecordFailure(testName, $"Expected '{testData}', received '{receivedData}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_BidirectionalCommunication()
        {
            string testName = "Bidirectional Communication";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                // Client -> Server
                string clientMsg = "From client";
                client.Send(Encoding.UTF8.GetBytes(clientMsg));
                Thread.Sleep(100);
                ReadResult rr1 = server.Read(clientGuid, clientMsg.Length);
                string received1 = Encoding.UTF8.GetString(rr1.Data);

                // Server -> Client
                string serverMsg = "From server";
                server.Send(clientGuid, Encoding.UTF8.GetBytes(serverMsg));
                Thread.Sleep(100);
                ReadResult rr2 = client.Read(serverMsg.Length);
                string received2 = Encoding.UTF8.GetString(rr2.Data);

                if (received1 != clientMsg || received2 != serverMsg)
                {
                    _framework.RecordFailure(testName, $"Data mismatch: '{received1}' vs '{clientMsg}', '{received2}' vs '{serverMsg}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_LargeDataTransfer_SingleRead()
        {
            string testName = "Large Data Transfer (Single Read)";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                // Send 64KB of data in a single operation
                byte[] largeData = new byte[65536];
                for (int i = 0; i < largeData.Length; i++)
                {
                    largeData[i] = (byte)(i % 256);
                }

                WriteResult wr = client.Send(largeData);
                if (wr.Status != WriteResultStatus.Success || wr.BytesWritten != largeData.Length)
                {
                    _framework.RecordFailure(testName, $"Send failed: {wr.Status}, wrote {wr.BytesWritten}/{largeData.Length} bytes");
                    return;
                }

                Thread.Sleep(300);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                ReadResult rr = server.Read(clientGuid, largeData.Length);
                if (rr.Status != ReadResultStatus.Success || rr.BytesRead != largeData.Length)
                {
                    _framework.RecordFailure(testName, $"Read failed: {rr.Status}, read {rr.BytesRead}/{largeData.Length} bytes");
                    return;
                }

                // Verify data integrity
                byte[] receivedData = rr.Data;
                for (int i = 0; i < largeData.Length; i++)
                {
                    if (largeData[i] != receivedData[i])
                    {
                        _framework.RecordFailure(testName, $"Data mismatch at byte {i}");
                        return;
                    }
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_StreamingLargeDataTransfer()
        {
            string testName = "Streaming Large Data Transfer (100MB)";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                // Stream 100MB of data using 8192-byte buffer
                int totalSize = 100 * 1024 * 1024; // 100MB
                int bufferSize = 8192;
                byte[] buffer = new byte[bufferSize];
                long totalSent = 0;
                long totalReceived = 0;

                // Start a task to read on the server side
                bool readError = false;
                string readErrorMsg = "";
                Task readTask = Task.Run(() =>
                {
                    try
                    {
                        while (totalReceived < totalSize)
                        {
                            int toRead = (int)Math.Min(bufferSize, totalSize - totalReceived);
                            ReadResult rr = server.Read(clientGuid, toRead);

                            if (rr.Status != ReadResultStatus.Success)
                            {
                                readError = true;
                                readErrorMsg = $"Read failed: {rr.Status} at {totalReceived} bytes";
                                break;
                            }

                            // Verify received data
                            for (int i = 0; i < rr.BytesRead; i++)
                            {
                                byte expected = (byte)((totalReceived + i) % 256);
                                if (rr.Data[i] != expected)
                                {
                                    readError = true;
                                    readErrorMsg = $"Data mismatch at byte {totalReceived + i}: expected {expected}, got {rr.Data[i]}";
                                    return;
                                }
                            }

                            totalReceived += rr.BytesRead;
                        }
                    }
                    catch (Exception ex)
                    {
                        readError = true;
                        readErrorMsg = $"Read exception: {ex.Message}";
                    }
                });

                // Send data from client in chunks
                while (totalSent < totalSize)
                {
                    int toSend = (int)Math.Min(bufferSize, totalSize - totalSent);

                    // Fill buffer with predictable data
                    for (int i = 0; i < toSend; i++)
                    {
                        buffer[i] = (byte)((totalSent + i) % 256);
                    }

                    // Send chunk
                    byte[] chunk = new byte[toSend];
                    Array.Copy(buffer, 0, chunk, 0, toSend);

                    WriteResult wr = client.Send(chunk);
                    if (wr.Status != WriteResultStatus.Success)
                    {
                        _framework.RecordFailure(testName, $"Send failed: {wr.Status} at {totalSent} bytes");
                        return;
                    }

                    totalSent += wr.BytesWritten;
                }

                // Wait for read task to complete
                readTask.Wait(30000); // 30 second timeout

                if (readError)
                {
                    _framework.RecordFailure(testName, readErrorMsg);
                    return;
                }

                if (totalSent != totalSize)
                {
                    _framework.RecordFailure(testName, $"Sent {totalSent}/{totalSize} bytes");
                    return;
                }

                if (totalReceived != totalSize)
                {
                    _framework.RecordFailure(testName, $"Received {totalReceived}/{totalSize} bytes");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Event-Tests

        static void Test_ClientConnectedEvent()
        {
            string testName = "Client Connected Event";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                bool eventFired = false;

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Events.ClientConnected += (s, e) => { eventFired = true; };

                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client.Connect(5);
                Thread.Sleep(200);

                if (!eventFired)
                {
                    _framework.RecordFailure(testName, "ClientConnected event did not fire");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ClientDisconnectedEvent()
        {
            string testName = "Client Disconnected Event (via Server Kick)";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                bool eventFired = false;

                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Events.ClientDisconnected += (s, e) => { eventFired = true; };
                client.Connect(5);
                Thread.Sleep(200);

                // Get client Guid and disconnect from server side
                var clients = server.GetClients().ToList();
                server.DisconnectClient(clients[0].Guid);
                Thread.Sleep(1000);

                if (!eventFired)
                {
                    _framework.RecordFailure(testName, "ClientDisconnected event did not fire when kicked by server");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ServerClientConnectedEvent()
        {
            string testName = "Server Client Connected Event";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                bool eventFired = false;
                Guid? connectedClientGuid = null;

                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Events.ClientConnected += (s, e) =>
                {
                    eventFired = true;
                    connectedClientGuid = e.Client.Guid;
                };
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                if (!eventFired)
                {
                    _framework.RecordFailure(testName, "Server ClientConnected event did not fire");
                    return;
                }

                if (connectedClientGuid == null)
                {
                    _framework.RecordFailure(testName, "Client Guid not captured in event");
                    return;
                }

                var clients = server.GetClients().ToList();
                if (clients.Count != 1 || clients[0].Guid != connectedClientGuid.Value)
                {
                    _framework.RecordFailure(testName, "Client Guid mismatch");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ServerClientDisconnectedEvent()
        {
            string testName = "Server Client Disconnected Event";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                bool eventFired = false;
                Guid? disconnectedClientGuid = null;

                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Events.ClientDisconnected += (s, e) =>
                {
                    eventFired = true;
                    disconnectedClientGuid = e.Client.Guid;
                };
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid expectedGuid = clients[0].Guid;

                client.Disconnect();
                Thread.Sleep(1000);

                if (!eventFired)
                {
                    _framework.RecordFailure(testName, "Server ClientDisconnected event did not fire (may not fire for explicit Disconnect)");
                    return;
                }

                if (disconnectedClientGuid != expectedGuid)
                {
                    _framework.RecordFailure(testName, $"Expected Guid {expectedGuid}, got {disconnectedClientGuid}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Statistics-Tests

        static void Test_ClientStatistics()
        {
            string testName = "Client Statistics";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                DateTime startTime = client.Statistics.StartTime;
                if (startTime > DateTime.Now.ToUniversalTime())
                {
                    _framework.RecordFailure(testName, "Invalid start time");
                    return;
                }

                // Send data
                byte[] sendData = Encoding.UTF8.GetBytes("Test message");
                client.Send(sendData);
                Thread.Sleep(100);

                if (client.Statistics.SentBytes != sendData.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {sendData.Length} sent bytes, got {client.Statistics.SentBytes}");
                    return;
                }

                // Receive data
                var clients = server.GetClients().ToList();
                server.Send(clients[0].Guid, sendData);
                Thread.Sleep(100);
                client.Read(sendData.Length);

                if (client.Statistics.ReceivedBytes != sendData.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {sendData.Length} received bytes, got {client.Statistics.ReceivedBytes}");
                    return;
                }

                if (client.Statistics.UpTime.TotalSeconds < 0)
                {
                    _framework.RecordFailure(testName, "Invalid uptime");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ServerStatistics()
        {
            string testName = "Server Statistics";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                DateTime startTime = server.Statistics.StartTime;

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                // Send data
                byte[] sendData = Encoding.UTF8.GetBytes("Server message");
                server.Send(clientGuid, sendData);
                Thread.Sleep(100);

                if (server.Statistics.SentBytes != sendData.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {sendData.Length} sent bytes, got {server.Statistics.SentBytes}");
                    return;
                }

                // Receive data
                client.Send(sendData);
                Thread.Sleep(100);
                server.Read(clientGuid, sendData.Length);

                if (server.Statistics.ReceivedBytes != sendData.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {sendData.Length} received bytes, got {server.Statistics.ReceivedBytes}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Disconnection-Tests

        static void Test_PlannedClientDisconnect()
        {
            string testName = "Planned Client Disconnect";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                if (!client.IsConnected)
                {
                    _framework.RecordFailure(testName, "Client not connected");
                    return;
                }

                client.Disconnect();
                Thread.Sleep(200);

                if (client.IsConnected)
                {
                    _framework.RecordFailure(testName, "Client still connected after Disconnect()");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_PlannedServerDisconnect()
        {
            string testName = "Planned Server Disconnect (Kick)";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                bool clientDisconnected = false;

                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Events.ClientDisconnected += (s, e) => { clientDisconnected = true; };
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                server.DisconnectClient(clientGuid);
                Thread.Sleep(1000);

                if (!clientDisconnected)
                {
                    _framework.RecordFailure(testName, "Client did not detect disconnection (event may not fire for server-initiated disconnect)");
                    return;
                }

                var remainingClients = server.GetClients().ToList();
                if (remainingClients.Count != 0)
                {
                    _framework.RecordFailure(testName, $"Expected 0 clients, found {remainingClients.Count}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_UnplannedDisconnect()
        {
            string testName = "Unplanned Disconnect Detection";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                // Abruptly dispose client (simulates crash/network failure)
                client.Dispose();
                client = null;
                Thread.Sleep(200);

                // Try to read from disconnected client
                ReadResult rr = server.Read(clientGuid, 10);
                if (rr.Status == ReadResultStatus.Success)
                {
                    _framework.RecordFailure(testName, "Read succeeded on disconnected client");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Timeout-Tests

        static void Test_SendWithTimeout()
        {
            string testName = "Send With Timeout";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                byte[] data = Encoding.UTF8.GetBytes("Timeout test");
                WriteResult wr = client.SendWithTimeout(5000, data);

                if (wr.Status != WriteResultStatus.Success)
                {
                    _framework.RecordFailure(testName, $"Send failed with status: {wr.Status}");
                    return;
                }

                if (wr.BytesWritten != data.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {data.Length} bytes, wrote {wr.BytesWritten}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ReadWithTimeout()
        {
            string testName = "Read With Timeout (Success Case)";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                // Send data from client
                byte[] data = Encoding.UTF8.GetBytes("Timeout read test");
                client.Send(data);
                Thread.Sleep(200);

                // Read with timeout on server
                ReadResult rr = server.ReadWithTimeout(5000, clientGuid, data.Length);

                if (rr.Status != ReadResultStatus.Success)
                {
                    _framework.RecordFailure(testName, $"Read failed with status: {rr.Status}");
                    return;
                }

                if (rr.BytesRead != data.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {data.Length} bytes, read {rr.BytesRead}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ReadWithTimeoutAsync_Cancellation_Client()
        {
            string testName = "ReadWithTimeoutAsync Cancellation (Client)";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                // Set up: timeout is 10 seconds, but we cancel after 1 second
                int timeoutMs = 10000;
                int cancelDelayMs = 1000;
                var cts = new CancellationTokenSource(cancelDelayMs);

                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Try to read bytes that will never arrive - should be cancelled
                Task<ReadResult> readTask = client.ReadWithTimeoutAsync(timeoutMs, 100, cts.Token);
                readTask.Wait();
                ReadResult rr = readTask.Result;

                sw.Stop();

                // Should return Canceled, not Timeout
                if (rr.Status != ReadResultStatus.Canceled)
                {
                    _framework.RecordFailure(testName, $"Expected Canceled status, got {rr.Status}");
                    return;
                }

                // Should complete in ~1 second (cancel time), not 10 seconds (timeout)
                if (sw.ElapsedMilliseconds > 5000)
                {
                    _framework.RecordFailure(testName, $"Took too long ({sw.ElapsedMilliseconds}ms), cancellation may not have worked");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ReadWithTimeoutAsync_Cancellation_Server()
        {
            string testName = "ReadWithTimeoutAsync Cancellation (Server)";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                // Set up: timeout is 10 seconds, but we cancel after 1 second
                int timeoutMs = 10000;
                int cancelDelayMs = 1000;
                var cts = new CancellationTokenSource(cancelDelayMs);

                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Try to read bytes that will never arrive - should be cancelled
                Task<ReadResult> readTask = server.ReadWithTimeoutAsync(timeoutMs, clientGuid, 100, cts.Token);
                readTask.Wait();
                ReadResult rr = readTask.Result;

                sw.Stop();

                // Should return Canceled, not Timeout
                if (rr.Status != ReadResultStatus.Canceled)
                {
                    _framework.RecordFailure(testName, $"Expected Canceled status, got {rr.Status}");
                    return;
                }

                // Should complete in ~1 second (cancel time), not 10 seconds (timeout)
                if (sw.ElapsedMilliseconds > 5000)
                {
                    _framework.RecordFailure(testName, $"Took too long ({sw.ElapsedMilliseconds}ms), cancellation may not have worked");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ReadWithTimeoutAsync_Timeout_Client()
        {
            string testName = "ReadWithTimeoutAsync Timeout (Client)";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                // Set up: timeout is 1 second, no cancellation
                int timeoutMs = 1000;

                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Try to read bytes that will never arrive - should timeout
                Task<ReadResult> readTask = client.ReadWithTimeoutAsync(timeoutMs, 100);
                readTask.Wait();
                ReadResult rr = readTask.Result;

                sw.Stop();

                // Should return Timeout (not Canceled since no token was cancelled)
                if (rr.Status != ReadResultStatus.Timeout)
                {
                    _framework.RecordFailure(testName, $"Expected Timeout status, got {rr.Status}");
                    return;
                }

                // Should complete in ~1 second (timeout time)
                if (sw.ElapsedMilliseconds < 800 || sw.ElapsedMilliseconds > 3000)
                {
                    _framework.RecordFailure(testName, $"Unexpected duration ({sw.ElapsedMilliseconds}ms), expected ~1000ms");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Async-Tests

        static void Test_AsyncSendReceive()
        {
            string testName = "Async Send/Receive";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                var clients = server.GetClients().ToList();
                Guid clientGuid = clients[0].Guid;

                // Async send from client
                byte[] data = Encoding.UTF8.GetBytes("Async test message");
                Task<WriteResult> sendTask = client.SendAsync(data);
                sendTask.Wait();

                if (sendTask.Result.Status != WriteResultStatus.Success)
                {
                    _framework.RecordFailure(testName, $"Async send failed: {sendTask.Result.Status}");
                    return;
                }

                Thread.Sleep(200);

                // Async read on server
                Task<ReadResult> readTask = server.ReadAsync(clientGuid, data.Length);
                readTask.Wait();

                if (readTask.Result.Status != ReadResultStatus.Success)
                {
                    _framework.RecordFailure(testName, $"Async read failed: {readTask.Result.Status}");
                    return;
                }

                string received = Encoding.UTF8.GetString(readTask.Result.Data);
                string expected = Encoding.UTF8.GetString(data);

                if (received != expected)
                {
                    _framework.RecordFailure(testName, $"Data mismatch: expected '{expected}', got '{received}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Error-Condition-Tests

        static void Test_ClientNotFound()
        {
            string testName = "Client Not Found Error";
            CavemanTcpServer server = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                Guid nonExistentGuid = Guid.NewGuid();
                ReadResult rr = server.Read(nonExistentGuid, 10);

                if (rr.Status != ReadResultStatus.ClientNotFound)
                {
                    _framework.RecordFailure(testName, $"Expected ClientNotFound, got {rr.Status}");
                    return;
                }

                WriteResult wr = server.Send(nonExistentGuid, "test");
                if (wr.Status != WriteResultStatus.ClientNotFound)
                {
                    _framework.RecordFailure(testName, $"Expected ClientNotFound, got {wr.Status}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_ReadFromDisconnectedClient()
        {
            string testName = "Read From Disconnected Client";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                client.Disconnect();
                Thread.Sleep(200);

                // Try to read - should detect disconnection (may throw exception or return error status)
                try
                {
                    ReadResult rr = client.Read(10);
                    if (rr.Status == ReadResultStatus.Success)
                    {
                        _framework.RecordFailure(testName, "Read succeeded on disconnected client");
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Expected - client not connected
                }

                // Either caught exception or got error status - both are acceptable
                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                // Unexpected exception type
                if (ex.Message.Contains("not connected"))
                {
                    _framework.RecordSuccess(testName); // This is actually expected behavior
                }
                else
                {
                    _framework.RecordFailure(testName, ex.Message);
                }
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        static void Test_WriteToDisconnectedClient()
        {
            string testName = "Write To Disconnected Client";
            CavemanTcpServer server = null;
            CavemanTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                client = new CavemanTcpClient(_hostname, port, false, null, null);
                client.Connect(5);
                Thread.Sleep(200);

                client.Disconnect();
                Thread.Sleep(200);

                // Try to write - should detect disconnection (may throw exception or return error status)
                try
                {
                    WriteResult wr = client.Send("test data");
                    if (wr.Status == WriteResultStatus.Success)
                    {
                        _framework.RecordFailure(testName, "Write succeeded on disconnected client");
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Expected - client not connected
                }

                // Either caught exception or got error status - both are acceptable
                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                // Unexpected exception type
                if (ex.Message.Contains("not connected"))
                {
                    _framework.RecordSuccess(testName); // This is actually expected behavior
                }
                else
                {
                    _framework.RecordFailure(testName, ex.Message);
                }
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Multiple-Client-Tests

        static void Test_MultipleClients()
        {
            string testName = "Multiple Clients";
            CavemanTcpServer server = null;
            List<CavemanTcpClient> clients = new List<CavemanTcpClient>();

            try
            {
                int port = GetNextPort();
                server = new CavemanTcpServer(_hostname, port, false, null, null);
                server.Start();
                Thread.Sleep(100);

                // Connect 5 clients
                for (int i = 0; i < 5; i++)
                {
                    var client = new CavemanTcpClient(_hostname, port, false, null, null);
                    client.Connect(5);
                    clients.Add(client);
                    Thread.Sleep(100);
                }

                var serverClients = server.GetClients().ToList();
                if (serverClients.Count != 5)
                {
                    _framework.RecordFailure(testName, $"Expected 5 clients, found {serverClients.Count}");
                    return;
                }

                // Send unique message to each client
                for (int i = 0; i < 5; i++)
                {
                    string msg = $"Message to client {i}";
                    server.Send(serverClients[i].Guid, Encoding.UTF8.GetBytes(msg));
                }

                Thread.Sleep(300);

                // Each client reads their message
                for (int i = 0; i < 5; i++)
                {
                    string expected = $"Message to client {i}";
                    ReadResult rr = clients[i].Read(expected.Length);
                    string received = Encoding.UTF8.GetString(rr.Data);

                    if (received != expected)
                    {
                        _framework.RecordFailure(testName, $"Client {i} expected '{expected}', got '{received}'");
                        return;
                    }
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                foreach (var client in clients)
                {
                    client?.Dispose();
                }
                server?.Dispose();
                Thread.Sleep(100);
            }
        }

        #endregion
    }

    #region Test-Framework

    class TestFramework
    {
        private List<TestResult> _results = new List<TestResult>();
        private object _lock = new object();

        public void RecordSuccess(string testName)
        {
            lock (_lock)
            {
                _results.Add(new TestResult { TestName = testName, Passed = true });
                Console.WriteLine($"[PASS] {testName}");
            }
        }

        public void RecordFailure(string testName, string reason)
        {
            lock (_lock)
            {
                _results.Add(new TestResult { TestName = testName, Passed = false, FailureReason = reason });
                Console.WriteLine($"[FAIL] {testName}");
                Console.WriteLine($"       Reason: {reason}");
            }
        }

        public void PrintSummary()
        {
            Console.WriteLine();
            foreach (var result in _results)
            {
                string status = result.Passed ? "PASS" : "FAIL";
                Console.WriteLine($"[{status}] {result.TestName}");
            }

            Console.WriteLine();
            int passed = _results.Count(r => r.Passed);
            int failed = _results.Count(r => !r.Passed);
            int total = _results.Count;

            Console.WriteLine($"Total: {total} tests");
            Console.WriteLine($"Passed: {passed}");
            Console.WriteLine($"Failed: {failed}");
            Console.WriteLine();

            if (failed == 0)
            {
                Console.WriteLine("====================");
                Console.WriteLine("OVERALL RESULT: PASS");
                Console.WriteLine("====================");
            }
            else
            {
                Console.WriteLine("====================");
                Console.WriteLine("OVERALL RESULT: FAIL");
                Console.WriteLine("====================");
            }
        }
    }

    class TestResult
    {
        public string TestName { get; set; }
        public bool Passed { get; set; }
        public string FailureReason { get; set; }
    }

    #endregion
}
