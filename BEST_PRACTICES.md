# CavemanTcp Best Practices Guide

A comprehensive guide for building stable and resilient TCP client and server applications using CavemanTcp.

## Table of Contents

1. [Understanding CavemanTcp's Design Philosophy](#understanding-cavemantcps-design-philosophy)
2. [Client Connection Management](#client-connection-management)
3. [Server Connection Management](#server-connection-management)
4. [Handling Read and Write Results](#handling-read-and-write-results)
5. [Reconnection Strategies](#reconnection-strategies)
6. [Event Handling](#event-handling)
7. [Timeout Management](#timeout-management)
8. [TCP Keepalive Configuration](#tcp-keepalive-configuration)
9. [Thread Safety Considerations](#thread-safety-considerations)
10. [Resource Cleanup](#resource-cleanup)
11. [Logging and Diagnostics](#logging-and-diagnostics)
12. [Common Pitfalls](#common-pitfalls)
13. [Complete Examples](#complete-examples)

---

## Understanding CavemanTcp's Design Philosophy

CavemanTcp is designed for developers who need **explicit control** over TCP communication. Unlike higher-level libraries that abstract away the details, CavemanTcp requires you to:

- **Explicitly read and write data** - No automatic message framing or background receiving
- **Handle disconnections during I/O** - Disconnections are typically detected during `Read()` or `Send()` operations
- **Manage your own application protocol** - You decide message formats, framing, and sequencing

### Key Implications

1. **No Background Monitoring**: The library does not continuously monitor connections. A connection may be dead, but you won't know until you try to read or write.

2. **Synchronous Control Flow**: You control when reads and writes happen, making it ideal for request-response protocols or custom state machines.

3. **Exception-Based Detection**: Many disconnection scenarios surface as exceptions during I/O operations, or as specific `ReadResultStatus`/`WriteResultStatus` values.

---

## Client Connection Management

### Basic Connection Pattern

```csharp
using CavemanTcp;

public class TcpClientManager
{
    private CavemanTcpClient _client;
    private readonly string _serverIp;
    private readonly int _serverPort;
    private bool _shouldReconnect = true;

    public TcpClientManager(string serverIp, int serverPort)
    {
        _serverIp = serverIp;
        _serverPort = serverPort;
    }

    public bool Connect(int timeoutSeconds = 5)
    {
        try
        {
            _client = new CavemanTcpClient(_serverIp, _serverPort, false, null, null);

            // Wire up events BEFORE connecting
            _client.Events.ClientConnected += OnConnected;
            _client.Events.ClientDisconnected += OnDisconnected;
            _client.Events.ExceptionEncountered += OnException;

            // Optional: Configure settings
            _client.Settings.StreamBufferSize = 65536;

            // Optional: Enable TCP keepalive (not available on .NET Standard)
            _client.Keepalive.EnableTcpKeepAlives = true;
            _client.Keepalive.TcpKeepAliveTime = 5;
            _client.Keepalive.TcpKeepAliveInterval = 5;
            _client.Keepalive.TcpKeepAliveRetryCount = 3;

            // Attempt connection
            _client.Connect(timeoutSeconds);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection failed: {ex.Message}");
            return false;
        }
    }

    private void OnConnected(object sender, EventArgs e)
    {
        Console.WriteLine("Connected to server");
    }

    private void OnDisconnected(object sender, EventArgs e)
    {
        Console.WriteLine("Disconnected from server");

        // Trigger reconnection if desired
        if (_shouldReconnect)
        {
            Task.Run(() => ReconnectWithBackoff());
        }
    }

    private void OnException(object sender, ExceptionEventArgs e)
    {
        Console.WriteLine($"Exception: {e.Exception.Message}");
    }
}
```

### Checking Connection State

```csharp
// Check if connected before operations
if (_client != null && _client.IsConnected)
{
    // Safe to attempt I/O
}
else
{
    // Need to reconnect
}
```

**Important**: `IsConnected` reflects the last known state. The connection may have dropped since the last I/O operation. Always handle disconnection scenarios in your read/write code.

---

## Server Connection Management

### Basic Server Pattern

```csharp
using CavemanTcp;
using System.Collections.Concurrent;

public class TcpServerManager
{
    private CavemanTcpServer _server;
    private readonly ConcurrentDictionary<Guid, ClientMetadata> _clients = new();
    private readonly string _listenerIp;
    private readonly int _listenerPort;

    public TcpServerManager(string listenerIp, int listenerPort)
    {
        _listenerIp = listenerIp;
        _listenerPort = listenerPort;
    }

    public void Start()
    {
        _server = new CavemanTcpServer(_listenerIp, _listenerPort, false, null, null);

        // Wire up events BEFORE starting
        _server.Events.ClientConnected += OnClientConnected;
        _server.Events.ClientDisconnected += OnClientDisconnected;
        _server.Events.ExceptionEncountered += OnException;

        // Optional: Configure settings
        _server.Settings.MaxConnections = 100;
        _server.Settings.StreamBufferSize = 65536;

        // Optional: IP filtering
        // _server.Settings.PermittedIPs.Add("192.168.1.0/24");
        // _server.Settings.BlockedIPs.Add("10.0.0.50");

        _server.Start();
        Console.WriteLine($"Server listening on {_listenerIp}:{_listenerPort}");
    }

    private void OnClientConnected(object sender, ClientConnectedEventArgs e)
    {
        Console.WriteLine($"Client connected: {e.Client.Guid} from {e.Client.IpPort}");

        // Track the client
        _clients[e.Client.Guid] = e.Client;

        // Optional: Store custom metadata
        e.Client.Name = "NewClient";
        e.Client.Metadata = new { ConnectedAt = DateTime.UtcNow };
    }

    private void OnClientDisconnected(object sender, ClientDisconnectedEventArgs e)
    {
        Console.WriteLine($"Client disconnected: {e.Client.Guid}, Reason: {e.Reason}");

        // Remove from tracking
        _clients.TryRemove(e.Client.Guid, out _);
    }

    private void OnException(object sender, ExceptionEventArgs e)
    {
        Console.WriteLine($"Server exception: {e.Exception.Message}");
    }

    // Get all connected clients
    public IEnumerable<ClientMetadata> GetConnectedClients()
    {
        return _server.GetClients();
    }
}
```

### Client Identification

Clients are identified by `Guid`, not by IP:port string. This is important because:

- Multiple connections from the same IP are distinguishable
- The Guid remains stable for the lifetime of the connection
- IP:port strings can be reused after disconnection

```csharp
// Store client Guid when they connect
private void OnClientConnected(object sender, ClientConnectedEventArgs e)
{
    Guid clientId = e.Client.Guid;
    string clientAddress = e.Client.IpPort;

    // Use clientId for all subsequent operations
    _server.Send(clientId, "Welcome!");
}
```

---

## Handling Read and Write Results

**Always check the result status** after every read or write operation. This is the primary mechanism for detecting disconnections.

### Read Result Handling

```csharp
public byte[] SafeRead(int byteCount, int timeoutMs = 5000)
{
    ReadResult result = _client.ReadWithTimeout(timeoutMs, byteCount);

    switch (result.Status)
    {
        case ReadResultStatus.Success:
            return result.Data;  // Note: Accessing .Data consumes the stream

        case ReadResultStatus.Disconnected:
            Console.WriteLine("Server disconnected during read");
            HandleDisconnection();
            return null;

        case ReadResultStatus.Timeout:
            Console.WriteLine($"Read timed out after {timeoutMs}ms");
            // Decide: retry, reconnect, or fail
            return null;

        case ReadResultStatus.Canceled:
            Console.WriteLine("Read was canceled");
            return null;

        default:
            Console.WriteLine($"Unexpected read status: {result.Status}");
            return null;
    }
}
```

### Write Result Handling

```csharp
public bool SafeSend(byte[] data, int timeoutMs = 5000)
{
    WriteResult result = _client.SendWithTimeout(timeoutMs, data);

    switch (result.Status)
    {
        case WriteResultStatus.Success:
            if (result.BytesWritten != data.Length)
            {
                Console.WriteLine($"Partial write: {result.BytesWritten}/{data.Length} bytes");
                // Handle partial write scenario
            }
            return true;

        case WriteResultStatus.Disconnected:
            Console.WriteLine("Server disconnected during write");
            HandleDisconnection();
            return false;

        case WriteResultStatus.Timeout:
            Console.WriteLine($"Write timed out after {timeoutMs}ms");
            // Connection may be in an inconsistent state
            HandleDisconnection();
            return false;

        case WriteResultStatus.Canceled:
            Console.WriteLine("Write was canceled");
            return false;

        default:
            Console.WriteLine($"Unexpected write status: {result.Status}");
            return false;
    }
}
```

### Server-Side Client Operations

```csharp
public bool SendToClient(Guid clientId, byte[] data)
{
    WriteResult result = _server.Send(clientId, data);

    if (result.Status == WriteResultStatus.ClientNotFound)
    {
        Console.WriteLine($"Client {clientId} not found - may have disconnected");
        return false;
    }

    if (result.Status == WriteResultStatus.Disconnected)
    {
        Console.WriteLine($"Client {clientId} disconnected during send");
        return false;
    }

    return result.Status == WriteResultStatus.Success;
}
```

---

## Reconnection Strategies

### Exponential Backoff

The most common and recommended reconnection strategy:

```csharp
public class ReconnectingClient
{
    private CavemanTcpClient _client;
    private readonly string _serverIp;
    private readonly int _serverPort;

    private bool _shouldReconnect = true;
    private bool _isReconnecting = false;
    private readonly object _reconnectLock = new object();

    // Backoff configuration
    private const int InitialDelayMs = 1000;      // 1 second
    private const int MaxDelayMs = 60000;         // 60 seconds
    private const double BackoffMultiplier = 2.0;
    private const int MaxRetries = 10;            // 0 for infinite

    public async Task ReconnectWithBackoffAsync()
    {
        lock (_reconnectLock)
        {
            if (_isReconnecting) return;
            _isReconnecting = true;
        }

        int currentDelay = InitialDelayMs;
        int attempts = 0;

        while (_shouldReconnect && (MaxRetries == 0 || attempts < MaxRetries))
        {
            attempts++;
            Console.WriteLine($"Reconnection attempt {attempts}, waiting {currentDelay}ms...");

            await Task.Delay(currentDelay);

            if (!_shouldReconnect) break;

            try
            {
                // Dispose old client if exists
                _client?.Dispose();

                // Create new client and connect
                _client = new CavemanTcpClient(_serverIp, _serverPort, false, null, null);
                SetupClientEvents();
                _client.Connect(5);

                Console.WriteLine("Reconnected successfully!");

                lock (_reconnectLock)
                {
                    _isReconnecting = false;
                }
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reconnection attempt {attempts} failed: {ex.Message}");

                // Increase delay with exponential backoff
                currentDelay = Math.Min((int)(currentDelay * BackoffMultiplier), MaxDelayMs);
            }
        }

        lock (_reconnectLock)
        {
            _isReconnecting = false;
        }

        if (attempts >= MaxRetries && MaxRetries > 0)
        {
            Console.WriteLine("Max reconnection attempts reached. Giving up.");
            OnReconnectionFailed?.Invoke();
        }
    }

    public event Action OnReconnectionFailed;

    public void StopReconnecting()
    {
        _shouldReconnect = false;
    }
}
```

### Circuit Breaker Pattern

For scenarios where you want to stop attempting connections after repeated failures:

```csharp
public class CircuitBreaker
{
    private enum State { Closed, Open, HalfOpen }

    private State _state = State.Closed;
    private int _failureCount = 0;
    private DateTime _lastFailureTime;

    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;

    public CircuitBreaker(int failureThreshold = 5, int openDurationSeconds = 30)
    {
        _failureThreshold = failureThreshold;
        _openDuration = TimeSpan.FromSeconds(openDurationSeconds);
    }

    public bool CanAttempt()
    {
        switch (_state)
        {
            case State.Closed:
                return true;

            case State.Open:
                if (DateTime.UtcNow - _lastFailureTime > _openDuration)
                {
                    _state = State.HalfOpen;
                    return true;
                }
                return false;

            case State.HalfOpen:
                return true;

            default:
                return false;
        }
    }

    public void RecordSuccess()
    {
        _state = State.Closed;
        _failureCount = 0;
    }

    public void RecordFailure()
    {
        _failureCount++;
        _lastFailureTime = DateTime.UtcNow;

        if (_failureCount >= _failureThreshold)
        {
            _state = State.Open;
            Console.WriteLine($"Circuit breaker opened after {_failureCount} failures");
        }
    }

    public bool IsOpen => _state == State.Open;
}

// Usage
public async Task ConnectWithCircuitBreakerAsync()
{
    if (!_circuitBreaker.CanAttempt())
    {
        Console.WriteLine("Circuit breaker is open - not attempting connection");
        return;
    }

    try
    {
        _client.Connect(5);
        _circuitBreaker.RecordSuccess();
    }
    catch
    {
        _circuitBreaker.RecordFailure();
        throw;
    }
}
```

---

## Event Handling

### Event Subscription Best Practices

1. **Subscribe before connecting/starting** - Events may fire immediately upon connection
2. **Keep handlers fast** - Long-running operations should be offloaded to background tasks
3. **Handle exceptions in handlers** - Unhandled exceptions can crash your application

```csharp
// Good: Subscribe before Connect()
_client = new CavemanTcpClient(ip, port, false, null, null);
_client.Events.ClientConnected += OnConnected;
_client.Events.ClientDisconnected += OnDisconnected;
_client.Connect(5);

// Bad: Subscribe after Connect() - may miss the ClientConnected event
_client = new CavemanTcpClient(ip, port, false, null, null);
_client.Connect(5);
_client.Events.ClientConnected += OnConnected;  // Too late!
```

### Safe Event Handler Pattern

```csharp
private void OnClientDisconnected(object sender, EventArgs e)
{
    try
    {
        // Quick operations only
        _isConnected = false;

        // Offload longer operations
        Task.Run(async () =>
        {
            try
            {
                await HandleDisconnectionAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling disconnection: {ex.Message}");
            }
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception in disconnect handler: {ex.Message}");
    }
}
```

### Server-Side Disconnect Reasons

```csharp
private void OnClientDisconnected(object sender, ClientDisconnectedEventArgs e)
{
    switch (e.Reason)
    {
        case DisconnectReason.Normal:
            Console.WriteLine($"Client {e.Client.Guid} disconnected normally");
            break;

        case DisconnectReason.Kicked:
            Console.WriteLine($"Client {e.Client.Guid} was kicked");
            break;

        case DisconnectReason.Timeout:
            Console.WriteLine($"Client {e.Client.Guid} timed out");
            break;

        case DisconnectReason.ConnectionDeclined:
            Console.WriteLine($"Client {e.Client.Guid} connection was declined");
            break;
    }
}
```

---

## Timeout Management

### Choosing Timeout Values

| Scenario | Recommended Timeout |
|----------|-------------------|
| Local network, fast operations | 5-10 seconds |
| Internet, normal operations | 15-30 seconds |
| Large file transfers | 60+ seconds or -1 (infinite) |
| Heartbeat/ping operations | 2-5 seconds |

### Using Cancellation Tokens

```csharp
public async Task<byte[]> ReadWithCancellationAsync(int byteCount, CancellationToken cancellationToken)
{
    // Combine timeout with user cancellation
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    linkedCts.CancelAfter(TimeSpan.FromSeconds(30));  // 30 second timeout

    try
    {
        ReadResult result = await _client.ReadWithTimeoutAsync(30000, byteCount, linkedCts.Token);

        if (result.Status == ReadResultStatus.Canceled)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Operation canceled by user");
            }
            else
            {
                Console.WriteLine("Operation timed out");
            }
            return null;
        }

        return result.Status == ReadResultStatus.Success ? result.Data : null;
    }
    catch (OperationCanceledException)
    {
        return null;
    }
}
```

### Timeout Behavior Notes

- **Timeout does not guarantee no data was transferred** - Check `BytesRead`/`BytesWritten`
- **After timeout, the stream may be in an inconsistent state** - Consider reconnecting
- **Use -1 for no timeout** in scenarios where you're willing to wait indefinitely

---

## TCP Keepalive Configuration

TCP keepalives help detect dead connections when there's no application-level traffic.

**Note**: TCP keepalives are **not available on .NET Standard**. They work on .NET Framework and .NET Core/.NET 5+.

```csharp
// Client keepalive configuration
_client.Keepalive.EnableTcpKeepAlives = true;
_client.Keepalive.TcpKeepAliveTime = 15;       // Start probing after 15 seconds of inactivity
_client.Keepalive.TcpKeepAliveInterval = 5;    // Probe every 5 seconds
_client.Keepalive.TcpKeepAliveRetryCount = 3;  // Give up after 3 failed probes

// Server keepalive configuration (applies to all client connections)
_server.Keepalive.EnableTcpKeepAlives = true;
_server.Keepalive.TcpKeepAliveTime = 15;
_server.Keepalive.TcpKeepAliveInterval = 5;
_server.Keepalive.TcpKeepAliveRetryCount = 3;
```

### When to Use TCP Keepalives

- Long-lived connections with periods of inactivity
- Connections through firewalls/NAT that may drop idle connections
- When you need to detect dead peers without application-level heartbeats

### Application-Level Heartbeats

For more control, implement your own heartbeat mechanism:

```csharp
public class HeartbeatManager
{
    private readonly CavemanTcpClient _client;
    private CancellationTokenSource _heartbeatCts;
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(5);

    public async Task StartHeartbeatAsync()
    {
        _heartbeatCts = new CancellationTokenSource();

        while (!_heartbeatCts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatInterval, _heartbeatCts.Token);

                if (!await SendHeartbeatAsync())
                {
                    Console.WriteLine("Heartbeat failed - connection may be dead");
                    OnHeartbeatFailed?.Invoke();
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> SendHeartbeatAsync()
    {
        byte[] ping = Encoding.UTF8.GetBytes("PING");

        var sendResult = await _client.SendWithTimeoutAsync(
            (int)_heartbeatTimeout.TotalMilliseconds, ping);

        if (sendResult.Status != WriteResultStatus.Success)
            return false;

        var readResult = await _client.ReadWithTimeoutAsync(
            (int)_heartbeatTimeout.TotalMilliseconds, 4);  // Expect "PONG"

        return readResult.Status == ReadResultStatus.Success;
    }

    public void StopHeartbeat()
    {
        _heartbeatCts?.Cancel();
    }

    public event Action OnHeartbeatFailed;
}
```

---

## Thread Safety Considerations

CavemanTcp uses per-client semaphores to ensure thread-safe read and write operations. However, you should still follow these guidelines:

### Client-Side Thread Safety

```csharp
public class ThreadSafeClient
{
    private CavemanTcpClient _client;
    private readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1, 1);

    // Multiple threads can safely call Send/Read concurrently
    // The library serializes operations internally

    // However, reconnection should be synchronized
    public async Task SafeReconnectAsync()
    {
        await _reconnectLock.WaitAsync();
        try
        {
            if (_client?.IsConnected == true) return;

            _client?.Dispose();
            _client = new CavemanTcpClient(_serverIp, _serverPort, false, null, null);
            _client.Connect(5);
        }
        finally
        {
            _reconnectLock.Release();
        }
    }
}
```

### Server-Side Thread Safety

```csharp
// Safe: Multiple threads reading from different clients
Task.Run(() => _server.Read(client1Guid, 100));
Task.Run(() => _server.Read(client2Guid, 100));

// Safe: Multiple threads writing to different clients
Task.Run(() => _server.Send(client1Guid, data));
Task.Run(() => _server.Send(client2Guid, data));

// Safe but serialized: Multiple threads accessing same client
// Operations will be queued internally
Task.Run(() => _server.Read(client1Guid, 100));
Task.Run(() => _server.Send(client1Guid, data));  // Waits for read to complete
```

---

## Resource Cleanup

### Proper Disposal Pattern

```csharp
public class ManagedTcpClient : IDisposable
{
    private CavemanTcpClient _client;
    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Stop any background operations first
            _shouldReconnect = false;
            _heartbeatCts?.Cancel();

            // Dispose the client
            if (_client != null)
            {
                try
                {
                    if (_client.IsConnected)
                    {
                        _client.Disconnect();
                    }
                }
                catch { /* Ignore disconnection errors during disposal */ }

                _client.Dispose();
                _client = null;
            }
        }

        _disposed = true;
    }

    ~ManagedTcpClient()
    {
        Dispose(false);
    }
}
```

### Using Statement

```csharp
// For short-lived connections
using (var client = new CavemanTcpClient("127.0.0.1", 9000, false, null, null))
{
    client.Connect(5);
    client.Send("Hello");
    var response = client.ReadWithTimeout(5000, 100);
}
// Client is automatically disposed here
```

---

## Logging and Diagnostics

### Built-in Logger

```csharp
_client.Logger = (message) =>
{
    Console.WriteLine($"[CavemanTcp] {DateTime.Now:HH:mm:ss.fff} {message}");

    // Or integrate with your logging framework
    // _logger.LogDebug(message);
};
```

### Statistics Monitoring

```csharp
public void PrintStatistics()
{
    var stats = _client.Statistics;

    Console.WriteLine($"Connection Statistics:");
    Console.WriteLine($"  Start Time: {stats.StartTime}");
    Console.WriteLine($"  Uptime: {stats.UpTime}");
    Console.WriteLine($"  Bytes Sent: {stats.SentBytes:N0}");
    Console.WriteLine($"  Bytes Received: {stats.ReceivedBytes:N0}");
}

// Reset counters (useful for periodic reporting)
_client.Statistics.Reset();
```

---

## Common Pitfalls

### 1. Not Checking Result Status

```csharp
// BAD: Ignoring the result
_client.Send(data);
var response = _client.Read(100);
ProcessResponse(response.Data);  // May crash if read failed!

// GOOD: Always check status
var sendResult = _client.Send(data);
if (sendResult.Status != WriteResultStatus.Success)
{
    HandleSendFailure(sendResult.Status);
    return;
}

var readResult = _client.Read(100);
if (readResult.Status != ReadResultStatus.Success)
{
    HandleReadFailure(readResult.Status);
    return;
}
ProcessResponse(readResult.Data);
```

### 2. Consuming ReadResult.Data Multiple Times

```csharp
// BAD: Accessing .Data twice
var result = _client.Read(100);
Console.WriteLine($"Received {result.Data.Length} bytes");  // First access - OK
ProcessData(result.Data);  // Second access - returns empty array!

// GOOD: Store the data first
var result = _client.Read(100);
byte[] data = result.Data;  // Consume once
Console.WriteLine($"Received {data.Length} bytes");
ProcessData(data);

// ALTERNATIVE: Use DataStream directly
var result = _client.Read(100);
using (var reader = new BinaryReader(result.DataStream))
{
    // Read from the stream
}
```

### 3. Not Handling Partial Operations

```csharp
// BAD: Assuming all bytes were written
_client.Send(largeData);

// GOOD: Verify bytes written
var result = _client.Send(largeData);
if (result.BytesWritten < largeData.Length)
{
    Console.WriteLine($"Only {result.BytesWritten}/{largeData.Length} bytes sent");
    // Handle partial send - may need to reconnect
}
```

### 4. Subscribing to Events After Connect

```csharp
// BAD: May miss the ClientConnected event
_client.Connect(5);
_client.Events.ClientConnected += OnConnected;  // Too late!

// GOOD: Subscribe first
_client.Events.ClientConnected += OnConnected;
_client.Connect(5);
```

### 5. Not Disposing Clients

```csharp
// BAD: Leaking resources
void ProcessMessage()
{
    var client = new CavemanTcpClient("127.0.0.1", 9000, false, null, null);
    client.Connect(5);
    client.Send("Hello");
    // Client never disposed - resource leak!
}

// GOOD: Proper disposal
void ProcessMessage()
{
    using var client = new CavemanTcpClient("127.0.0.1", 9000, false, null, null);
    client.Connect(5);
    client.Send("Hello");
}
```

### 6. Blocking the Event Handler Thread

```csharp
// BAD: Long-running operation in event handler
private void OnClientConnected(object sender, ClientConnectedEventArgs e)
{
    Thread.Sleep(5000);  // Blocks other events!
    LoadClientDataFromDatabase(e.Client.Guid);
}

// GOOD: Offload to background task
private void OnClientConnected(object sender, ClientConnectedEventArgs e)
{
    var clientGuid = e.Client.Guid;
    Task.Run(() => LoadClientDataFromDatabase(clientGuid));
}
```

---

## Complete Examples

### Robust Client Example

```csharp
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CavemanTcp;

public class RobustTcpClient : IDisposable
{
    private CavemanTcpClient _client;
    private readonly string _serverIp;
    private readonly int _serverPort;

    private bool _shouldRun = true;
    private bool _isReconnecting = false;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    private const int ReconnectDelayMs = 1000;
    private const int MaxReconnectDelayMs = 30000;
    private const int ConnectionTimeoutSeconds = 5;
    private const int IoTimeoutMs = 10000;

    public event Action<byte[]> OnDataReceived;
    public event Action OnConnected;
    public event Action OnDisconnected;

    public bool IsConnected => _client?.IsConnected ?? false;

    public RobustTcpClient(string serverIp, int serverPort)
    {
        _serverIp = serverIp;
        _serverPort = serverPort;
    }

    public async Task StartAsync()
    {
        _shouldRun = true;
        await ConnectAsync();
    }

    public void Stop()
    {
        _shouldRun = false;
        _client?.Dispose();
    }

    private async Task ConnectAsync()
    {
        int delay = ReconnectDelayMs;

        while (_shouldRun)
        {
            try
            {
                _client = new CavemanTcpClient(_serverIp, _serverPort, false, null, null);

                _client.Events.ClientConnected += (s, e) => OnConnected?.Invoke();
                _client.Events.ClientDisconnected += (s, e) =>
                {
                    OnDisconnected?.Invoke();
                    if (_shouldRun) Task.Run(() => ReconnectAsync());
                };

                _client.Keepalive.EnableTcpKeepAlives = true;
                _client.Keepalive.TcpKeepAliveTime = 15;
                _client.Keepalive.TcpKeepAliveInterval = 5;
                _client.Keepalive.TcpKeepAliveRetryCount = 3;

                _client.Connect(ConnectionTimeoutSeconds);
                Console.WriteLine("Connected successfully");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
                _client?.Dispose();

                await Task.Delay(delay);
                delay = Math.Min(delay * 2, MaxReconnectDelayMs);
            }
        }
    }

    private async Task ReconnectAsync()
    {
        await _reconnectLock.WaitAsync();
        try
        {
            if (_isReconnecting || !_shouldRun) return;
            _isReconnecting = true;

            Console.WriteLine("Reconnecting...");
            _client?.Dispose();
            await ConnectAsync();
        }
        finally
        {
            _isReconnecting = false;
            _reconnectLock.Release();
        }
    }

    public async Task<bool> SendAsync(byte[] data)
    {
        if (!IsConnected) return false;

        try
        {
            var result = await _client.SendWithTimeoutAsync(IoTimeoutMs, data);

            if (result.Status == WriteResultStatus.Success)
                return true;

            if (result.Status == WriteResultStatus.Disconnected)
                await ReconnectAsync();

            return false;
        }
        catch
        {
            await ReconnectAsync();
            return false;
        }
    }

    public async Task<byte[]> ReadAsync(int count)
    {
        if (!IsConnected) return null;

        try
        {
            var result = await _client.ReadWithTimeoutAsync(IoTimeoutMs, count);

            if (result.Status == ReadResultStatus.Success)
                return result.Data;

            if (result.Status == ReadResultStatus.Disconnected)
                await ReconnectAsync();

            return null;
        }
        catch
        {
            await ReconnectAsync();
            return null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
```

### Robust Server Example

```csharp
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using CavemanTcp;

public class RobustTcpServer : IDisposable
{
    private CavemanTcpServer _server;
    private readonly string _listenerIp;
    private readonly int _listenerPort;
    private readonly ConcurrentDictionary<Guid, ClientState> _clients = new();

    private const int IoTimeoutMs = 10000;

    public event Action<Guid, byte[]> OnDataReceived;
    public event Action<Guid> OnClientConnected;
    public event Action<Guid, DisconnectReason> OnClientDisconnected;

    public RobustTcpServer(string listenerIp, int listenerPort)
    {
        _listenerIp = listenerIp;
        _listenerPort = listenerPort;
    }

    public void Start()
    {
        _server = new CavemanTcpServer(_listenerIp, _listenerPort, false, null, null);

        _server.Events.ClientConnected += HandleClientConnected;
        _server.Events.ClientDisconnected += HandleClientDisconnected;
        _server.Events.ExceptionEncountered += (s, e) =>
            Console.WriteLine($"Server exception: {e.Exception.Message}");

        _server.Settings.MaxConnections = 100;

        _server.Keepalive.EnableTcpKeepAlives = true;
        _server.Keepalive.TcpKeepAliveTime = 15;
        _server.Keepalive.TcpKeepAliveInterval = 5;
        _server.Keepalive.TcpKeepAliveRetryCount = 3;

        _server.Start();
        Console.WriteLine($"Server started on {_listenerIp}:{_listenerPort}");
    }

    private void HandleClientConnected(object sender, ClientConnectedEventArgs e)
    {
        var state = new ClientState { Metadata = e.Client };
        _clients[e.Client.Guid] = state;

        Console.WriteLine($"Client connected: {e.Client.Guid} from {e.Client.IpPort}");
        OnClientConnected?.Invoke(e.Client.Guid);
    }

    private void HandleClientDisconnected(object sender, ClientDisconnectedEventArgs e)
    {
        _clients.TryRemove(e.Client.Guid, out _);

        Console.WriteLine($"Client disconnected: {e.Client.Guid}, Reason: {e.Reason}");
        OnClientDisconnected?.Invoke(e.Client.Guid, e.Reason);
    }

    public async Task<bool> SendToClientAsync(Guid clientId, byte[] data)
    {
        try
        {
            var result = await _server.SendWithTimeoutAsync(IoTimeoutMs, clientId, data);
            return result.Status == WriteResultStatus.Success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending to {clientId}: {ex.Message}");
            return false;
        }
    }

    public async Task BroadcastAsync(byte[] data)
    {
        var tasks = _clients.Keys.Select(id => SendToClientAsync(id, data));
        await Task.WhenAll(tasks);
    }

    public async Task<byte[]> ReadFromClientAsync(Guid clientId, int count)
    {
        try
        {
            var result = await _server.ReadWithTimeoutAsync(IoTimeoutMs, clientId, count);
            return result.Status == ReadResultStatus.Success ? result.Data : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading from {clientId}: {ex.Message}");
            return null;
        }
    }

    public void DisconnectClient(Guid clientId)
    {
        _server.DisconnectClient(clientId);
    }

    public IEnumerable<Guid> GetConnectedClients()
    {
        return _clients.Keys;
    }

    public void Dispose()
    {
        _server?.Stop();
        _server?.Dispose();
    }

    private class ClientState
    {
        public ClientMetadata Metadata { get; set; }
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    }
}
```

---

## Summary

Building robust applications with CavemanTcp requires:

1. **Always check result status** after every read/write operation
2. **Implement reconnection logic** with exponential backoff
3. **Subscribe to events before connecting** to avoid missing notifications
4. **Configure TCP keepalives** for long-lived connections
5. **Handle timeouts appropriately** - they may indicate connection issues
6. **Properly dispose resources** to avoid leaks
7. **Use logging** to diagnose connection issues
8. **Offload long operations** from event handlers to background tasks

By following these practices, you can build reliable TCP applications that gracefully handle network failures and recover automatically.
