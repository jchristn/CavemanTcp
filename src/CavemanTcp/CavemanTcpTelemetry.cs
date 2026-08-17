namespace CavemanTcp
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Telemetry source names and metric instrument names emitted by CavemanTcp.
    /// Subscribe to <see cref="MeterName"/> for metrics and <see cref="ActivitySourceName"/> for traces.
    /// </summary>
    public static class CavemanTcpTelemetry
    {
        /// <summary>
        /// Stable meter name used by CavemanTcp metrics.
        /// </summary>
        public const string MeterName = "CavemanTcp";

        /// <summary>
        /// Stable activity source name used by CavemanTcp traces.
        /// </summary>
        public const string ActivitySourceName = "CavemanTcp";

        /// <summary>
        /// Count of completed CavemanTcp operations.
        /// </summary>
        public const string OperationsMetricName = "cavemantcp.operations";

        /// <summary>
        /// Duration of CavemanTcp operations, in seconds.
        /// </summary>
        public const string OperationDurationMetricName = "cavemantcp.operation.duration";

        /// <summary>
        /// Number of bytes sent through CavemanTcp.
        /// </summary>
        public const string BytesSentMetricName = "cavemantcp.bytes.sent";

        /// <summary>
        /// Number of bytes received through CavemanTcp.
        /// </summary>
        public const string BytesReceivedMetricName = "cavemantcp.bytes.received";

        /// <summary>
        /// Number of accepted or established CavemanTcp connections.
        /// </summary>
        public const string ConnectionsOpenedMetricName = "cavemantcp.connections.opened";

        /// <summary>
        /// Number of closed CavemanTcp connections.
        /// </summary>
        public const string ConnectionsClosedMetricName = "cavemantcp.connections.closed";

        /// <summary>
        /// Number of currently active CavemanTcp connections.
        /// </summary>
        public const string ActiveConnectionsMetricName = "cavemantcp.connections.active";

        /// <summary>
        /// Number of declined CavemanTcp server connections.
        /// </summary>
        public const string ConnectionsDeclinedMetricName = "cavemantcp.connections.declined";

        /// <summary>
        /// Number of exceptions observed by CavemanTcp operations.
        /// </summary>
        public const string ExceptionsMetricName = "cavemantcp.exceptions";
    }

    internal static class CavemanTcpInstrumentation
    {
        internal const string RoleClient = "client";
        internal const string RoleServer = "server";

        internal const string OperationConnect = "connect";
        internal const string OperationDisconnect = "disconnect";
        internal const string OperationDispose = "dispose";
        internal const string OperationStart = "start";
        internal const string OperationStartAsync = "start_async";
        internal const string OperationStop = "stop";
        internal const string OperationGetClients = "get_clients";
        internal const string OperationSend = "send";
        internal const string OperationRead = "read";
        internal const string OperationGetStream = "get_stream";
        internal const string OperationIsConnected = "is_connected";
        internal const string OperationAccept = "accept";
        internal const string OperationTls = "tls";
        internal const string OperationAuthorizeConnection = "authorize_connection";

        internal const string StatusSuccess = "success";
        internal const string StatusError = "error";
        internal const string StatusTimeout = "timeout";
        internal const string StatusCanceled = "canceled";
        internal const string StatusDisconnected = "disconnected";
        internal const string StatusClientNotFound = "client_not_found";
        internal const string StatusAlreadyConnected = "already_connected";
        internal const string StatusNotConnected = "not_connected";
        internal const string StatusDeclined = "declined";
        internal const string StatusBlocked = "blocked";
        internal const string StatusNotPermitted = "not_permitted";
        internal const string StatusAuthorizationRejected = "authorization_rejected";

        private const string TransportTcp = "tcp";
        private const string TransportTls = "tls";

        private const string AttributeRole = "cavemantcp.role";
        private const string AttributeOperation = "cavemantcp.operation";
        private const string AttributeStatus = "cavemantcp.status";
        private const string AttributeTransport = "network.transport";
        private const string AttributeTimeoutMs = "cavemantcp.timeout_ms";
        private const string AttributeBytes = "cavemantcp.bytes";
        private const string AttributeClientId = "cavemantcp.client.id";
        private const string AttributeExceptionType = "exception.type";
        private const string AttributeExceptionMessage = "exception.message";
        private const string AttributeServerAddress = "server.address";
        private const string AttributeServerPort = "server.port";
        private const string AttributeClientAddress = "client.address";

        private static readonly Meter _Meter = new Meter(CavemanTcpTelemetry.MeterName);
        internal static readonly ActivitySource ActivitySource = new ActivitySource(CavemanTcpTelemetry.ActivitySourceName);

        private static readonly Counter<long> _Operations =
            _Meter.CreateCounter<long>(CavemanTcpTelemetry.OperationsMetricName, unit: "{operation}", description: "Completed CavemanTcp operations.");

        private static readonly Histogram<double> _OperationDuration =
            _Meter.CreateHistogram<double>(CavemanTcpTelemetry.OperationDurationMetricName, unit: "s", description: "Duration of CavemanTcp operations.");

        private static readonly Counter<long> _BytesSent =
            _Meter.CreateCounter<long>(CavemanTcpTelemetry.BytesSentMetricName, unit: "By", description: "Bytes sent through CavemanTcp.");

        private static readonly Counter<long> _BytesReceived =
            _Meter.CreateCounter<long>(CavemanTcpTelemetry.BytesReceivedMetricName, unit: "By", description: "Bytes received through CavemanTcp.");

        private static readonly Counter<long> _ConnectionsOpened =
            _Meter.CreateCounter<long>(CavemanTcpTelemetry.ConnectionsOpenedMetricName, unit: "{connection}", description: "Connections established or accepted by CavemanTcp.");

        private static readonly Counter<long> _ConnectionsClosed =
            _Meter.CreateCounter<long>(CavemanTcpTelemetry.ConnectionsClosedMetricName, unit: "{connection}", description: "Connections closed by CavemanTcp.");

        private static readonly UpDownCounter<long> _ActiveConnections =
            _Meter.CreateUpDownCounter<long>(CavemanTcpTelemetry.ActiveConnectionsMetricName, unit: "{connection}", description: "Currently active CavemanTcp connections.");

        private static readonly Counter<long> _ConnectionsDeclined =
            _Meter.CreateCounter<long>(CavemanTcpTelemetry.ConnectionsDeclinedMetricName, unit: "{connection}", description: "Server connections declined by CavemanTcp.");

        private static readonly Counter<long> _Exceptions =
            _Meter.CreateCounter<long>(CavemanTcpTelemetry.ExceptionsMetricName, unit: "{exception}", description: "Exceptions observed by CavemanTcp operations.");

        internal static long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        internal static Activity StartActivity(string role, string operation, bool ssl, ActivityKind kind)
        {
            Activity activity = ActivitySource.StartActivity("cavemantcp." + role + "." + operation, kind);
            if (activity != null)
            {
                activity.SetTag(AttributeRole, role);
                activity.SetTag(AttributeOperation, operation);
                activity.SetTag(AttributeTransport, GetTransport(ssl));
            }

            return activity;
        }

        internal static void SetEndpoint(Activity activity, string serverAddress, int serverPort)
        {
            if (activity == null) return;
            if (!String.IsNullOrWhiteSpace(serverAddress)) activity.SetTag(AttributeServerAddress, serverAddress);
            if (serverPort >= 0) activity.SetTag(AttributeServerPort, serverPort);
        }

        internal static void SetClient(Activity activity, ClientMetadata client)
        {
            if (activity == null || client == null) return;
            activity.SetTag(AttributeClientId, client.Guid.ToString());
            if (!String.IsNullOrWhiteSpace(client.IpPort)) activity.SetTag(AttributeClientAddress, client.IpPort);
        }

        internal static void SetTimeout(Activity activity, int timeoutMs)
        {
            if (activity == null || timeoutMs < 0) return;
            activity.SetTag(AttributeTimeoutMs, timeoutMs);
        }

        internal static void RecordOperation(string role, string operation, string status, long startTimestamp)
        {
            double seconds = GetElapsedSeconds(startTimestamp);
            TagList tags = CreateOperationTags(role, operation, status);
            _Operations.Add(1, tags);
            _OperationDuration.Record(seconds, tags);
        }

        internal static void RecordWrite(string role, string status, long bytesWritten, long startTimestamp)
        {
            RecordOperation(role, OperationSend, status, startTimestamp);
            if (bytesWritten > 0)
            {
                TagList tags = CreateStatusTags(role, status);
                _BytesSent.Add(bytesWritten, tags);
            }
        }

        internal static void RecordRead(string role, string status, long bytesRead, long startTimestamp)
        {
            RecordOperation(role, OperationRead, status, startTimestamp);
            if (bytesRead > 0)
            {
                TagList tags = CreateStatusTags(role, status);
                _BytesReceived.Add(bytesRead, tags);
            }
        }

        internal static void RecordConnectionOpened(string role, bool ssl)
        {
            TagList tags = CreateConnectionTags(role, ssl);
            _ConnectionsOpened.Add(1, tags);
            _ActiveConnections.Add(1, tags);
        }

        internal static void RecordConnectionClosed(string role, bool ssl, DisconnectReason reason)
        {
            TagList closeTags = CreateConnectionTags(role, ssl);
            closeTags.Add("reason", ToReason(reason));
            _ConnectionsClosed.Add(1, closeTags);

            TagList activeTags = CreateConnectionTags(role, ssl);
            _ActiveConnections.Add(-1, activeTags);
        }

        internal static void RecordConnectionDeclined(string status)
        {
            TagList tags = new TagList();
            tags.Add(AttributeRole, RoleServer);
            tags.Add(AttributeStatus, status);
            _ConnectionsDeclined.Add(1, tags);
        }

        internal static void RecordException(string role, string operation, Exception exception, Activity activity)
        {
            if (exception == null) return;

            string exceptionType = exception.GetType().FullName ?? exception.GetType().Name;

            TagList tags = new TagList();
            tags.Add(AttributeRole, role);
            tags.Add(AttributeOperation, operation);
            tags.Add(AttributeExceptionType, exceptionType);
            _Exceptions.Add(1, tags);

            if (activity != null)
            {
                activity.SetTag(AttributeExceptionType, exceptionType);
                activity.SetTag(AttributeExceptionMessage, exception.Message);
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            }
        }

        internal static void CompleteActivity(Activity activity, string status)
        {
            CompleteActivity(activity, status, 0);
        }

        internal static void CompleteActivity(Activity activity, string status, long bytes)
        {
            if (activity == null) return;

            activity.SetTag(AttributeStatus, status);
            if (bytes > 0) activity.SetTag(AttributeBytes, bytes);

            if (String.Equals(status, StatusSuccess, StringComparison.Ordinal)
                || String.Equals(status, StatusAlreadyConnected, StringComparison.Ordinal))
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else if (!String.Equals(status, StatusNotConnected, StringComparison.Ordinal))
            {
                activity.SetStatus(ActivityStatusCode.Error, status);
            }
        }

        internal static string ToStatus(WriteResultStatus status)
        {
            switch (status)
            {
                case WriteResultStatus.Success:
                    return StatusSuccess;
                case WriteResultStatus.Timeout:
                    return StatusTimeout;
                case WriteResultStatus.Disconnected:
                    return StatusDisconnected;
                case WriteResultStatus.Canceled:
                    return StatusCanceled;
                case WriteResultStatus.ClientNotFound:
                    return StatusClientNotFound;
                default:
                    return StatusError;
            }
        }

        internal static string ToStatus(ReadResultStatus status)
        {
            switch (status)
            {
                case ReadResultStatus.Success:
                    return StatusSuccess;
                case ReadResultStatus.Timeout:
                    return StatusTimeout;
                case ReadResultStatus.Disconnected:
                    return StatusDisconnected;
                case ReadResultStatus.Canceled:
                    return StatusCanceled;
                case ReadResultStatus.ClientNotFound:
                    return StatusClientNotFound;
                default:
                    return StatusError;
            }
        }

        internal static string ToStatus(Exception exception)
        {
            if (exception is TimeoutException) return StatusTimeout;
            if (exception is OperationCanceledException) return StatusCanceled;
            return StatusError;
        }

        private static TagList CreateOperationTags(string role, string operation, string status)
        {
            TagList tags = new TagList();
            tags.Add(AttributeRole, role);
            tags.Add(AttributeOperation, operation);
            tags.Add(AttributeStatus, status);
            return tags;
        }

        private static TagList CreateStatusTags(string role, string status)
        {
            TagList tags = new TagList();
            tags.Add(AttributeRole, role);
            tags.Add(AttributeStatus, status);
            return tags;
        }

        private static TagList CreateConnectionTags(string role, bool ssl)
        {
            TagList tags = new TagList();
            tags.Add(AttributeRole, role);
            tags.Add(AttributeTransport, GetTransport(ssl));
            return tags;
        }

        private static string GetTransport(bool ssl)
        {
            return ssl ? TransportTls : TransportTcp;
        }

        private static string ToReason(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.Kicked:
                    return "kicked";
                case DisconnectReason.Timeout:
                    return "timeout";
                case DisconnectReason.ConnectionDeclined:
                    return "connection_declined";
                case DisconnectReason.Normal:
                default:
                    return "normal";
            }
        }

        private static double GetElapsedSeconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;
        }
    }
}
