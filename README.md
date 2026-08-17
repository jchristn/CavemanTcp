![alt tag](https://github.com/jchristn/cavemantcp/blob/master/assets/icon.ico)

# CavemanTcp

[![NuGet Version](https://img.shields.io/nuget/v/CavemanTcp.svg?style=flat)](https://www.nuget.org/packages/CavemanTcp/) [![NuGet](https://img.shields.io/nuget/dt/CavemanTcp.svg)](https://www.nuget.org/packages/CavemanTcp) 

CavemanTcp gives you the ultimate control in building TCP-based applications involving clients and servers.  

With CavemanTcp, you have full control over reading and writing data.  CavemanTcp is designed for those that want explicit control over when data is read or written or want to build a state machine on top of TCP.

Important:
- If you are looking for a package that will continually read data and raise events when data is received, see SimpleTcp: https://github.com/jchristn/simpletcp
- If you are looking for an all-in-one package that handles delivery of well-formed application-layer messages, see WatsonTcp: https://github.com/jchristn/watsontcp

## Disconnection Handling

Since CavemanTcp relies on the consuming application to specify when to read or write, there is still no background receive loop or message pump.  You retain explicit control over I/O, and you should still build your application on the expectation that disconnects can surface during reads and writes.

Client and server connection monitors are available to detect graceful closes and resets quickly using lightweight socket polling.  They are enabled by default.  Silent network failures still require TCP keepalives or an application heartbeat if you need aggressive detection.

As of v1.3.0, TCP keepalive support was added for .NET Core and .NET Framework; unfortunately .NET Standard does not offer this support, so it is not present for apps using CavemanTcp targeted to .NET Standard.

## New in v2.1.1

- BCL metrics and tracing through `System.Diagnostics.Metrics.Meter` and `System.Diagnostics.ActivitySource`
- Stable telemetry source names via `CavemanTcpTelemetry.MeterName` and `CavemanTcpTelemetry.ActivitySourceName`
- Client/server lifecycle, connection, send, read, TLS, authorization, direct stream, and not-found outcomes are measurable by OpenTelemetry/Radiant/Prometheus-style hosts
- Target `netstandard2.0`, `netstandard2.1`, `net462`, `net472`, `net48`, `net8.0`, and `net10.0`
- Lightweight socket-poll connection monitoring for faster disconnect detection
- True cancellable async timeout handling for read and write APIs
- Lower-allocation send/read paths and removal of per-chunk flush overhead
- Faster server client lookup by IP:port and cleaner rejected-connection handling
- `Callbacks.AuthorizeConnection` support and `Events.ClientDeclined`
- Touchstone-based automated, xUnit, and NUnit coverage with 62 shared scenarios spanning positive, negative, concurrency, cancellation, and lifecycle cases

## Examples

### Server Example
```csharp
using CavemanTcp;

// Instantiate
CavemanTcpServer server = new CavemanTcpServer("127.0.0.1", 8000, false, null, null);
server.Logger = Logger;

// Set callbacks and events
server.Callbacks.AuthorizeConnection = (ip, port) => true;
server.Events.ClientConnected += (s, e) => 
{ 
    Console.WriteLine("Client " + e.Client.ToString() + " connected to server");
};
server.Events.ClientDeclined += (s, e) =>
{
    Console.WriteLine("Declined " + e.IpPort + " reason " + e.Reason);
};
server.Events.ClientDisconnected += (s, e) => 
{ 
    Console.WriteLine("Client " + e.Client.ToString() + " disconnected from server"); 
}; 

// Start server
server.Start(); 

// Send [Data] to client at [guid] 
Guid guid = Guid.Parse("00001111-2222-3333-4444-555566667777");
WriteResult wr = null;
wr = server.Send(guid, "[Data]");
wr = server.SendWithTimeout([ms], guid, "[Data]");
wr = await server.SendAsync(guid, "[Data]");
wr = await server.SendWithTimeoutAsync([ms], guid, "[Data]");

// Receive [count] bytes of data from client at [guid]
ReadResult rr = null;
rr = server.Read(guid, [count]);
rr = server.ReadWithTimeout([ms], guid, count);
rr = await server.ReadAsync(guid, [count]);
rr = await server.ReadWithTimeoutAsync([ms], guid, [count]);

// List clients
List<ClientMetadata> clients = server.GetClients().ToList();

// Disconnect a client
server.DisconnectClient(guid);
```

### Client Example
```csharp
using CavemanTcp; 

// Instantiate
CavemanTcpClient client = new CavemanTcpClient("127.0.0.1", 8000, false, null, null);
client.Logger = Logger;

// Set callbacks
client.Events.ClientConnected += (s, e) => 
{ 
    Console.WriteLine("Connected to server"); 
};

client.Events.ClientDisconnected += (s, e) => 
{ 
    Console.WriteLine("Disconnected from server"); 
};

// Connect to server
client.Connect(10);

// Send data to server
WriteResult wr = null;
wr = client.Send("[Data]");
wr = client.SendWithTimeout([ms], "[Data]");
wr = await client.SendAsync("[Data]");
wr = await client.SendWithTimeoutAsync([ms], "[Data]");

// Read [count] bytes of data from server
ReadResult rr = null;
rr = client.Read([count]);
rr = client.ReadWithTimeout([ms], count);
rr = await client.ReadAsync([count]);
rr = await client.ReadWithTimeoutAsync([ms], [count]);
```

## WriteResult and ReadResult

`WriteResult` and `ReadResult` contains a `Status` property that indicates one of the following:

- `ClientNotFound` - only applicable for server read and write operations
- `Success` - the operation was successful
- `Timeout` - the operation timed out
- `Disconnected` - the peer disconnected
- `Canceled` - the operation was canceled by the caller

`WriteResult` also includes:

- `BytesWritten` - the number of bytes written to the socket.

`ReadResult` also includes:

- `BytesRead` - the number of bytes read from the socket.
- `DataStream` - a `MemoryStream` containing the requested data.
- `Data` - a `byte[]` representation of `DataStream`.  Using this property will fully read `DataStream` to the end.

## Telemetry

CavemanTcp emits metrics and traces using only the .NET diagnostics APIs.  Applications can subscribe to `CavemanTcpTelemetry.MeterName` (`"CavemanTcp"`) and `CavemanTcpTelemetry.ActivitySourceName` (`"CavemanTcp"`) from OpenTelemetry, Radiant, `dotnet-counters`, Prometheus exporters, or another diagnostics host.

Metrics use dotted names and low-cardinality tags such as role, operation, status, and transport:

- `cavemantcp.operations`
- `cavemantcp.operation.duration`
- `cavemantcp.bytes.sent`
- `cavemantcp.bytes.received`
- `cavemantcp.connections.opened`
- `cavemantcp.connections.closed`
- `cavemantcp.connections.active`
- `cavemantcp.connections.declined`
- `cavemantcp.exceptions`

## Local vs External Connections

**IMPORTANT**
* If you specify `127.0.0.1` as the listener IP address, it will only be able to accept connections from within the local host.  
* To accept connections from other machines:
  * Use a specific interface IP address, or
  * Use `null`, `*`, `+`, or `0.0.0.0` for the listener IP address (requires admin privileges to listen on any IP address)
* Make sure you create a permit rule on your firewall to allow inbound connections on that port
* If you use a port number under 1024, admin privileges will be required

## Operations with Timeouts

When using any of the APIs that allow you to specify a timeout (i.e. `SendWithTimeout`, `SendWithTimeoutAsync`, `ReadWithTimeout`, and `ReadWithTimeoutAsync`), the resultant `WriteResult` and `ReadResult` as mentioned above will indicate if the operation timed out.  

It is important to understand what a timeout indicates and more important what it doesn't.

- A timeout on a write operation has **nothing to do with whether or not the recipient read the data**.  Rather it is whether or not CavemanTcp was able to write the data to the underlying `NetworkStream` or `SslStream`
- A timeout on a read operation will occur if CavemanTcp is unable to read the specified number of bytes from the underlying `NetworkStream` or `SslStream` in the allotted number of milliseconds
- Timeout and cancellation APIs now cancel the underlying I/O operation instead of timing out a wrapper task
- Valid values for `timeoutMs` are `-1` or any positive integer.  `-1` indicates no timeout and is the same as using an API that doesn't specify a timeout
- Pay close attention to either `BytesRead` or `BytesWritten` (if you were reading or writing) in the event of a timeout.  The timeout may have occurred mid-operation and therefore it will be important to recover from the failure.
  - For example, server sends client 50,000 bytes
  - On the client, a `ReadWithTimeout` was initiated with a 10 second timeout, attempting to read 50,000 bytes
  - In that 10 seconds, the client was only able to read 30,000 bytes
  - A `ReadResult` with `Status == ReadResultStatus.Timeout` is returned, and the `BytesRead` property is set to 30,000
  - In this case, **there are still 20,000 bytes from the server waiting in the client's underlying `NetworkStream` or `SslStream`**
  - As such, it is recommended that, upon timeout, you reset the connection (but this is your choice)

## TCP Keepalives

As of v1.3.0, support for TCP keepalives has been added to CavemanTcp, primarily to address the issue of a network interface being shut down, the cable unplugged, or the media otherwise becoming unavailable.  It is important to note that keepalives are supported in .NET Core and .NET Framework, but NOT .NET Standard.  As of this release, .NET Standard provides no facilities for TCP keepalives.

TCP keepalives are disabled by default.
```csharp
server.Keepalive.EnableTcpKeepAlives = true;
server.Keepalive.TcpKeepAliveInterval = 2;      // seconds to wait before sending subsequent keepalive
server.Keepalive.TcpKeepAliveTime = 2;          // seconds to wait before sending a keepalive
server.Keepalive.TcpKeepAliveRetryCount = 3;    // number of failed keepalive probes before terminating connection
```

Some important notes about TCP keepalives:

- This capability is enabled by the underlying framework and operating system, not provided by this library
- Keepalives only work in .NET Core and .NET Framework
- ```Keepalive.TcpKeepAliveRetryCount``` is only applicable to .NET Core; for .NET Framework, this value is forced to 10

## Testing

Touchstone-backed automated coverage ships in four projects:

- `src/Test.Shared` - shared scenarios and assertions
- `src/Test.Automated` - console runner
- `src/Test.XUnit` - xUnit harness
- `src/Test.NUnit` - NUnit harness

Examples:

```bash
dotnet run --project src/Test.Automated/Test.Automated.csproj -c Release -f net8.0 --no-build
dotnet test src/Test.XUnit/Test.XUnit.csproj -c Release -f net8.0 --no-build
dotnet test src/Test.NUnit/Test.NUnit.csproj -c Release -f net8.0 --no-build
```

## Special Thanks

A special thanks to those that have improved the library!

@LeaT113 @Kliodna @zzampong @SaintedPsycho @samisil @eatyouroats @CetinOzdil 
@akselatom @wtarr @Magicqy @sherman89

## Help or Feedback

Need help or have feedback?  Please file an issue here!

## Version History

Please refer to CHANGELOG.md.

## Thanks

Special thanks to VektorPicker for the free Caveman icon: http://www.vectorpicker.com/caveman-icon_490587_47.html
