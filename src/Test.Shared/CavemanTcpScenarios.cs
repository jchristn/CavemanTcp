#pragma warning disable CS0618

namespace Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using CavemanTcp;

    public static class CavemanTcpScenarios
    {
        private static readonly string _Hostname = "127.0.0.1";
        private const int DefaultConditionTimeoutMs = 3000;
        private const int DefaultEventTimeoutMs = 3000;
        private const int PollIntervalMs = 10;
        private const string CertificatePassword = "simpletcp";

        public static async Task AsyncBidirectionalCommunication()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] clientPayload = Encoding.UTF8.GetBytes("async-client");
                byte[] serverPayload = Encoding.UTF8.GetBytes("async-server");

                WriteResult clientWrite = await pair.Client.SendAsync(clientPayload).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, clientWrite.Status);
                ReadResult serverRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, clientPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, serverRead.Status);
                AssertPayloadEquals(clientPayload, serverRead.Data);

                WriteResult serverWrite = await pair.Server.SendAsync(pair.ServerClient.Guid, serverPayload).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, serverWrite.Status);
                ReadResult clientRead = await pair.Client.ReadAsync(serverPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, clientRead.Status);
                AssertPayloadEquals(serverPayload, clientRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task AsyncOperationsReturnCanceledWhenTokenIsCanceled()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                using CancellationTokenSource cts = new CancellationTokenSource();
                cts.Cancel();

                WriteResult clientSend = await pair.Client.SendAsync(new byte[] { 0x01 }, cts.Token).ConfigureAwait(false);
                ReadResult clientRead = await pair.Client.ReadAsync(1, cts.Token).ConfigureAwait(false);
                WriteResult clientSendWithTimeout = await pair.Client.SendWithTimeoutAsync(1000, new byte[] { 0x01 }, cts.Token).ConfigureAwait(false);
                ReadResult clientReadWithTimeout = await pair.Client.ReadWithTimeoutAsync(1000, 1, cts.Token).ConfigureAwait(false);

                WriteResult serverSend = await pair.Server.SendAsync(pair.ServerClient.Guid, new byte[] { 0x01 }, cts.Token).ConfigureAwait(false);
                ReadResult serverRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, 1, cts.Token).ConfigureAwait(false);
                WriteResult serverSendWithTimeout = await pair.Server.SendWithTimeoutAsync(1000, pair.ServerClient.Guid, new byte[] { 0x01 }, cts.Token).ConfigureAwait(false);
                ReadResult serverReadWithTimeout = await pair.Server.ReadWithTimeoutAsync(1000, pair.ServerClient.Guid, 1, cts.Token).ConfigureAwait(false);

                TestAssert.Equal(WriteResultStatus.Canceled, clientSend.Status);
                TestAssert.Equal(ReadResultStatus.Canceled, clientRead.Status);
                TestAssert.Equal(WriteResultStatus.Canceled, clientSendWithTimeout.Status);
                TestAssert.Equal(ReadResultStatus.Canceled, clientReadWithTimeout.Status);

                TestAssert.Equal(WriteResultStatus.Canceled, serverSend.Status);
                TestAssert.Equal(ReadResultStatus.Canceled, serverRead.Status);
                TestAssert.Equal(WriteResultStatus.Canceled, serverSendWithTimeout.Status);
                TestAssert.Equal(ReadResultStatus.Canceled, serverReadWithTimeout.Status);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task AuthorizationCallbackExceptionsRaiseExceptionEventAndDeclineConnection()
        {
            int port = GetNextPort();
            Exception capturedException = null;
            string declinedIpPort = null;
            DisconnectReason declinedReason = DisconnectReason.Normal;
            using ManualResetEventSlim exceptionEvent = new ManualResetEventSlim(false);
            using ManualResetEventSlim declinedEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            server.Callbacks.AuthorizeConnection = (ip, clientPort) => throw new InvalidOperationException("auth exploded");
            server.Events.ExceptionEncountered += (s, e) =>
            {
                capturedException = e.Exception;
                exceptionEvent.Set();
            };
            server.Events.ClientDeclined += (s, e) =>
            {
                declinedIpPort = e.IpPort;
                declinedReason = e.Reason;
                declinedEvent.Set();
            };

            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
            client.Settings.PollIntervalMicroSeconds = 50000;

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                TryConnectAndIgnoreFailure(client);

                WaitForSignal(exceptionEvent, failureMessage: "Server exception event was not raised.");
                WaitForSignal(declinedEvent, failureMessage: "Client decline event was not raised.");
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000).ConfigureAwait(false);
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000).ConfigureAwait(false);

                TestAssert.NotNull(capturedException);
                TestAssert.True(capturedException.Message.IndexOf("auth exploded", StringComparison.OrdinalIgnoreCase) >= 0);
                TestAssert.True(!String.IsNullOrEmpty(declinedIpPort));
                TestAssert.Equal(DisconnectReason.ConnectionDeclined, declinedReason);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task BasicClientConnection()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                TestAssert.True(pair.Client.IsConnected);
                TestAssert.Single(pair.Server.GetClients());
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task BasicServerStartStop()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);
                TestAssert.True(server.IsListening);

                server.Stop();
                await WaitForServerStoppedAsync(server).ConfigureAwait(false);
                TestAssert.False(server.IsListening);
            }
            finally
            {
                SafeDispose(server);
            }
        }

        public static async Task ClientAndServerStatisticsTrackTrafficAndReset()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] clientToServer = Encoding.UTF8.GetBytes("abcde");
                byte[] serverToClient = Encoding.UTF8.GetBytes("1234567");

                await pair.Client.SendAsync(clientToServer).ConfigureAwait(false);
                ReadResult serverRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, clientToServer.Length).ConfigureAwait(false);
                AssertPayloadEquals(clientToServer, serverRead.Data);

                await pair.Server.SendAsync(pair.ServerClient.Guid, serverToClient).ConfigureAwait(false);
                ReadResult clientRead = await pair.Client.ReadAsync(serverToClient.Length).ConfigureAwait(false);
                AssertPayloadEquals(serverToClient, clientRead.Data);

                TestAssert.Equal((long)clientToServer.Length, pair.Client.Statistics.SentBytes);
                TestAssert.Equal((long)serverToClient.Length, pair.Client.Statistics.ReceivedBytes);
                TestAssert.Equal((long)serverToClient.Length, pair.Server.Statistics.SentBytes);
                TestAssert.Equal((long)clientToServer.Length, pair.Server.Statistics.ReceivedBytes);
                TestAssert.True(pair.Client.Statistics.StartTime <= DateTime.UtcNow);
                TestAssert.True(pair.Server.Statistics.StartTime <= DateTime.UtcNow);
                TestAssert.True(pair.Client.Statistics.UpTime >= TimeSpan.Zero);
                TestAssert.True(pair.Server.Statistics.UpTime >= TimeSpan.Zero);
                TestAssert.True(pair.Client.Statistics.ToString().IndexOf("Received bytes", StringComparison.OrdinalIgnoreCase) >= 0);
                TestAssert.True(pair.Server.Statistics.ToString().IndexOf("Sent bytes", StringComparison.OrdinalIgnoreCase) >= 0);

                pair.Client.Statistics.Reset();
                pair.Server.Statistics.Reset();

                TestAssert.Equal(0L, pair.Client.Statistics.SentBytes);
                TestAssert.Equal(0L, pair.Client.Statistics.ReceivedBytes);
                TestAssert.Equal(0L, pair.Server.Statistics.SentBytes);
                TestAssert.Equal(0L, pair.Server.Statistics.ReceivedBytes);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task TelemetryEmitsMetricsAndActivitiesForRoundTrip()
        {
            ConcurrentBag<string> metricNames = new ConcurrentBag<string>();
            ConcurrentBag<string> operationStatuses = new ConcurrentBag<string>();
            ConcurrentBag<string> activityNames = new ConcurrentBag<string>();

            using MeterListener meterListener = new MeterListener();
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CavemanTcpTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                metricNames.Add(instrument.Name);

                if (String.Equals(instrument.Name, CavemanTcpTelemetry.OperationsMetricName, StringComparison.Ordinal))
                {
                    foreach (KeyValuePair<string, object> tag in tags)
                    {
                        if (String.Equals(tag.Key, "cavemantcp.status", StringComparison.Ordinal) && tag.Value != null)
                        {
                            operationStatuses.Add(tag.Value.ToString());
                        }
                    }
                }
            });
            meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            {
                metricNames.Add(instrument.Name);
            });
            meterListener.Start();

            using ActivityListener activityListener = new ActivityListener
            {
                ShouldListenTo = source => String.Equals(source.Name, CavemanTcpTelemetry.ActivitySourceName, StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => activityNames.Add(activity.OperationName)
            };
            ActivitySource.AddActivityListener(activityListener);

            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] clientPayload = Encoding.UTF8.GetBytes("telemetry-client");
                byte[] serverPayload = Encoding.UTF8.GetBytes("telemetry-server");

                WriteResult clientWrite = await pair.Client.SendAsync(clientPayload).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, clientWrite.Status);
                ReadResult serverRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, clientPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, serverRead.Status);
                AssertPayloadEquals(clientPayload, serverRead.Data);

                WriteResult serverWrite = await pair.Server.SendAsync(pair.ServerClient.Guid, serverPayload).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, serverWrite.Status);
                ReadResult clientRead = await pair.Client.ReadAsync(serverPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, clientRead.Status);
                AssertPayloadEquals(serverPayload, clientRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }

            TestAssert.True(metricNames.Contains(CavemanTcpTelemetry.OperationsMetricName), "Operation counter was not emitted.");
            TestAssert.True(metricNames.Contains(CavemanTcpTelemetry.OperationDurationMetricName), "Operation duration histogram was not emitted.");
            TestAssert.True(metricNames.Contains(CavemanTcpTelemetry.BytesSentMetricName), "Bytes sent counter was not emitted.");
            TestAssert.True(metricNames.Contains(CavemanTcpTelemetry.BytesReceivedMetricName), "Bytes received counter was not emitted.");
            TestAssert.True(metricNames.Contains(CavemanTcpTelemetry.ConnectionsOpenedMetricName), "Connections opened counter was not emitted.");
            TestAssert.True(metricNames.Contains(CavemanTcpTelemetry.ConnectionsClosedMetricName), "Connections closed counter was not emitted.");
            TestAssert.True(metricNames.Contains(CavemanTcpTelemetry.ActiveConnectionsMetricName), "Active connections up/down counter was not emitted.");
            TestAssert.True(operationStatuses.Contains("success"), "Operation status tag was not emitted.");

            TestAssert.True(activityNames.Contains("cavemantcp.client.connect"), "Client connect activity was not emitted.");
            TestAssert.True(activityNames.Contains("cavemantcp.client.send"), "Client send activity was not emitted.");
            TestAssert.True(activityNames.Contains("cavemantcp.client.read"), "Client read activity was not emitted.");
            TestAssert.True(activityNames.Contains("cavemantcp.server.start"), "Server start activity was not emitted.");
            TestAssert.True(activityNames.Contains("cavemantcp.server.accept"), "Server accept activity was not emitted.");
            TestAssert.True(activityNames.Contains("cavemantcp.server.send"), "Server send activity was not emitted.");
            TestAssert.True(activityNames.Contains("cavemantcp.server.read"), "Server read activity was not emitted.");
        }

        public static Task ClientConnectFailureRaisesExceptionEvent()
        {
            int port = GetNextPort();
            Exception capturedException = null;
            using ManualResetEventSlim exceptionEvent = new ManualResetEventSlim(false);

            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
            client.Events.ExceptionEncountered += (s, e) =>
            {
                capturedException = e.Exception;
                exceptionEvent.Set();
            };

            try
            {
                Exception connectException = TestAssert.ThrowsAny<Exception>(() => client.Connect(1));
                WaitForSignal(exceptionEvent, failureMessage: "Client exception event was not raised.");

                TestAssert.NotNull(connectException);
                TestAssert.NotNull(capturedException);
                TestAssert.Equal(connectException.GetType(), capturedException.GetType());
                TestAssert.False(client.IsConnected);
            }
            finally
            {
                SafeDispose(client);
            }

            return Task.CompletedTask;
        }

        public static async Task ClientConnectAndDisconnectAreIdempotent()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                client.Connect(5);
                await WaitForServerClientCountAsync(server, 1).ConfigureAwait(false);

                client.Disconnect();
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000).ConfigureAwait(false);
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000).ConfigureAwait(false);

                client.Disconnect();
                TestAssert.False(client.IsConnected);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientCanReconnectAfterDisconnect()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                client.Disconnect();
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000).ConfigureAwait(false);
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                WriteResult write = client.Send("reconnect");
                TestAssert.Equal(WriteResultStatus.Success, write.Status);
                ReadResult read = server.Read(GetSingleClient(server).Guid, 9);
                TestAssert.Equal(ReadResultStatus.Success, read.Status);
                TestAssert.Equal("reconnect", Encoding.UTF8.GetString(read.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientCanReconnectRepeatedly()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                for (int i = 0; i < 3; i++)
                {
                    client.Connect(5);
                    await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                    string payload = "cycle-" + i;
                    WriteResult write = client.Send(payload);
                    TestAssert.Equal(WriteResultStatus.Success, write.Status);
                    ReadResult read = server.Read(GetSingleClient(server).Guid, payload.Length);
                    TestAssert.Equal(ReadResultStatus.Success, read.Status);
                    TestAssert.Equal(payload, Encoding.UTF8.GetString(read.Data));

                    client.Disconnect();
                    await WaitForClientDisconnectedAsync(client, timeoutMs: 5000).ConfigureAwait(false);
                    await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000).ConfigureAwait(false);
                }
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static Task ClientSendReturnsDisconnectedWhenNotConnected()
        {
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, GetNextPort(), false, null, null);
            byte[] payload = new byte[] { 0x01, 0x02, 0x03 };

            try
            {
                void AssertDisconnected(WriteResult result)
                {
                    TestAssert.Equal(WriteResultStatus.Disconnected, result.Status);
                    TestAssert.Equal(0L, result.BytesWritten);
                }

                AssertDisconnected(client.Send("x"));
                AssertDisconnected(client.Send(payload));
                AssertDisconnected(client.SendWithTimeout(-1, "y"));
                AssertDisconnected(client.SendWithTimeout(-1, payload));
                AssertDisconnected(client.SendAsync("z").GetAwaiter().GetResult());
                AssertDisconnected(client.SendAsync(payload).GetAwaiter().GetResult());
                AssertDisconnected(client.SendWithTimeoutAsync(-1, "w").GetAwaiter().GetResult());
                AssertDisconnected(client.SendWithTimeoutAsync(-1, payload).GetAwaiter().GetResult());
            }
            finally
            {
                SafeDispose(client);
            }

            return Task.CompletedTask;
        }

        public static async Task ClientReadAndStreamApisThrowWhenNotConnected()
        {
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, GetNextPort(), false, null, null);
            byte[] payload = new byte[] { 0x09 };

            try
            {
                using MemoryStream stream1 = new MemoryStream(payload, writable: false);
                using MemoryStream stream2 = new MemoryStream(payload, writable: false);
                using MemoryStream stream3 = new MemoryStream(payload, writable: false);
                using MemoryStream stream4 = new MemoryStream(payload, writable: false);

                TestAssert.ThrowsAny<IOException>(() => client.Send(1, stream1));
                TestAssert.ThrowsAny<IOException>(() => client.SendWithTimeout(-1, 1, stream2));
                await TestAssert.ThrowsAsync<IOException>(() => client.SendAsync(1, stream3)).ConfigureAwait(false);
                await TestAssert.ThrowsAsync<IOException>(() => client.SendWithTimeoutAsync(-1, 1, stream4)).ConfigureAwait(false);

                TestAssert.ThrowsAny<IOException>(() => client.Read(1));
                TestAssert.ThrowsAny<IOException>(() => client.ReadWithTimeout(-1, 1));
                await TestAssert.ThrowsAsync<IOException>(() => client.ReadAsync(1)).ConfigureAwait(false);
                await TestAssert.ThrowsAsync<IOException>(() => client.ReadWithTimeoutAsync(-1, 1)).ConfigureAwait(false);
                TestAssert.ThrowsAny<IOException>(() => client.GetStream());
            }
            finally
            {
                SafeDispose(client);
            }
        }

        public static async Task ClientEventsFireOnConnectAndDisconnect()
        {
            int port = GetNextPort();
            using ManualResetEventSlim connectedEvent = new ManualResetEventSlim(false);
            using ManualResetEventSlim disconnectedEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
            client.Events.ClientConnected += (s, e) => connectedEvent.Set();
            client.Events.ClientDisconnected += (s, e) => disconnectedEvent.Set();

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);
                WaitForSignal(connectedEvent, failureMessage: "Client connected event did not fire.");

                client.Disconnect();
                WaitForSignal(disconnectedEvent, timeoutMs: 5000, failureMessage: "Client disconnected event did not fire.");
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000).ConfigureAwait(false);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientMetadataPropertiesAndToStringWork()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                object metadata = new object();
                pair.ServerClient.Name = "primary-client";
                pair.ServerClient.Metadata = metadata;

                ClientMetadata listedClient = pair.Server.GetClients().Single();
                string text = listedClient.ToString();

                TestAssert.Equal(pair.ServerClient.Guid, listedClient.Guid);
                TestAssert.Equal(pair.ServerClient.IpPort, listedClient.IpPort);
                TestAssert.Equal("primary-client", listedClient.Name);
                TestAssert.True(Object.ReferenceEquals(metadata, listedClient.Metadata));
                TestAssert.True(text.IndexOf(pair.ServerClient.Guid.ToString(), StringComparison.OrdinalIgnoreCase) >= 0);
                TestAssert.True(text.IndexOf(pair.ServerClient.IpPort, StringComparison.OrdinalIgnoreCase) >= 0);
                TestAssert.True(text.IndexOf("primary-client", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ClientSendAndServerReadUsingStringAndBytes()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                string text = "hello-server";
                WriteResult textWrite = pair.Client.Send(text);
                TestAssert.Equal(WriteResultStatus.Success, textWrite.Status);
                TestAssert.Equal((long)text.Length, textWrite.BytesWritten);

                ReadResult textRead = pair.Server.Read(pair.ServerClient.Guid, text.Length);
                TestAssert.Equal(ReadResultStatus.Success, textRead.Status);
                TestAssert.Equal(text, Encoding.UTF8.GetString(textRead.Data));

                byte[] bytes = CreatePatternedPayload(32);
                WriteResult byteWrite = pair.Client.Send(bytes);
                TestAssert.Equal(WriteResultStatus.Success, byteWrite.Status);
                TestAssert.Equal((long)bytes.Length, byteWrite.BytesWritten);

                ReadResult byteRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, bytes.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, byteRead.Status);
                AssertPayloadEquals(bytes, byteRead.Data);

                byte[] timeoutBytes = CreatePatternedPayload(19);
                WriteResult timeoutByteWrite = pair.Client.SendWithTimeout(-1, timeoutBytes);
                TestAssert.Equal(WriteResultStatus.Success, timeoutByteWrite.Status);
                TestAssert.Equal((long)timeoutBytes.Length, timeoutByteWrite.BytesWritten);

                ReadResult timeoutByteRead = pair.Server.ReadWithTimeout(-1, pair.ServerClient.Guid, timeoutBytes.Length);
                TestAssert.Equal(ReadResultStatus.Success, timeoutByteRead.Status);
                AssertPayloadEquals(timeoutBytes, timeoutByteRead.Data);

                byte[] asyncTimeoutBytes = CreatePatternedPayload(23);
                WriteResult asyncTimeoutByteWrite = await pair.Client.SendWithTimeoutAsync(-1, asyncTimeoutBytes).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, asyncTimeoutByteWrite.Status);
                TestAssert.Equal((long)asyncTimeoutBytes.Length, asyncTimeoutByteWrite.BytesWritten);

                ReadResult asyncTimeoutByteRead = await pair.Server.ReadWithTimeoutAsync(-1, pair.ServerClient.Guid, asyncTimeoutBytes.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncTimeoutByteRead.Status);
                AssertPayloadEquals(asyncTimeoutBytes, asyncTimeoutByteRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ConcurrentClientSendsDeliverAllBytes()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                List<byte> expected = Enumerable.Range(1, 24).Select(i => (byte)i).ToList();
                List<Task<WriteResult>> sends = new List<Task<WriteResult>>();

                for (int i = 0; i < expected.Count; i++)
                {
                    byte value = expected[i];
                    byte[] payload = new[] { value };

                    if (i % 2 == 0) sends.Add(pair.Client.SendAsync(payload));
                    else sends.Add(pair.Client.SendWithTimeoutAsync(-1, payload));
                }

                WriteResult[] results = await Task.WhenAll(sends).ConfigureAwait(false);
                foreach (WriteResult result in results)
                {
                    TestAssert.Equal(WriteResultStatus.Success, result.Status);
                    TestAssert.Equal(1L, result.BytesWritten);
                }

                ReadResult read = await pair.Server.ReadAsync(pair.ServerClient.Guid, expected.Count).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, read.Status);
                AssertByteSetEquals(expected, read.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ConcurrentServerSendsDeliverAllBytes()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                List<byte> expected = Enumerable.Range(51, 24).Select(i => (byte)i).ToList();
                List<Task<WriteResult>> sends = new List<Task<WriteResult>>();

                for (int i = 0; i < expected.Count; i++)
                {
                    byte value = expected[i];
                    byte[] payload = new[] { value };

                    if (i % 4 == 0) sends.Add(pair.Server.SendAsync(pair.ServerClient.Guid, payload));
                    else if (i % 4 == 1) sends.Add(pair.Server.SendWithTimeoutAsync(-1, pair.ServerClient.Guid, payload));
                    else if (i % 4 == 2) sends.Add(pair.Server.SendAsync(pair.ServerClient.IpPort, payload));
                    else sends.Add(pair.Server.SendWithTimeoutAsync(-1, pair.ServerClient.IpPort, payload));
                }

                WriteResult[] results = await Task.WhenAll(sends).ConfigureAwait(false);
                foreach (WriteResult result in results)
                {
                    TestAssert.Equal(WriteResultStatus.Success, result.Status);
                    TestAssert.Equal(1L, result.BytesWritten);
                }

                ReadResult read = await pair.Client.ReadAsync(expected.Count).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, read.Status);
                AssertByteSetEquals(expected, read.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ClientStreamTimeoutAndAsyncOverloadsRoundTrip()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] syncTimeoutPayload = CreatePatternedPayload(257);
                using (MemoryStream syncTimeoutStream = new MemoryStream(syncTimeoutPayload, writable: false))
                {
                    WriteResult syncTimeoutWrite = pair.Client.SendWithTimeout(-1, syncTimeoutStream.Length, syncTimeoutStream);
                    TestAssert.Equal(WriteResultStatus.Success, syncTimeoutWrite.Status);
                    TestAssert.Equal((long)syncTimeoutPayload.Length, syncTimeoutWrite.BytesWritten);
                }

                ReadResult syncTimeoutRead = pair.Server.Read(pair.ServerClient.Guid, syncTimeoutPayload.Length);
                TestAssert.Equal(ReadResultStatus.Success, syncTimeoutRead.Status);
                AssertPayloadEquals(syncTimeoutPayload, syncTimeoutRead.Data);

                byte[] asyncPayload = CreatePatternedPayload(513);
                using (MemoryStream asyncStream = new MemoryStream(asyncPayload, writable: false))
                {
                    WriteResult asyncWrite = await pair.Client.SendAsync(asyncStream.Length, asyncStream).ConfigureAwait(false);
                    TestAssert.Equal(WriteResultStatus.Success, asyncWrite.Status);
                    TestAssert.Equal((long)asyncPayload.Length, asyncWrite.BytesWritten);
                }

                ReadResult asyncRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, asyncPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncRead.Status);
                AssertPayloadEquals(asyncPayload, asyncRead.Data);

                byte[] asyncTimeoutPayload = CreatePatternedPayload(769);
                using (MemoryStream asyncTimeoutStream = new MemoryStream(asyncTimeoutPayload, writable: false))
                {
                    WriteResult asyncTimeoutWrite = await pair.Client.SendWithTimeoutAsync(-1, asyncTimeoutStream.Length, asyncTimeoutStream).ConfigureAwait(false);
                    TestAssert.Equal(WriteResultStatus.Success, asyncTimeoutWrite.Status);
                    TestAssert.Equal((long)asyncTimeoutPayload.Length, asyncTimeoutWrite.BytesWritten);
                }

                ReadResult asyncTimeoutRead = await pair.Server.ReadWithTimeoutAsync(-1, pair.ServerClient.Guid, asyncTimeoutPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncTimeoutRead.Status);
                AssertPayloadEquals(asyncTimeoutPayload, asyncTimeoutRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ClientStringTimeoutAndAsyncOverloadsRoundTrip()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                const string syncTimeoutMessage = "client-sync-timeout";
                WriteResult syncTimeoutWrite = pair.Client.SendWithTimeout(-1, syncTimeoutMessage);
                TestAssert.Equal(WriteResultStatus.Success, syncTimeoutWrite.Status);
                TestAssert.Equal((long)syncTimeoutMessage.Length, syncTimeoutWrite.BytesWritten);

                ReadResult syncTimeoutRead = pair.Server.ReadWithTimeout(-1, pair.ServerClient.Guid, syncTimeoutMessage.Length);
                TestAssert.Equal(ReadResultStatus.Success, syncTimeoutRead.Status);
                TestAssert.Equal(syncTimeoutMessage, Encoding.UTF8.GetString(syncTimeoutRead.Data));

                const string asyncMessage = "client-async-string";
                WriteResult asyncWrite = await pair.Client.SendAsync(asyncMessage).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, asyncWrite.Status);
                TestAssert.Equal((long)asyncMessage.Length, asyncWrite.BytesWritten);

                ReadResult asyncRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, asyncMessage.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncRead.Status);
                TestAssert.Equal(asyncMessage, Encoding.UTF8.GetString(asyncRead.Data));

                const string asyncTimeoutMessage = "client-async-timeout";
                WriteResult asyncTimeoutWrite = await pair.Client.SendWithTimeoutAsync(-1, asyncTimeoutMessage).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, asyncTimeoutWrite.Status);
                TestAssert.Equal((long)asyncTimeoutMessage.Length, asyncTimeoutWrite.BytesWritten);

                ReadResult asyncTimeoutRead = await pair.Server.ReadWithTimeoutAsync(-1, pair.ServerClient.Guid, asyncTimeoutMessage.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncTimeoutRead.Status);
                TestAssert.Equal(asyncTimeoutMessage, Encoding.UTF8.GetString(asyncTimeoutRead.Data));
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ConnectionMonitorsDetectRemoteDisconnects()
        {
            int port1 = GetNextPort();
            using ManualResetEventSlim clientDisconnectedEvent = new ManualResetEventSlim(false);
            CavemanTcpServer server1 = new CavemanTcpServer(_Hostname, port1, false, null, null);
            CavemanTcpClient client1 = new CavemanTcpClient(_Hostname, port1, false, null, null);
            client1.Settings.PollIntervalMicroSeconds = 50000;
            client1.Events.ClientDisconnected += (s, e) => clientDisconnectedEvent.Set();

            try
            {
                server1.Start();
                await WaitForServerListeningAsync(server1).ConfigureAwait(false);

                client1.Connect(5);
                await WaitForClientConnectedAsync(client1, server1, 1).ConfigureAwait(false);
                ClientMetadata serverClient = GetSingleClient(server1);

                server1.DisconnectClient(serverClient.Guid);
                WaitForSignal(clientDisconnectedEvent, timeoutMs: 5000, failureMessage: "Client-side connection monitor did not raise disconnected event.");
                await WaitForClientDisconnectedAsync(client1, timeoutMs: 5000).ConfigureAwait(false);
            }
            finally
            {
                SafeDispose(client1);
                SafeDispose(server1);
            }

            int port2 = GetNextPort();
            using ManualResetEventSlim serverDisconnectedEvent = new ManualResetEventSlim(false);
            CavemanTcpServer server2 = new CavemanTcpServer(_Hostname, port2, false, null, null);
            CavemanTcpClient client2 = new CavemanTcpClient(_Hostname, port2, false, null, null);
            server2.Events.ClientDisconnected += (s, e) => serverDisconnectedEvent.Set();

            try
            {
                server2.Start();
                await WaitForServerListeningAsync(server2).ConfigureAwait(false);

                client2.Connect(5);
                await WaitForClientConnectedAsync(client2, server2, 1).ConfigureAwait(false);

                client2.Disconnect();
                WaitForSignal(serverDisconnectedEvent, timeoutMs: 5000, failureMessage: "Server-side connection monitor did not raise disconnected event.");
                await WaitForServerClientCountAsync(server2, 0, timeoutMs: 5000).ConfigureAwait(false);
            }
            finally
            {
                SafeDispose(client2);
                SafeDispose(server2);
            }
        }

        public static async Task InFlightReadOperationsCanBeCanceled()
        {
            var clientPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                using CancellationTokenSource clientCts = new CancellationTokenSource();
                Task<ReadResult> clientReadTask = clientPair.Client.ReadAsync(8, clientCts.Token);
                await Task.Delay(100).ConfigureAwait(false);
                clientCts.Cancel();

                ReadResult clientRead = await clientReadTask.ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Canceled, clientRead.Status);
                TestAssert.Equal(0L, clientRead.BytesRead);
            }
            finally
            {
                CleanupPair(clientPair);
            }

            var serverPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                using CancellationTokenSource serverCts = new CancellationTokenSource();
                Task<ReadResult> serverReadTask = serverPair.Server.ReadWithTimeoutAsync(-1, serverPair.ServerClient.Guid, 8, serverCts.Token);
                await Task.Delay(100).ConfigureAwait(false);
                serverCts.Cancel();

                ReadResult serverRead = await serverReadTask.ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Canceled, serverRead.Status);
                TestAssert.Equal(0L, serverRead.BytesRead);
            }
            finally
            {
                CleanupPair(serverPair);
            }
        }

        public static async Task InFlightSendOperationsCanBeCanceled()
        {
            var clientPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                using CancellationTokenSource clientCts = new CancellationTokenSource();
                using CancelableBlockingContentStream clientStream = new CancelableBlockingContentStream(new byte[] { 0x01 });

                Task<WriteResult> clientSendTask = clientPair.Client.SendAsync(1, clientStream, clientCts.Token);
                clientStream.WaitForReadStart();
                clientCts.Cancel();

                WriteResult clientSend = await clientSendTask.ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Canceled, clientSend.Status);
                TestAssert.Equal(0L, clientSend.BytesWritten);
            }
            finally
            {
                CleanupPair(clientPair);
            }

            var serverPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                using CancellationTokenSource serverCts = new CancellationTokenSource();
                using CancelableBlockingContentStream serverStream = new CancelableBlockingContentStream(new byte[] { 0x02 });

                Task<WriteResult> serverSendTask = serverPair.Server.SendWithTimeoutAsync(-1, serverPair.ServerClient.Guid, 1, serverStream, serverCts.Token);
                serverStream.WaitForReadStart();
                serverCts.Cancel();

                WriteResult serverSend = await serverSendTask.ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Canceled, serverSend.Status);
                TestAssert.Equal(0L, serverSend.BytesWritten);
            }
            finally
            {
                CleanupPair(serverPair);
            }
        }

        public static async Task EventHandlerExceptionsAreLogged()
        {
            ConcurrentQueue<string> serverLogs = CreateLogCapture(out Action<string> serverLogger);
            ConcurrentQueue<string> clientLogs = CreateLogCapture(out Action<string> clientLogger);

            int port = GetNextPort();
            using ManualResetEventSlim clientConnectedEvent = new ManualResetEventSlim(false);
            using ManualResetEventSlim serverDisconnectedEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            server.Logger = serverLogger;
            server.Events.ClientConnected += (s, e) => throw new InvalidOperationException("server connect handler exploded");
            server.Events.ClientDisconnected += (s, e) =>
            {
                serverDisconnectedEvent.Set();
                throw new InvalidOperationException("server disconnect handler exploded");
            };

            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
            client.Logger = clientLogger;
            client.Events.ClientConnected += (s, e) =>
            {
                clientConnectedEvent.Set();
                throw new InvalidOperationException("client connect handler exploded");
            };
            client.Events.ClientDisconnected += (s, e) => throw new InvalidOperationException("client disconnect handler exploded");

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);
                WaitForSignal(clientConnectedEvent, failureMessage: "Client did not connect.");

                client.Disconnect();
                WaitForSignal(serverDisconnectedEvent, timeoutMs: 5000, failureMessage: "Server disconnect event did not fire.");
                await WaitForConditionAsync(() =>
                    LogContains(serverLogs, "Event handler exception in ClientConnected")
                    && LogContains(serverLogs, "Event handler exception in ClientDisconnected")
                    && LogContains(clientLogs, "Event handler exception in ClientConnected")
                    && LogContains(clientLogs, "Event handler exception in ClientDisconnected"),
                    timeoutMs: 5000,
                    failureMessage: "Expected event-handler exception log messages were not emitted.").ConfigureAwait(false);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task GetStreamAndIsConnectedWork()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                TestAssert.True(pair.Client.IsConnected);
                TestAssert.True(pair.Server.IsConnected(pair.ServerClient.Guid));
                TestAssert.True(pair.Server.IsConnected(pair.ServerClient.IpPort));

                Stream clientStream = pair.Client.GetStream();
                Stream serverStreamByGuid = pair.Server.GetStream(pair.ServerClient.Guid);
                Stream serverStreamByIpPort = pair.Server.GetStream(pair.ServerClient.IpPort);

                TestAssert.NotNull(clientStream);
                TestAssert.NotNull(serverStreamByGuid);
                TestAssert.NotNull(serverStreamByIpPort);
                TestAssert.True(clientStream.CanRead && clientStream.CanWrite);
                TestAssert.True(serverStreamByGuid.CanRead && serverStreamByGuid.CanWrite);
                TestAssert.True(serverStreamByIpPort.CanRead && serverStreamByIpPort.CanWrite);

                byte[] fromClient = Encoding.UTF8.GetBytes("stream-client");
                await clientStream.WriteAsync(fromClient, 0, fromClient.Length).ConfigureAwait(false);
                ReadResult serverRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, fromClient.Length).ConfigureAwait(false);
                AssertPayloadEquals(fromClient, serverRead.Data);

                byte[] fromServer = Encoding.UTF8.GetBytes("stream-server");
                await serverStreamByGuid.WriteAsync(fromServer, 0, fromServer.Length).ConfigureAwait(false);
                ReadResult clientRead = await pair.Client.ReadAsync(fromServer.Length).ConfigureAwait(false);
                AssertPayloadEquals(fromServer, clientRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task IpPortPfxConstructorsRoundTrip()
        {
            int port = GetNextPort();
            string ipPort = _Hostname + ":" + port;
            string certificatePath = GetCertificatePath();

            CavemanTcpServer server = new CavemanTcpServer(ipPort, true, certificatePath, CertificatePassword);
            CavemanTcpClient client = new CavemanTcpClient(ipPort, true, certificatePath, CertificatePassword);
            client.Settings.AcceptInvalidCertificates = true;

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server, timeoutMs: 5000).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000).ConfigureAwait(false);
                ClientMetadata serverClient = GetSingleClient(server);

                WriteResult clientWrite = client.Send("ipport-ssl");
                TestAssert.Equal(WriteResultStatus.Success, clientWrite.Status);
                ReadResult serverRead = server.Read(serverClient.Guid, 10);
                TestAssert.Equal(ReadResultStatus.Success, serverRead.Status);
                TestAssert.Equal("ipport-ssl", Encoding.UTF8.GetString(serverRead.Data));

                WriteResult serverWrite = await server.SendAsync(serverClient.Guid, Encoding.UTF8.GetBytes("ssl-ipport")).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, serverWrite.Status);
                ReadResult clientRead = await client.ReadAsync(10).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, clientRead.Status);
                TestAssert.Equal("ssl-ipport", Encoding.UTF8.GetString(clientRead.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task IpPortConstructorsRoundTrip()
        {
            int port = GetNextPort();
            string ipPort = _Hostname + ":" + port;

            CavemanTcpServer server = new CavemanTcpServer(ipPort);
            CavemanTcpClient client = new CavemanTcpClient(ipPort);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                WriteResult write = client.Send("ipport");
                TestAssert.Equal(WriteResultStatus.Success, write.Status);
                ReadResult read = server.Read(GetSingleClient(server).Guid, 6);
                TestAssert.Equal(ReadResultStatus.Success, read.Status);
                TestAssert.Equal("ipport", Encoding.UTF8.GetString(read.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task LargePayloadRoundTrip()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] payload = CreatePatternedPayload(128 * 1024);
                WriteResult write = await pair.Client.SendAsync(payload).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, write.Status);
                TestAssert.Equal((long)payload.Length, write.BytesWritten);

                ReadResult read = await pair.Server.ReadAsync(pair.ServerClient.Guid, payload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, read.Status);
                TestAssert.Equal((long)payload.Length, read.BytesRead);
                AssertPayloadEquals(payload, read.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task LargePayloadServerToClientRoundTrip()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] payload = CreatePatternedPayload(128 * 1024);
                WriteResult write = await pair.Server.SendAsync(pair.ServerClient.Guid, payload).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, write.Status);
                TestAssert.Equal((long)payload.Length, write.BytesWritten);

                ReadResult read = await pair.Client.ReadAsync(payload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, read.Status);
                TestAssert.Equal((long)payload.Length, read.BytesRead);
                AssertPayloadEquals(payload, read.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task MultipleClientsReceiveDistinctMessages()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            List<CavemanTcpClient> clients = new List<CavemanTcpClient>();

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                for (int i = 0; i < 3; i++)
                {
                    CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
                    client.Connect(5);
                    clients.Add(client);
                }

                await WaitForServerClientCountAsync(server, 3).ConfigureAwait(false);
                List<ClientMetadata> serverClients = server.GetClients().OrderBy(c => c.IpPort, StringComparer.Ordinal).ToList();
                TestAssert.Equal(3, serverClients.Count);

                for (int i = 0; i < serverClients.Count; i++)
                {
                    string message = "client-" + i;
                    WriteResult write = server.Send(serverClients[i].Guid, message);
                    TestAssert.Equal(WriteResultStatus.Success, write.Status);
                }

                for (int i = 0; i < clients.Count; i++)
                {
                    string expected = "client-" + i;
                    ReadResult read = clients[i].Read(expected.Length);
                    TestAssert.Equal(ReadResultStatus.Success, read.Status);
                    TestAssert.Equal(expected, Encoding.UTF8.GetString(read.Data));
                }
            }
            finally
            {
                foreach (CavemanTcpClient client in clients)
                {
                    SafeDispose(client);
                }

                SafeDispose(server);
            }
        }

        public static async Task ReadOperationsReturnDisconnectedWhenPeerDisconnects()
        {
            var clientPair = await CreateConnectedPairAsync(configureClient: c => c.Settings.PollIntervalMicroSeconds = 50000).ConfigureAwait(false);

            try
            {
                Task<ReadResult> clientReadTask = clientPair.Client.ReadAsync(16);
                await Task.Delay(100).ConfigureAwait(false);
                clientPair.Server.DisconnectClient(clientPair.ServerClient.Guid);

                ReadResult clientRead = await clientReadTask.ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Disconnected, clientRead.Status);
                await WaitForClientDisconnectedAsync(clientPair.Client, timeoutMs: 5000).ConfigureAwait(false);
            }
            finally
            {
                CleanupPair(clientPair);
            }

            var serverPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                Task<ReadResult> serverReadTask = serverPair.Server.ReadAsync(serverPair.ServerClient.Guid, 16);
                await Task.Delay(100).ConfigureAwait(false);
                serverPair.Client.Disconnect();

                ReadResult serverRead = await serverReadTask.ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Disconnected, serverRead.Status);
                await WaitForServerClientCountAsync(serverPair.Server, 0, timeoutMs: 5000).ConfigureAwait(false);
            }
            finally
            {
                CleanupPair(serverPair);
            }
        }

        public static async Task ReadOperationsReturnTimeoutAndPartialData()
        {
            var clientSyncPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                await clientSyncPair.Server.SendAsync(clientSyncPair.ServerClient.Guid, Encoding.UTF8.GetBytes("part")).ConfigureAwait(false);
                ReadResult clientSync = clientSyncPair.Client.ReadWithTimeout(200, 8);
                TestAssert.Equal(ReadResultStatus.Timeout, clientSync.Status);
                TestAssert.Equal(4L, clientSync.BytesRead);
                TestAssert.Equal("part", Encoding.UTF8.GetString(clientSync.Data));
            }
            finally
            {
                CleanupPair(clientSyncPair);
            }

            var clientAsyncPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                await clientAsyncPair.Server.SendAsync(clientAsyncPair.ServerClient.Guid, Encoding.UTF8.GetBytes("async")).ConfigureAwait(false);
                ReadResult clientAsync = await clientAsyncPair.Client.ReadWithTimeoutAsync(200, 9).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Timeout, clientAsync.Status);
                TestAssert.Equal(5L, clientAsync.BytesRead);
                TestAssert.Equal("async", Encoding.UTF8.GetString(clientAsync.Data));
            }
            finally
            {
                CleanupPair(clientAsyncPair);
            }

            var serverSyncPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                await serverSyncPair.Client.SendAsync(Encoding.UTF8.GetBytes("srv")).ConfigureAwait(false);
                ReadResult serverSync = serverSyncPair.Server.ReadWithTimeout(200, serverSyncPair.ServerClient.Guid, 6);
                TestAssert.Equal(ReadResultStatus.Timeout, serverSync.Status);
                TestAssert.Equal(3L, serverSync.BytesRead);
                TestAssert.Equal("srv", Encoding.UTF8.GetString(serverSync.Data));
            }
            finally
            {
                CleanupPair(serverSyncPair);
            }

            var serverAsyncPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                await serverAsyncPair.Client.SendAsync(Encoding.UTF8.GetBytes("node")).ConfigureAwait(false);
                ReadResult serverAsync = await serverAsyncPair.Server.ReadWithTimeoutAsync(200, serverAsyncPair.ServerClient.Guid, 9).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Timeout, serverAsync.Status);
                TestAssert.Equal(4L, serverAsync.BytesRead);
                TestAssert.Equal("node", Encoding.UTF8.GetString(serverAsync.Data));
            }
            finally
            {
                CleanupPair(serverAsyncPair);
            }
        }

        public static async Task ReadWithTimeoutReturnsTimeoutWithNoDataWhenNothingIsSent()
        {
            void AssertEmptyTimeout(ReadResult result)
            {
                TestAssert.Equal(ReadResultStatus.Timeout, result.Status);
                TestAssert.Equal(0L, result.BytesRead);
                TestAssert.Null(result.Data);
            }

            var clientSyncPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                AssertEmptyTimeout(clientSyncPair.Client.ReadWithTimeout(200, 8));
            }
            finally
            {
                CleanupPair(clientSyncPair);
            }

            var clientAsyncPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                AssertEmptyTimeout(await clientAsyncPair.Client.ReadWithTimeoutAsync(200, 8).ConfigureAwait(false));
            }
            finally
            {
                CleanupPair(clientAsyncPair);
            }

            var serverSyncPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                AssertEmptyTimeout(serverSyncPair.Server.ReadWithTimeout(200, serverSyncPair.ServerClient.Guid, 8));
            }
            finally
            {
                CleanupPair(serverSyncPair);
            }

            var serverAsyncPair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                AssertEmptyTimeout(await serverAsyncPair.Server.ReadWithTimeoutAsync(200, serverAsyncPair.ServerClient.Guid, 8).ConfigureAwait(false));
            }
            finally
            {
                CleanupPair(serverAsyncPair);
            }
        }

        public static async Task ReadResultCachesData()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] payload = Encoding.UTF8.GetBytes("cache-test");
                await pair.Server.SendAsync(pair.ServerClient.Guid, payload).ConfigureAwait(false);
                ReadResult result = pair.Client.Read(payload.Length);

                byte[] first = result.Data;
                byte[] second = result.Data;

                TestAssert.True(Object.ReferenceEquals(first, second));
                TestAssert.Equal(result.DataStream.Length, result.DataStream.Position);
                AssertPayloadEquals(payload, first);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static Task ResultObjectsExposeExpectedDefaultsAndConstructors()
        {
            WriteResult defaultWrite = new WriteResult();
            TestAssert.Equal(WriteResultStatus.Success, defaultWrite.Status);
            TestAssert.Equal(0L, defaultWrite.BytesWritten);

            WriteResult constructedWrite = new WriteResult(WriteResultStatus.Timeout, 42);
            TestAssert.Equal(WriteResultStatus.Timeout, constructedWrite.Status);
            TestAssert.Equal(42L, constructedWrite.BytesWritten);

            ReadResult defaultRead = new ReadResult();
            TestAssert.Equal(ReadResultStatus.Success, defaultRead.Status);
            TestAssert.Equal(0L, defaultRead.BytesRead);
            TestAssert.Null(defaultRead.Data);

            byte[] payload = Encoding.UTF8.GetBytes("read-result");
            using MemoryStream stream = new MemoryStream(payload, writable: false);
            ReadResult constructedRead = new ReadResult(ReadResultStatus.Success, payload.Length, stream);

            TestAssert.Equal(ReadResultStatus.Success, constructedRead.Status);
            TestAssert.Equal((long)payload.Length, constructedRead.BytesRead);
            AssertPayloadEquals(payload, constructedRead.Data);
            TestAssert.Equal(constructedRead.DataStream.Length, constructedRead.DataStream.Position);

            ReadResult nullDataRead = new ReadResult(ReadResultStatus.Timeout, 0, null);
            TestAssert.Equal(ReadResultStatus.Timeout, nullDataRead.Status);
            TestAssert.Null(nullDataRead.Data);

            return Task.CompletedTask;
        }

        public static async Task ServerGetClientsReturnsSnapshot()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                List<ClientMetadata> snapshot = server.GetClients().ToList();
                TestAssert.Single(snapshot);
                Guid clientGuid = snapshot[0].Guid;

                client.Disconnect();
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000).ConfigureAwait(false);

                TestAssert.Single(snapshot);
                TestAssert.Equal(clientGuid, snapshot[0].Guid);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static Task PropertySettersRetainAssignedInstances()
        {
            CavemanTcpClientSettings clientSettings = new CavemanTcpClientSettings();
            CavemanTcpClientEvents clientEvents = new CavemanTcpClientEvents();
            CavemanTcpKeepaliveSettings clientKeepalive = new CavemanTcpKeepaliveSettings();
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, GetNextPort(), false, null, null);

            CavemanTcpServerSettings serverSettings = new CavemanTcpServerSettings();
            CavemanTcpServerEvents serverEvents = new CavemanTcpServerEvents();
            CavemanTcpKeepaliveSettings serverKeepalive = new CavemanTcpKeepaliveSettings();
            CavemanTcpServerCallbacks serverCallbacks = new CavemanTcpServerCallbacks();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, GetNextPort(), false, null, null);

            try
            {
                client.Settings = clientSettings;
                client.Events = clientEvents;
                client.Keepalive = clientKeepalive;

                server.Settings = serverSettings;
                server.Events = serverEvents;
                server.Keepalive = serverKeepalive;
                server.Callbacks = serverCallbacks;

                TestAssert.True(Object.ReferenceEquals(clientSettings, client.Settings));
                TestAssert.True(Object.ReferenceEquals(clientEvents, client.Events));
                TestAssert.True(Object.ReferenceEquals(clientKeepalive, client.Keepalive));

                TestAssert.True(Object.ReferenceEquals(serverSettings, server.Settings));
                TestAssert.True(Object.ReferenceEquals(serverEvents, server.Events));
                TestAssert.True(Object.ReferenceEquals(serverKeepalive, server.Keepalive));
                TestAssert.True(Object.ReferenceEquals(serverCallbacks, server.Callbacks));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }

            return Task.CompletedTask;
        }

        public static async Task SendOperationsReturnTimeoutWhenWriteSemaphoreIsBusy()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                BlockingContentStream clientBlockingStream = new BlockingContentStream(new byte[] { 0x01 });

                try
                {
                    Task<WriteResult> blockingClientSend = pair.Client.SendAsync(1, clientBlockingStream);
                    clientBlockingStream.WaitForReadStart();

                    WriteResult clientSyncTimeout = pair.Client.SendWithTimeout(100, new byte[] { 0x02 });
                    WriteResult clientAsyncTimeout = await pair.Client.SendWithTimeoutAsync(100, new byte[] { 0x03 }).ConfigureAwait(false);

                    TestAssert.Equal(WriteResultStatus.Timeout, clientSyncTimeout.Status);
                    TestAssert.Equal(WriteResultStatus.Timeout, clientAsyncTimeout.Status);

                    clientBlockingStream.Release();
                    WriteResult completedClientSend = await blockingClientSend.ConfigureAwait(false);
                    TestAssert.Equal(WriteResultStatus.Success, completedClientSend.Status);
                    await pair.Server.ReadAsync(pair.ServerClient.Guid, 1).ConfigureAwait(false);
                }
                finally
                {
                    clientBlockingStream.Release();
                    clientBlockingStream.Dispose();
                }

                BlockingContentStream serverBlockingStream = new BlockingContentStream(new byte[] { 0x04 });

                try
                {
                    Task<WriteResult> blockingServerSend = pair.Server.SendAsync(pair.ServerClient.Guid, 1, serverBlockingStream);
                    serverBlockingStream.WaitForReadStart();

                    WriteResult serverSyncTimeout = pair.Server.SendWithTimeout(100, pair.ServerClient.Guid, new byte[] { 0x05 });
                    WriteResult serverAsyncTimeout = await pair.Server.SendWithTimeoutAsync(100, pair.ServerClient.Guid, new byte[] { 0x06 }).ConfigureAwait(false);

                    TestAssert.Equal(WriteResultStatus.Timeout, serverSyncTimeout.Status);
                    TestAssert.Equal(WriteResultStatus.Timeout, serverAsyncTimeout.Status);

                    serverBlockingStream.Release();
                    WriteResult completedServerSend = await blockingServerSend.ConfigureAwait(false);
                    TestAssert.Equal(WriteResultStatus.Success, completedServerSend.Status);
                    await pair.Client.ReadAsync(1).ConfigureAwait(false);
                }
                finally
                {
                    serverBlockingStream.Release();
                    serverBlockingStream.Dispose();
                }
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ServerClientNotFoundApisReturnExpectedResults()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            Guid fakeGuid = Guid.NewGuid();
            string fakeIpPort = "127.0.0.1:65530";

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                TestAssert.Equal(WriteResultStatus.ClientNotFound, server.Send(fakeGuid, "x").Status);
                TestAssert.Equal(WriteResultStatus.ClientNotFound, server.Send(fakeIpPort, "x").Status);
                TestAssert.Equal(ReadResultStatus.ClientNotFound, server.Read(fakeGuid, 1).Status);
                TestAssert.Equal(ReadResultStatus.ClientNotFound, server.Read(fakeIpPort, 1).Status);

                TestAssert.Equal(WriteResultStatus.ClientNotFound, (await server.SendAsync(fakeGuid, new byte[] { 0x01 }).ConfigureAwait(false)).Status);
                TestAssert.Equal(WriteResultStatus.ClientNotFound, (await server.SendAsync(fakeIpPort, new byte[] { 0x01 }).ConfigureAwait(false)).Status);
                TestAssert.Equal(ReadResultStatus.ClientNotFound, (await server.ReadAsync(fakeGuid, 1).ConfigureAwait(false)).Status);
                TestAssert.Equal(ReadResultStatus.ClientNotFound, (await server.ReadAsync(fakeIpPort, 1).ConfigureAwait(false)).Status);

                TestAssert.False(server.IsConnected(fakeGuid));
                TestAssert.False(server.IsConnected(fakeIpPort));

                server.DisconnectClient(fakeGuid);
                server.DisconnectClient(fakeIpPort);

                TestAssert.Throws<KeyNotFoundException>(() => server.GetStream(fakeGuid));
                TestAssert.Throws<KeyNotFoundException>(() => server.GetStream(fakeIpPort));
            }
            finally
            {
                SafeDispose(server);
            }
        }

        public static Task ServerConnectedEventProvidesMetadataAndEndpoint()
        {
            int port = GetNextPort();
            ClientConnectedEventArgs connectedArgs = null;
            using ManualResetEventSlim connectedEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
            server.Events.ClientConnected += (s, e) =>
            {
                connectedArgs = e;
                connectedEvent.Set();
            };

            try
            {
                server.Start();
                WaitForServerListeningAsync(server).GetAwaiter().GetResult();

                client.Connect(5);
                WaitForClientConnectedAsync(client, server, 1).GetAwaiter().GetResult();
                WaitForSignal(connectedEvent, failureMessage: "Connected event was not raised.");

                TestAssert.NotNull(connectedArgs);
                TestAssert.NotNull(connectedArgs.Client);
                TestAssert.NotNull(connectedArgs.LocalEndpoint);
                TestAssert.NotEqual(Guid.Empty, connectedArgs.Client.Guid);
                TestAssert.True(!String.IsNullOrEmpty(connectedArgs.Client.IpPort));
                TestAssert.Equal(port, ((IPEndPoint)connectedArgs.LocalEndpoint).Port);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }

            return Task.CompletedTask;
        }

        public static async Task ServerEventsFireOnConnectAndDisconnect()
        {
            int port = GetNextPort();
            Guid connectedGuid = Guid.Empty;
            Guid disconnectedGuid = Guid.Empty;
            DisconnectReason disconnectReason = DisconnectReason.Normal;
            using ManualResetEventSlim connectedEvent = new ManualResetEventSlim(false);
            using ManualResetEventSlim disconnectedEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            server.Events.ClientConnected += (s, e) =>
            {
                connectedGuid = e.Client.Guid;
                connectedEvent.Set();
            };
            server.Events.ClientDisconnected += (s, e) =>
            {
                disconnectedGuid = e.Client.Guid;
                disconnectReason = e.Reason;
                disconnectedEvent.Set();
            };

            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);
                WaitForSignal(connectedEvent, failureMessage: "Server connected event did not fire.");

                server.DisconnectClient(connectedGuid);
                WaitForSignal(disconnectedEvent, timeoutMs: 5000, failureMessage: "Server disconnected event did not fire.");
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000).ConfigureAwait(false);

                TestAssert.NotEqual(Guid.Empty, connectedGuid);
                TestAssert.Equal(connectedGuid, disconnectedGuid);
                TestAssert.Equal(DisconnectReason.Kicked, disconnectReason);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerClientDisconnectRaisesKickedReason()
        {
            int port = GetNextPort();
            Guid disconnectedGuid = Guid.Empty;
            DisconnectReason disconnectReason = DisconnectReason.Normal;
            using ManualResetEventSlim disconnectedEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            server.Events.ClientDisconnected += (s, e) =>
            {
                disconnectedGuid = e.Client.Guid;
                disconnectReason = e.Reason;
                disconnectedEvent.Set();
            };

            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);
                Guid expectedGuid = GetSingleClient(server).Guid;

                client.Disconnect();
                WaitForSignal(disconnectedEvent, timeoutMs: 5000, failureMessage: "Server disconnected event did not fire for client-initiated disconnect.");
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000).ConfigureAwait(false);

                TestAssert.Equal(expectedGuid, disconnectedGuid);
                TestAssert.Equal(DisconnectReason.Kicked, disconnectReason);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerGuidStreamTimeoutAndAsyncOverloadsRoundTrip()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] syncPayload = CreatePatternedPayload(123);
                using (MemoryStream syncStream = new MemoryStream(syncPayload, writable: false))
                {
                    WriteResult syncWrite = pair.Server.Send(pair.ServerClient.Guid, syncStream.Length, syncStream);
                    TestAssert.Equal(WriteResultStatus.Success, syncWrite.Status);
                    TestAssert.Equal((long)syncPayload.Length, syncWrite.BytesWritten);
                }

                ReadResult syncRead = pair.Client.Read(syncPayload.Length);
                TestAssert.Equal(ReadResultStatus.Success, syncRead.Status);
                AssertPayloadEquals(syncPayload, syncRead.Data);

                byte[] syncTimeoutPayload = CreatePatternedPayload(231);
                using (MemoryStream syncTimeoutStream = new MemoryStream(syncTimeoutPayload, writable: false))
                {
                    WriteResult syncTimeoutWrite = pair.Server.SendWithTimeout(-1, pair.ServerClient.Guid, syncTimeoutStream.Length, syncTimeoutStream);
                    TestAssert.Equal(WriteResultStatus.Success, syncTimeoutWrite.Status);
                    TestAssert.Equal((long)syncTimeoutPayload.Length, syncTimeoutWrite.BytesWritten);
                }

                ReadResult syncTimeoutRead = pair.Client.ReadWithTimeout(-1, syncTimeoutPayload.Length);
                TestAssert.Equal(ReadResultStatus.Success, syncTimeoutRead.Status);
                AssertPayloadEquals(syncTimeoutPayload, syncTimeoutRead.Data);

                byte[] asyncPayload = CreatePatternedPayload(345);
                using (MemoryStream asyncStream = new MemoryStream(asyncPayload, writable: false))
                {
                    WriteResult asyncWrite = await pair.Server.SendAsync(pair.ServerClient.Guid, asyncStream.Length, asyncStream).ConfigureAwait(false);
                    TestAssert.Equal(WriteResultStatus.Success, asyncWrite.Status);
                    TestAssert.Equal((long)asyncPayload.Length, asyncWrite.BytesWritten);
                }

                ReadResult asyncRead = await pair.Client.ReadAsync(asyncPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncRead.Status);
                AssertPayloadEquals(asyncPayload, asyncRead.Data);

                byte[] asyncTimeoutPayload = CreatePatternedPayload(457);
                using (MemoryStream asyncTimeoutStream = new MemoryStream(asyncTimeoutPayload, writable: false))
                {
                    WriteResult asyncTimeoutWrite = await pair.Server.SendWithTimeoutAsync(-1, pair.ServerClient.Guid, asyncTimeoutStream.Length, asyncTimeoutStream).ConfigureAwait(false);
                    TestAssert.Equal(WriteResultStatus.Success, asyncTimeoutWrite.Status);
                    TestAssert.Equal((long)asyncTimeoutPayload.Length, asyncTimeoutWrite.BytesWritten);
                }

                ReadResult asyncTimeoutRead = await pair.Client.ReadWithTimeoutAsync(-1, asyncTimeoutPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncTimeoutRead.Status);
                AssertPayloadEquals(asyncTimeoutPayload, asyncTimeoutRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ServerGuidStringTimeoutAndAsyncOverloadsRoundTrip()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                const string syncTimeoutMessage = "server-sync-timeout";
                WriteResult syncTimeoutWrite = pair.Server.SendWithTimeout(-1, pair.ServerClient.Guid, syncTimeoutMessage);
                TestAssert.Equal(WriteResultStatus.Success, syncTimeoutWrite.Status);
                TestAssert.Equal((long)syncTimeoutMessage.Length, syncTimeoutWrite.BytesWritten);

                ReadResult syncTimeoutRead = pair.Client.ReadWithTimeout(-1, syncTimeoutMessage.Length);
                TestAssert.Equal(ReadResultStatus.Success, syncTimeoutRead.Status);
                TestAssert.Equal(syncTimeoutMessage, Encoding.UTF8.GetString(syncTimeoutRead.Data));

                const string asyncMessage = "server-async-string";
                WriteResult asyncWrite = await pair.Server.SendAsync(pair.ServerClient.Guid, asyncMessage).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, asyncWrite.Status);
                TestAssert.Equal((long)asyncMessage.Length, asyncWrite.BytesWritten);

                ReadResult asyncRead = await pair.Client.ReadAsync(asyncMessage.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncRead.Status);
                TestAssert.Equal(asyncMessage, Encoding.UTF8.GetString(asyncRead.Data));

                const string asyncTimeoutMessage = "server-async-timeout";
                WriteResult asyncTimeoutWrite = await pair.Server.SendWithTimeoutAsync(-1, pair.ServerClient.Guid, asyncTimeoutMessage).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, asyncTimeoutWrite.Status);
                TestAssert.Equal((long)asyncTimeoutMessage.Length, asyncTimeoutWrite.BytesWritten);

                ReadResult asyncTimeoutRead = await pair.Client.ReadWithTimeoutAsync(-1, asyncTimeoutMessage.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncTimeoutRead.Status);
                TestAssert.Equal(asyncTimeoutMessage, Encoding.UTF8.GetString(asyncTimeoutRead.Data));
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ServerIpPortStreamOverloadsWork()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                string ipPort = pair.ServerClient.IpPort;

                byte[] syncPayload = CreatePatternedPayload(111);
                using (MemoryStream syncStream = new MemoryStream(syncPayload, writable: false))
                {
                    WriteResult syncWrite = pair.Server.Send(ipPort, syncStream.Length, syncStream);
                    TestAssert.Equal(WriteResultStatus.Success, syncWrite.Status);
                    TestAssert.Equal((long)syncPayload.Length, syncWrite.BytesWritten);
                }

                ReadResult syncRead = pair.Client.Read(syncPayload.Length);
                TestAssert.Equal(ReadResultStatus.Success, syncRead.Status);
                AssertPayloadEquals(syncPayload, syncRead.Data);

                byte[] syncTimeoutPayload = CreatePatternedPayload(222);
                using (MemoryStream syncTimeoutStream = new MemoryStream(syncTimeoutPayload, writable: false))
                {
                    WriteResult syncTimeoutWrite = pair.Server.SendWithTimeout(-1, ipPort, syncTimeoutStream.Length, syncTimeoutStream);
                    TestAssert.Equal(WriteResultStatus.Success, syncTimeoutWrite.Status);
                    TestAssert.Equal((long)syncTimeoutPayload.Length, syncTimeoutWrite.BytesWritten);
                }

                ReadResult syncTimeoutRead = pair.Client.ReadWithTimeout(-1, syncTimeoutPayload.Length);
                TestAssert.Equal(ReadResultStatus.Success, syncTimeoutRead.Status);
                AssertPayloadEquals(syncTimeoutPayload, syncTimeoutRead.Data);

                byte[] asyncPayload = CreatePatternedPayload(333);
                using (MemoryStream asyncStream = new MemoryStream(asyncPayload, writable: false))
                {
                    WriteResult asyncWrite = await pair.Server.SendAsync(ipPort, asyncStream.Length, asyncStream).ConfigureAwait(false);
                    TestAssert.Equal(WriteResultStatus.Success, asyncWrite.Status);
                    TestAssert.Equal((long)asyncPayload.Length, asyncWrite.BytesWritten);
                }

                ReadResult asyncRead = await pair.Client.ReadAsync(asyncPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncRead.Status);
                AssertPayloadEquals(asyncPayload, asyncRead.Data);

                byte[] asyncTimeoutPayload = CreatePatternedPayload(444);
                using (MemoryStream asyncTimeoutStream = new MemoryStream(asyncTimeoutPayload, writable: false))
                {
                    WriteResult asyncTimeoutWrite = await pair.Server.SendWithTimeoutAsync(-1, ipPort, asyncTimeoutStream.Length, asyncTimeoutStream).ConfigureAwait(false);
                    TestAssert.Equal(WriteResultStatus.Success, asyncTimeoutWrite.Status);
                    TestAssert.Equal((long)asyncTimeoutPayload.Length, asyncTimeoutWrite.BytesWritten);
                }

                ReadResult asyncTimeoutRead = await pair.Client.ReadWithTimeoutAsync(-1, asyncTimeoutPayload.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncTimeoutRead.Status);
                AssertPayloadEquals(asyncTimeoutPayload, asyncTimeoutRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ServerMaxConnectionsPausesAndResumes()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            server.Settings.MaxConnections = 1;

            CavemanTcpClient client1 = new CavemanTcpClient(_Hostname, port, false, null, null);
            CavemanTcpClient client2 = new CavemanTcpClient(_Hostname, port, false, null, null);
            CavemanTcpClient client3 = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client1.Connect(5);
                await WaitForClientConnectedAsync(client1, server, 1).ConfigureAwait(false);
                await WaitForConditionAsync(() => !server.IsListening, timeoutMs: 5000, failureMessage: "Server did not pause after reaching max connections.").ConfigureAwait(false);

                TryConnectAndIgnoreFailure(client2);
                await WaitForServerClientCountAsync(server, 1).ConfigureAwait(false);

                client1.Disconnect();
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000).ConfigureAwait(false);
                await WaitForConditionAsync(() => server.IsListening, timeoutMs: 5000, failureMessage: "Server did not resume listening after a slot freed up.").ConfigureAwait(false);

                client3.Connect(5);
                await WaitForClientConnectedAsync(client3, server, 1).ConfigureAwait(false);
            }
            finally
            {
                SafeDispose(client1);
                SafeDispose(client2);
                SafeDispose(client3);
                SafeDispose(server);
            }
        }

        public static async Task ServerNotFoundTimeoutAndStreamApisReturnExpectedResults()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            Guid fakeGuid = Guid.NewGuid();
            string fakeIpPort = "127.0.0.1:65529";

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                void AssertWriteClientNotFound(WriteResult result)
                {
                    TestAssert.Equal(WriteResultStatus.ClientNotFound, result.Status);
                    TestAssert.Equal(0L, result.BytesWritten);
                }

                void AssertReadClientNotFound(ReadResult result)
                {
                    TestAssert.Equal(ReadResultStatus.ClientNotFound, result.Status);
                    TestAssert.Equal(0L, result.BytesRead);
                    TestAssert.Null(result.Data);
                }

                AssertWriteClientNotFound(server.SendWithTimeout(-1, fakeGuid, "guid-timeout-string"));
                AssertWriteClientNotFound(server.SendWithTimeout(-1, fakeIpPort, "ip-timeout-string"));
                AssertWriteClientNotFound(await server.SendAsync(fakeGuid, "guid-async-string").ConfigureAwait(false));
                AssertWriteClientNotFound(await server.SendAsync(fakeIpPort, "ip-async-string").ConfigureAwait(false));
                AssertWriteClientNotFound(await server.SendWithTimeoutAsync(-1, fakeGuid, "guid-async-timeout-string").ConfigureAwait(false));
                AssertWriteClientNotFound(await server.SendWithTimeoutAsync(-1, fakeIpPort, "ip-async-timeout-string").ConfigureAwait(false));

                using (MemoryStream guidSyncStream = new MemoryStream(new byte[] { 0x01 }, writable: false))
                {
                    AssertWriteClientNotFound(server.Send(fakeGuid, guidSyncStream.Length, guidSyncStream));
                }

                using (MemoryStream ipSyncStream = new MemoryStream(new byte[] { 0x02 }, writable: false))
                {
                    AssertWriteClientNotFound(server.Send(fakeIpPort, ipSyncStream.Length, ipSyncStream));
                }

                using (MemoryStream guidTimeoutStream = new MemoryStream(new byte[] { 0x03 }, writable: false))
                {
                    AssertWriteClientNotFound(server.SendWithTimeout(-1, fakeGuid, guidTimeoutStream.Length, guidTimeoutStream));
                }

                using (MemoryStream ipTimeoutStream = new MemoryStream(new byte[] { 0x04 }, writable: false))
                {
                    AssertWriteClientNotFound(server.SendWithTimeout(-1, fakeIpPort, ipTimeoutStream.Length, ipTimeoutStream));
                }

                using (MemoryStream guidAsyncStream = new MemoryStream(new byte[] { 0x05 }, writable: false))
                {
                    AssertWriteClientNotFound(await server.SendAsync(fakeGuid, guidAsyncStream.Length, guidAsyncStream).ConfigureAwait(false));
                }

                using (MemoryStream ipAsyncStream = new MemoryStream(new byte[] { 0x06 }, writable: false))
                {
                    AssertWriteClientNotFound(await server.SendAsync(fakeIpPort, ipAsyncStream.Length, ipAsyncStream).ConfigureAwait(false));
                }

                using (MemoryStream guidAsyncTimeoutStream = new MemoryStream(new byte[] { 0x07 }, writable: false))
                {
                    AssertWriteClientNotFound(await server.SendWithTimeoutAsync(-1, fakeGuid, guidAsyncTimeoutStream.Length, guidAsyncTimeoutStream).ConfigureAwait(false));
                }

                using (MemoryStream ipAsyncTimeoutStream = new MemoryStream(new byte[] { 0x08 }, writable: false))
                {
                    AssertWriteClientNotFound(await server.SendWithTimeoutAsync(-1, fakeIpPort, ipAsyncTimeoutStream.Length, ipAsyncTimeoutStream).ConfigureAwait(false));
                }

                AssertReadClientNotFound(server.ReadWithTimeout(-1, fakeGuid, 1));
                AssertReadClientNotFound(server.ReadWithTimeout(-1, fakeIpPort, 1));
                AssertReadClientNotFound(await server.ReadWithTimeoutAsync(-1, fakeGuid, 1).ConfigureAwait(false));
                AssertReadClientNotFound(await server.ReadWithTimeoutAsync(-1, fakeIpPort, 1).ConfigureAwait(false));
            }
            finally
            {
                SafeDispose(server);
            }
        }

        public static async Task ServerObsoleteIpPortOverloadsWork()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                string ipPort = pair.ServerClient.IpPort;

                WriteResult send1 = pair.Server.Send(ipPort, "legacy1");
                TestAssert.Equal(WriteResultStatus.Success, send1.Status);
                ReadResult clientRead1 = pair.Client.Read(7);
                TestAssert.Equal("legacy1", Encoding.UTF8.GetString(clientRead1.Data));

                WriteResult send2 = pair.Server.SendWithTimeout(-1, ipPort, Encoding.UTF8.GetBytes("legacy2"));
                TestAssert.Equal(WriteResultStatus.Success, send2.Status);
                ReadResult clientRead2 = pair.Client.Read(7);
                TestAssert.Equal("legacy2", Encoding.UTF8.GetString(clientRead2.Data));

                WriteResult send3 = await pair.Server.SendAsync(ipPort, Encoding.UTF8.GetBytes("legacy3")).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, send3.Status);
                ReadResult clientRead3 = await pair.Client.ReadAsync(7).ConfigureAwait(false);
                TestAssert.Equal("legacy3", Encoding.UTF8.GetString(clientRead3.Data));

                WriteResult send4 = await pair.Server.SendWithTimeoutAsync(-1, ipPort, Encoding.UTF8.GetBytes("legacy4")).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, send4.Status);
                ReadResult clientRead4 = await pair.Client.ReadAsync(7).ConfigureAwait(false);
                TestAssert.Equal("legacy4", Encoding.UTF8.GetString(clientRead4.Data));

                await pair.Client.SendAsync(Encoding.UTF8.GetBytes("srv1")).ConfigureAwait(false);
                ReadResult read1 = pair.Server.Read(ipPort, 4);
                TestAssert.Equal("srv1", Encoding.UTF8.GetString(read1.Data));

                await pair.Client.SendAsync(Encoding.UTF8.GetBytes("srv2")).ConfigureAwait(false);
                ReadResult read2 = pair.Server.ReadWithTimeout(-1, ipPort, 4);
                TestAssert.Equal("srv2", Encoding.UTF8.GetString(read2.Data));

                await pair.Client.SendAsync(Encoding.UTF8.GetBytes("srv3")).ConfigureAwait(false);
                ReadResult read3 = await pair.Server.ReadAsync(ipPort, 4).ConfigureAwait(false);
                TestAssert.Equal("srv3", Encoding.UTF8.GetString(read3.Data));

                await pair.Client.SendAsync(Encoding.UTF8.GetBytes("srv4")).ConfigureAwait(false);
                ReadResult read4 = await pair.Server.ReadWithTimeoutAsync(-1, ipPort, 4).ConfigureAwait(false);
                TestAssert.Equal("srv4", Encoding.UTF8.GetString(read4.Data));

                TestAssert.True(pair.Server.IsConnected(ipPort));
                TestAssert.NotNull(pair.Server.GetStream(ipPort));

                pair.Server.DisconnectClient(ipPort);
                await WaitForClientDisconnectedAsync(pair.Client, timeoutMs: 5000).ConfigureAwait(false);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ServerPermitBlockAndAuthorizationRulesAreApplied()
        {
            var permittedRejection = await RejectConnectionAsync(server =>
            {
                server.Settings.PermittedIPs.Add("192.0.2.55");
            }).ConfigureAwait(false);

            TestAssert.True(!String.IsNullOrEmpty(permittedRejection.DeclinedIpPort));
            TestAssert.Equal(DisconnectReason.ConnectionDeclined, permittedRejection.Reason);

            var blockedRejection = await RejectConnectionAsync(server =>
            {
                server.Settings.BlockedIPs.Add(_Hostname);
            }).ConfigureAwait(false);

            TestAssert.True(!String.IsNullOrEmpty(blockedRejection.DeclinedIpPort));
            TestAssert.Equal(DisconnectReason.ConnectionDeclined, blockedRejection.Reason);

            var callbackRejection = await RejectConnectionAsync(server =>
            {
                server.Callbacks.AuthorizeConnection = (ip, port) => false;
            }).ConfigureAwait(false);

            TestAssert.True(!String.IsNullOrEmpty(callbackRejection.DeclinedIpPort));
            TestAssert.Equal(DisconnectReason.ConnectionDeclined, callbackRejection.Reason);
        }

        public static async Task ServerAuthorizationCallbackAllowsConnection()
        {
            int port = GetNextPort();
            string observedIp = null;
            int observedPort = 0;

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            server.Callbacks.AuthorizeConnection = (ip, clientPort) =>
            {
                observedIp = ip;
                observedPort = clientPort;
                return true;
            };

            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                TestAssert.True(!String.IsNullOrEmpty(observedIp));
                TestAssert.True(observedPort > 0);
                TestAssert.True(IPAddress.TryParse(observedIp, out IPAddress parsedIp) && IPAddress.IsLoopback(parsedIp));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerSendAndClientReadUsingStringAndBytes()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                string text = "hello-client";
                WriteResult textWrite = pair.Server.Send(pair.ServerClient.Guid, text);
                TestAssert.Equal(WriteResultStatus.Success, textWrite.Status);
                TestAssert.Equal((long)text.Length, textWrite.BytesWritten);

                ReadResult textRead = pair.Client.Read(text.Length);
                TestAssert.Equal(ReadResultStatus.Success, textRead.Status);
                TestAssert.Equal(text, Encoding.UTF8.GetString(textRead.Data));

                byte[] bytes = CreatePatternedPayload(32);
                WriteResult byteWrite = await pair.Server.SendAsync(pair.ServerClient.Guid, bytes).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, byteWrite.Status);
                TestAssert.Equal((long)bytes.Length, byteWrite.BytesWritten);

                ReadResult byteRead = await pair.Client.ReadAsync(bytes.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, byteRead.Status);
                AssertPayloadEquals(bytes, byteRead.Data);

                byte[] timeoutBytes = CreatePatternedPayload(29);
                WriteResult timeoutByteWrite = pair.Server.SendWithTimeout(-1, pair.ServerClient.Guid, timeoutBytes);
                TestAssert.Equal(WriteResultStatus.Success, timeoutByteWrite.Status);
                TestAssert.Equal((long)timeoutBytes.Length, timeoutByteWrite.BytesWritten);

                ReadResult timeoutByteRead = pair.Client.ReadWithTimeout(-1, timeoutBytes.Length);
                TestAssert.Equal(ReadResultStatus.Success, timeoutByteRead.Status);
                AssertPayloadEquals(timeoutBytes, timeoutByteRead.Data);

                byte[] asyncTimeoutBytes = CreatePatternedPayload(31);
                WriteResult asyncTimeoutByteWrite = await pair.Server.SendWithTimeoutAsync(-1, pair.ServerClient.Guid, asyncTimeoutBytes).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, asyncTimeoutByteWrite.Status);
                TestAssert.Equal((long)asyncTimeoutBytes.Length, asyncTimeoutByteWrite.BytesWritten);

                ReadResult asyncTimeoutByteRead = await pair.Client.ReadWithTimeoutAsync(-1, asyncTimeoutBytes.Length).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, asyncTimeoutByteRead.Status);
                AssertPayloadEquals(asyncTimeoutBytes, asyncTimeoutByteRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ServerDisposeDisconnectsClients()
        {
            int port = GetNextPort();
            using ManualResetEventSlim clientDisconnectedEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
            client.Settings.PollIntervalMicroSeconds = 50000;
            client.Events.ClientDisconnected += (s, e) => clientDisconnectedEvent.Set();

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                server.Dispose();
                WaitForSignal(clientDisconnectedEvent, timeoutMs: 5000, failureMessage: "Client disconnect event did not fire when the server was disposed.");
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000).ConfigureAwait(false);
                TestAssert.False(server.IsListening);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerRestartAfterStopAcceptsNewConnections()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);
                server.Stop();
                await WaitForServerStoppedAsync(server, timeoutMs: 5000).ConfigureAwait(false);

                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);
                TestAssert.Single(server.GetClients());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerCanStartAndStopRepeatedly()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    server.Start();
                    await WaitForServerListeningAsync(server).ConfigureAwait(false);
                    server.Stop();
                    await WaitForServerStoppedAsync(server, timeoutMs: 5000).ConfigureAwait(false);
                }

                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
                try
                {
                    client.Connect(5);
                    await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);
                }
                finally
                {
                    SafeDispose(client);
                }
            }
            finally
            {
                SafeDispose(server);
            }
        }

        public static async Task ServerStopStopsAcceptingNewConnections()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);
                server.Stop();
                await WaitForServerStoppedAsync(server, timeoutMs: 5000).ConfigureAwait(false);

                Exception connectException = TestAssert.ThrowsAny<Exception>(() => client.Connect(1));
                TestAssert.NotNull(connectException);
                TestAssert.False(server.IsListening);
                TestAssert.False(client.IsConnected);
                TestAssert.Equal(0, server.GetClients().Count());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static Task ServerStartAndStartAsyncRejectDuplicateStart()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);

            try
            {
                server.Start();
                WaitForServerListeningAsync(server).GetAwaiter().GetResult();

                TestAssert.Throws<InvalidOperationException>(() => server.Start());
                TestAssert.Throws<InvalidOperationException>(() => server.StartAsync());

                server.Stop();
                WaitForServerStoppedAsync(server, timeoutMs: 5000).GetAwaiter().GetResult();

                server.Start();
                WaitForServerListeningAsync(server).GetAwaiter().GetResult();
            }
            finally
            {
                SafeDispose(server);
            }

            return Task.CompletedTask;
        }

        public static Task SettingsAndKeepaliveDefaultsAreExpected()
        {
            CavemanTcpClientSettings clientSettings = new CavemanTcpClientSettings();
            TestAssert.Equal(65536, clientSettings.StreamBufferSize);
            TestAssert.True(clientSettings.AcceptInvalidCertificates);
            TestAssert.False(clientSettings.CheckCertificateRevocation);
            TestAssert.False(clientSettings.MutuallyAuthenticate);
            TestAssert.True(clientSettings.EnableConnectionMonitor);
            TestAssert.Equal(250000, clientSettings.PollIntervalMicroSeconds);

            CavemanTcpServerSettings serverSettings = new CavemanTcpServerSettings();
            TestAssert.Equal(65536, serverSettings.StreamBufferSize);
            TestAssert.True(serverSettings.MonitorClientConnections);
            TestAssert.True(serverSettings.AcceptInvalidCertificates);
            TestAssert.False(serverSettings.MutuallyAuthenticate);
            TestAssert.Equal(4096, serverSettings.MaxConnections);
            TestAssert.NotNull(serverSettings.PermittedIPs);
            TestAssert.NotNull(serverSettings.BlockedIPs);
            TestAssert.Equal(0, serverSettings.PermittedIPs.Count);
            TestAssert.Equal(0, serverSettings.BlockedIPs.Count);

            CavemanTcpKeepaliveSettings keepaliveSettings = new CavemanTcpKeepaliveSettings();
            TestAssert.False(keepaliveSettings.EnableTcpKeepAlives);
            TestAssert.Equal(2, keepaliveSettings.TcpKeepAliveInterval);
            TestAssert.Equal(2, keepaliveSettings.TcpKeepAliveTime);
            TestAssert.Equal(3, keepaliveSettings.TcpKeepAliveRetryCount);

            return Task.CompletedTask;
        }

        public static async Task StringOverloadsRejectNullOrEmptyPayloadsWhenConnected()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                TestAssert.Throws<ArgumentNullException>(() => pair.Client.Send((string)null));
                TestAssert.Throws<ArgumentNullException>(() => pair.Client.Send(String.Empty));
                TestAssert.Throws<ArgumentException>(() => pair.Client.SendWithTimeout(-1, (string)null));
                TestAssert.Throws<ArgumentException>(() => pair.Client.SendWithTimeout(-1, String.Empty));
                await TestAssert.ThrowsAsync<ArgumentException>(() => pair.Client.SendAsync((string)null)).ConfigureAwait(false);
                await TestAssert.ThrowsAsync<ArgumentException>(() => pair.Client.SendAsync(String.Empty)).ConfigureAwait(false);
                await TestAssert.ThrowsAsync<ArgumentException>(() => pair.Client.SendWithTimeoutAsync(-1, (string)null)).ConfigureAwait(false);
                await TestAssert.ThrowsAsync<ArgumentException>(() => pair.Client.SendWithTimeoutAsync(-1, String.Empty)).ConfigureAwait(false);

                TestAssert.Throws<ArgumentException>(() => pair.Server.Send(pair.ServerClient.Guid, (string)null));
                TestAssert.Throws<ArgumentException>(() => pair.Server.Send(pair.ServerClient.Guid, String.Empty));
                TestAssert.Throws<ArgumentException>(() => pair.Server.SendWithTimeout(-1, pair.ServerClient.Guid, (string)null));
                TestAssert.Throws<ArgumentException>(() => pair.Server.SendWithTimeout(-1, pair.ServerClient.Guid, String.Empty));
                await TestAssert.ThrowsAsync<ArgumentException>(() => pair.Server.SendAsync(pair.ServerClient.Guid, (string)null)).ConfigureAwait(false);
                await TestAssert.ThrowsAsync<ArgumentException>(() => pair.Server.SendAsync(pair.ServerClient.Guid, String.Empty)).ConfigureAwait(false);
                await TestAssert.ThrowsAsync<ArgumentException>(() => pair.Server.SendWithTimeoutAsync(-1, pair.ServerClient.Guid, (string)null)).ConfigureAwait(false);
                await TestAssert.ThrowsAsync<ArgumentException>(() => pair.Server.SendWithTimeoutAsync(-1, pair.ServerClient.Guid, String.Empty)).ConfigureAwait(false);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task WildcardListenerConstructorsAcceptConnections()
        {
            foreach (string listenerIp in new[] { "*", "+" })
            {
                int port = GetNextPort();
                CavemanTcpServer server = new CavemanTcpServer(listenerIp, port, false, null, null);
                CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

                try
                {
                    server.Start();
                    await WaitForServerListeningAsync(server).ConfigureAwait(false);

                    client.Connect(5);
                    await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

                    WriteResult write = client.Send("wildcard");
                    TestAssert.Equal(WriteResultStatus.Success, write.Status);
                    ReadResult read = server.Read(GetSingleClient(server).Guid, 8);
                    TestAssert.Equal(ReadResultStatus.Success, read.Status);
                    TestAssert.Equal("wildcard", Encoding.UTF8.GetString(read.Data));
                }
                finally
                {
                    SafeDispose(client);
                    SafeDispose(server);
                }
            }
        }

        public static Task SslValidationFailureRaisesClientExceptionEvent()
        {
            X509Certificate2 certificate = LoadTestCertificate();
            int port = GetNextPort();
            Exception capturedException = null;
            using ManualResetEventSlim exceptionEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, certificate);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, true);
            client.Settings.AcceptInvalidCertificates = false;
            client.Events.ExceptionEncountered += (s, e) =>
            {
                capturedException = e.Exception;
                exceptionEvent.Set();
            };

            try
            {
                server.Start();
                WaitForServerListeningAsync(server, timeoutMs: 5000).GetAwaiter().GetResult();

                Exception connectException = TestAssert.ThrowsAny<Exception>(() => client.Connect(5));
                WaitForSignal(exceptionEvent, timeoutMs: 5000, failureMessage: "SSL validation failure did not raise a client exception event.");

                TestAssert.NotNull(connectException);
                TestAssert.NotNull(capturedException);
                TestAssert.Equal(connectException.GetType(), capturedException.GetType());
                TestAssert.False(client.IsConnected);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                certificate.Dispose();
            }

            return Task.CompletedTask;
        }

        public static async Task SslRoundTripWithCertificateObject()
        {
            X509Certificate2 certificate = LoadTestCertificate();
            int port = GetNextPort();

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, certificate);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, certificate);
            client.Settings.AcceptInvalidCertificates = true;

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server, timeoutMs: 5000).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000).ConfigureAwait(false);
                ClientMetadata serverClient = GetSingleClient(server);

                WriteResult clientWrite = client.Send("ssl-object");
                TestAssert.Equal(WriteResultStatus.Success, clientWrite.Status);
                ReadResult serverRead = server.Read(serverClient.Guid, 10);
                TestAssert.Equal("ssl-object", Encoding.UTF8.GetString(serverRead.Data));

                WriteResult serverWrite = await server.SendAsync(serverClient.Guid, Encoding.UTF8.GetBytes("ssl-reply")).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, serverWrite.Status);
                ReadResult clientRead = await client.ReadAsync(9).ConfigureAwait(false);
                TestAssert.Equal("ssl-reply", Encoding.UTF8.GetString(clientRead.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                certificate.Dispose();
            }
        }

        public static async Task SslRoundTripWithPfxConstructors()
        {
            int port = GetNextPort();
            string certificatePath = GetCertificatePath();

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, true, certificatePath, CertificatePassword);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, true, certificatePath, CertificatePassword);
            client.Settings.AcceptInvalidCertificates = true;

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server, timeoutMs: 5000).ConfigureAwait(false);

                client.Connect(5);
                await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000).ConfigureAwait(false);
                ClientMetadata serverClient = GetSingleClient(server);

                WriteResult clientWrite = await client.SendAsync(Encoding.UTF8.GetBytes("ssl-pfx")).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, clientWrite.Status);
                ReadResult serverRead = await server.ReadAsync(serverClient.Guid, 7).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, serverRead.Status);
                TestAssert.Equal("ssl-pfx", Encoding.UTF8.GetString(serverRead.Data));

                WriteResult serverWrite = await server.SendAsync(serverClient.Guid, Encoding.UTF8.GetBytes("ssl-ok")).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, serverWrite.Status);
                ReadResult clientRead = await client.ReadAsync(6).ConfigureAwait(false);
                TestAssert.Equal(ReadResultStatus.Success, clientRead.Status);
                TestAssert.Equal("ssl-ok", Encoding.UTF8.GetString(clientRead.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task StartAsyncHonorsCancellation()
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            using CancellationTokenSource cts = new CancellationTokenSource();

            try
            {
                Task serverTask = server.StartAsync(cts.Token);
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                cts.Cancel();
                await serverTask.ConfigureAwait(false);
                await WaitForServerStoppedAsync(server, timeoutMs: 5000).ConfigureAwait(false);
                TestAssert.False(server.IsListening);
            }
            finally
            {
                SafeDispose(server);
            }
        }

        public static async Task StreamSendAndReceive()
        {
            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                byte[] fromClient = CreatePatternedPayload(4096);
                using MemoryStream clientStream = new MemoryStream(fromClient, writable: false);
                WriteResult clientWrite = pair.Client.Send(clientStream.Length, clientStream);
                TestAssert.Equal(WriteResultStatus.Success, clientWrite.Status);
                ReadResult serverRead = await pair.Server.ReadAsync(pair.ServerClient.Guid, fromClient.Length).ConfigureAwait(false);
                AssertPayloadEquals(fromClient, serverRead.Data);

                byte[] fromServer = CreatePatternedPayload(4096);
                using MemoryStream serverStream = new MemoryStream(fromServer, writable: false);
                WriteResult serverWrite = await pair.Server.SendAsync(pair.ServerClient.Guid, serverStream.Length, serverStream).ConfigureAwait(false);
                TestAssert.Equal(WriteResultStatus.Success, serverWrite.Status);
                ReadResult clientRead = await pair.Client.ReadAsync(fromServer.Length).ConfigureAwait(false);
                AssertPayloadEquals(fromServer, clientRead.Data);
            }
            finally
            {
                CleanupPair(pair);
            }
        }

        public static async Task ValidationGuardsCoverConstructorsSettingsAndStreams()
        {
            TestAssert.Throws<ArgumentNullException>(() => new CavemanTcpClient((string)null));
            TestAssert.Throws<ArgumentException>(() => new CavemanTcpClient(_Hostname, -1, false));
            TestAssert.Throws<ArgumentNullException>(() => new CavemanTcpServer((string)null));
            TestAssert.Throws<ArgumentException>(() => new CavemanTcpServer(_Hostname, -1, false, null, null));

            CavemanTcpClient lifecycleClient = new CavemanTcpClient(_Hostname, GetNextPort(), false, null, null);
            TestAssert.Throws<ArgumentException>(() => lifecycleClient.Connect(0));
            TestAssert.ThrowsAny<IOException>(() => lifecycleClient.GetStream());

            CavemanTcpServer lifecycleServer = new CavemanTcpServer(_Hostname, GetNextPort(), false, null, null);
            TestAssert.Throws<InvalidOperationException>(() => lifecycleServer.Stop());

            CavemanTcpClientSettings clientSettings = new CavemanTcpClientSettings();
            TestAssert.Throws<ArgumentException>(() => clientSettings.StreamBufferSize = 0);
            TestAssert.Throws<ArgumentException>(() => clientSettings.PollIntervalMicroSeconds = 0);
            clientSettings.PollIntervalMicroSeconds = -1;
            clientSettings.PollIntervalMicroSeconds = 50000;

            CavemanTcpServerSettings serverSettings = new CavemanTcpServerSettings();
            TestAssert.Throws<ArgumentException>(() => serverSettings.StreamBufferSize = 0);
            TestAssert.Throws<ArgumentException>(() => serverSettings.MaxConnections = 0);
            serverSettings.PermittedIPs = null;
            serverSettings.BlockedIPs = null;
            TestAssert.NotNull(serverSettings.PermittedIPs);
            TestAssert.NotNull(serverSettings.BlockedIPs);

            CavemanTcpKeepaliveSettings keepaliveSettings = new CavemanTcpKeepaliveSettings();
            TestAssert.Throws<ArgumentException>(() => keepaliveSettings.TcpKeepAliveInterval = 0);
            TestAssert.Throws<ArgumentException>(() => keepaliveSettings.TcpKeepAliveTime = 0);
            TestAssert.Throws<ArgumentException>(() => keepaliveSettings.TcpKeepAliveRetryCount = 0);

            lifecycleClient.Settings = null;
            lifecycleClient.Events = null;
            lifecycleClient.Keepalive = null;
            TestAssert.NotNull(lifecycleClient.Settings);
            TestAssert.NotNull(lifecycleClient.Events);
            TestAssert.NotNull(lifecycleClient.Keepalive);

            lifecycleServer.Settings = null;
            lifecycleServer.Events = null;
            lifecycleServer.Keepalive = null;
            lifecycleServer.Callbacks = null;
            TestAssert.NotNull(lifecycleServer.Settings);
            TestAssert.NotNull(lifecycleServer.Events);
            TestAssert.NotNull(lifecycleServer.Keepalive);
            TestAssert.NotNull(lifecycleServer.Callbacks);

            var pair = await CreateConnectedPairAsync().ConfigureAwait(false);

            try
            {
                TestAssert.Throws<ArgumentNullException>(() => pair.Client.Send((byte[])null));
                TestAssert.Throws<ArgumentException>(() => pair.Client.SendWithTimeout(0, new byte[] { 0x01 }));
                TestAssert.Throws<ArgumentException>(() => pair.Client.Read(0));
                TestAssert.Throws<ArgumentException>(() => pair.Client.ReadWithTimeout(0, 1));
                TestAssert.Throws<ArgumentException>(() => pair.Client.Send(0, new MemoryStream()));
                TestAssert.Throws<ArgumentNullException>(() => pair.Client.Send(1, (Stream)null));
                TestAssert.Throws<InvalidOperationException>(() => pair.Client.Send(1, new NonReadableStream()));
                TestAssert.Throws<ArgumentException>(() => pair.Client.SendWithTimeoutAsync(0, new byte[] { 0x01 }).GetAwaiter().GetResult());
                TestAssert.Throws<ArgumentException>(() => pair.Client.ReadWithTimeoutAsync(0, 1).GetAwaiter().GetResult());

                TestAssert.Throws<ArgumentException>(() => pair.Server.Send(pair.ServerClient.Guid, Array.Empty<byte>()));
                TestAssert.Throws<ArgumentException>(() => pair.Server.SendWithTimeout(0, pair.ServerClient.Guid, new byte[] { 0x01 }));
                TestAssert.Throws<ArgumentException>(() => pair.Server.Read(pair.ServerClient.Guid, 0));
                TestAssert.Throws<ArgumentException>(() => pair.Server.ReadWithTimeout(0, pair.ServerClient.Guid, 1));
                TestAssert.Throws<ArgumentException>(() => pair.Server.Send(pair.ServerClient.Guid, 0, new MemoryStream()));
                TestAssert.Throws<ArgumentNullException>(() => pair.Server.Send(pair.ServerClient.Guid, 1, (Stream)null));
                TestAssert.Throws<InvalidOperationException>(() => pair.Server.Send(pair.ServerClient.Guid, 1, new NonReadableStream()));
                TestAssert.Throws<ArgumentException>(() => pair.Server.SendWithTimeoutAsync(0, pair.ServerClient.Guid, new byte[] { 0x01 }).GetAwaiter().GetResult());
                TestAssert.Throws<ArgumentException>(() => pair.Server.ReadWithTimeoutAsync(0, pair.ServerClient.Guid, 1).GetAwaiter().GetResult());
            }
            finally
            {
                CleanupPair(pair);
                SafeDispose(lifecycleClient);
                SafeDispose(lifecycleServer);
            }
        }

        private static void AssertPayloadEquals(byte[] expected, byte[] actual)
        {
            TestAssert.NotNull(actual);
            TestAssert.Equal(expected.Length, actual.Length, "Payload length mismatch.");

            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    throw new InvalidOperationException("Payload mismatch at byte " + i + ".");
                }
            }
        }

        private static void AssertByteSetEquals(IEnumerable<byte> expected, byte[] actual)
        {
            TestAssert.NotNull(expected);
            TestAssert.NotNull(actual);

            byte[] expectedBytes = expected.OrderBy(b => b).ToArray();
            byte[] actualBytes = actual.OrderBy(b => b).ToArray();
            AssertPayloadEquals(expectedBytes, actualBytes);
        }

        private static void CleanupPair((CavemanTcpServer Server, CavemanTcpClient Client, ClientMetadata ServerClient) pair)
        {
            SafeDispose(pair.Client);
            SafeDispose(pair.Server);
        }

        private static async Task<(CavemanTcpServer Server, CavemanTcpClient Client, ClientMetadata ServerClient)> CreateConnectedPairAsync(Action<CavemanTcpServer> configureServer = null, Action<CavemanTcpClient> configureClient = null)
        {
            int port = GetNextPort();
            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);

            configureServer?.Invoke(server);
            configureClient?.Invoke(client);

            server.Start();
            await WaitForServerListeningAsync(server).ConfigureAwait(false);

            client.Connect(5);
            await WaitForClientConnectedAsync(client, server, 1).ConfigureAwait(false);

            return (server, client, GetSingleClient(server));
        }

        private static ConcurrentQueue<string> CreateLogCapture(out Action<string> logger)
        {
            ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
            logger = message =>
            {
                messages.Enqueue(message);
            };
            return messages;
        }

        private static byte[] CreatePatternedPayload(int size)
        {
            if (size < 0) throw new ArgumentException("Size must be zero or greater.", nameof(size));

            byte[] data = new byte[size];
            for (int i = 0; i < size; i++) data[i] = (byte)(i % 251);
            return data;
        }

        private static string GetCertificatePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "cavemantcp.pfx");
        }

        private static int GetNextPort()
        {
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static ClientMetadata GetSingleClient(CavemanTcpServer server)
        {
            List<ClientMetadata> clients = server.GetClients().ToList();
            TestAssert.Single(clients);
            return clients[0];
        }

        private static X509Certificate2 LoadTestCertificate()
        {
            string certificatePath = GetCertificatePath();

#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12FromFile(certificatePath, CertificatePassword);
#else
#pragma warning disable SYSLIB0057
            return new X509Certificate2(certificatePath, CertificatePassword);
#pragma warning restore SYSLIB0057
#endif
        }

        private static bool LogContains(IEnumerable<string> messages, string fragment)
        {
            if (messages == null) return false;
            return messages.Any(msg => msg != null && msg.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static async Task<(string DeclinedIpPort, DisconnectReason Reason)> RejectConnectionAsync(Action<CavemanTcpServer> configureServer)
        {
            int port = GetNextPort();
            string declinedIpPort = null;
            DisconnectReason declinedReason = DisconnectReason.Normal;
            using ManualResetEventSlim declinedEvent = new ManualResetEventSlim(false);

            CavemanTcpServer server = new CavemanTcpServer(_Hostname, port, false, null, null);
            configureServer?.Invoke(server);
            server.Events.ClientDeclined += (s, e) =>
            {
                declinedIpPort = e.IpPort;
                declinedReason = e.Reason;
                declinedEvent.Set();
            };

            CavemanTcpClient client = new CavemanTcpClient(_Hostname, port, false, null, null);
            client.Settings.PollIntervalMicroSeconds = 50000;

            try
            {
                server.Start();
                await WaitForServerListeningAsync(server).ConfigureAwait(false);

                TryConnectAndIgnoreFailure(client);
                WaitForSignal(declinedEvent, timeoutMs: 5000, failureMessage: "Expected connection decline event was not raised.");
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000).ConfigureAwait(false);
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000).ConfigureAwait(false);

                return (declinedIpPort, declinedReason);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        private static void SafeDispose(IDisposable obj)
        {
            try { obj?.Dispose(); }
            catch (AggregateException) { }
            catch (ObjectDisposedException) { }
        }

        private static void TryConnectAndIgnoreFailure(CavemanTcpClient client)
        {
            try
            {
                client.Connect(3);
            }
            catch
            {
            }
        }

        private static async Task WaitForClientConnectedAsync(CavemanTcpClient client, CavemanTcpServer server = null, int expectedServerClients = 0, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() =>
            {
                if (!client.IsConnected) return false;
                if (server == null) return true;
                return server.GetClients().Count() >= expectedServerClients;
            }, timeoutMs, "Client did not reach the expected connected state in time.").ConfigureAwait(false);
        }

        private static async Task WaitForClientDisconnectedAsync(CavemanTcpClient client, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => !client.IsConnected, timeoutMs, "Client did not disconnect in time.").ConfigureAwait(false);
        }

        private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = DefaultConditionTimeoutMs, string failureMessage = null)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            DateTime timeout = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < timeout)
            {
                if (condition()) return;
                await Task.Delay(PollIntervalMs).ConfigureAwait(false);
            }

            TestAssert.True(condition(), failureMessage ?? "Condition was not satisfied before the timeout expired.");
        }

        private static void WaitForSignal(ManualResetEventSlim handle, int timeoutMs = DefaultEventTimeoutMs, string failureMessage = null)
        {
            TestAssert.True(handle.Wait(timeoutMs), failureMessage ?? "Expected event was not raised before the timeout expired.");
        }

        private static async Task WaitForServerClientCountAsync(CavemanTcpServer server, int expectedClientCount, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => server.GetClients().Count() == expectedClientCount, timeoutMs, "Server did not reach the expected client count in time.").ConfigureAwait(false);
        }

        private static async Task WaitForServerListeningAsync(CavemanTcpServer server, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => server.IsListening, timeoutMs, "Server did not start listening in time.").ConfigureAwait(false);
        }

        private static async Task WaitForServerStoppedAsync(CavemanTcpServer server, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => !server.IsListening, timeoutMs, "Server did not stop listening in time.").ConfigureAwait(false);
        }

        private sealed class BlockingContentStream : Stream
        {
            private readonly byte[] _data;
            private readonly ManualResetEventSlim _entered = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);
            private int _position;

            internal BlockingContentStream(byte[] data)
            {
                _data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _data.Length;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _entered.Set();
                _release.Wait();

                if (_position >= _data.Length) return 0;

                int bytesToCopy = Math.Min(count, _data.Length - _position);
                Buffer.BlockCopy(_data, _position, buffer, offset, bytesToCopy);
                _position += bytesToCopy;
                return bytesToCopy;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            internal void Release()
            {
                _release.Set();
            }

            internal void WaitForReadStart(int timeoutMs = DefaultConditionTimeoutMs)
            {
                TestAssert.True(_entered.Wait(timeoutMs), "Blocking content stream was never read.");
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _release.Set();
                    _entered.Dispose();
                    _release.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        private sealed class CancelableBlockingContentStream : Stream
        {
            private readonly byte[] _data;
            private readonly ManualResetEventSlim _entered = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);
            private int _position;

            internal CancelableBlockingContentStream(byte[] data)
            {
                _data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _data.Length;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _entered.Set();
                _release.Wait();
                return CopyData(buffer, offset, count);
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                _entered.Set();

                while (!_release.IsSet)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return CopyData(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            internal void WaitForReadStart(int timeoutMs = DefaultConditionTimeoutMs)
            {
                TestAssert.True(_entered.Wait(timeoutMs), "Cancelable blocking content stream was never read.");
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _release.Set();
                    _entered.Dispose();
                    _release.Dispose();
                }

                base.Dispose(disposing);
            }

            private int CopyData(byte[] buffer, int offset, int count)
            {
                if (_position >= _data.Length) return 0;

                int bytesToCopy = Math.Min(count, _data.Length - _position);
                Buffer.BlockCopy(_data, _position, buffer, offset, bytesToCopy);
                _position += bytesToCopy;
                return bytesToCopy;
            }
        }

        private sealed class NonReadableStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => 0;

            public override long Position
            {
                get => 0;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
