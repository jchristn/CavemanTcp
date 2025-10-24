# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CavemanTcp is a .NET TCP client/server library that provides developers with full control over network reads and writes. Unlike higher-level libraries (SimpleTcp, WatsonTcp), CavemanTcp requires explicit read/write calls, making it ideal for building custom state machines on top of TCP.

**Key Design Principle**: This library does NOT continuously monitor connections. Disconnections are typically detected during read/write operations, so applications should expect and handle exceptions during these operations.

## Build Commands

```bash
# Build the solution
dotnet build src/CavemanTcp.sln

# Build specific configuration
dotnet build src/CavemanTcp.sln -c Release
dotnet build src/CavemanTcp.sln -c Debug

# Build the library only
dotnet build src/CavemanTcp/CavemanTcp.csproj

# Build and pack NuGet package
dotnet pack src/CavemanTcp/CavemanTcp.csproj -c Release
```

## Target Frameworks

The library targets multiple frameworks: `netstandard2.0`, `netstandard2.1`, `net462`, `net472`, `net48`, `net6.0`, `net8.0`

Note: TCP keepalive support is available for .NET Core and .NET Framework, but NOT for .NET Standard.

## Project Structure

- **src/CavemanTcp/** - Main library source code
- **src/Test.Client/** - Synchronous client test application
- **src/Test.ClientAsync/** - Asynchronous client test application
- **src/Test.Server/** - Synchronous server test application
- **src/Test.ServerAsync/** - Asynchronous server test application
- **src/Test.SslClient/** - SSL client test application
- **src/Test.SslServer/** - SSL server test application
- **src/Test.Disconnect/** - Disconnect handling test application
- **src/Test.HttpLoopback/** - HTTP loopback test application

## Architecture Overview

### Core Components

**CavemanTcpServer** - TCP server that accepts client connections
- Clients are tracked by `Guid` (not IP:port string)
- Uses `ConcurrentDictionary` for client management
- Exposes `ClientConnected` and `ClientDisconnected` events
- Send/Read methods target specific clients by Guid
- Returns `ClientMetadata` enumeration via `GetClients()`

**CavemanTcpClient** - TCP client that connects to servers
- Manual connection via `Connect(int timeout)`
- Exposes `ClientConnected` and `ClientDisconnected` events
- Send/Read methods operate on the server connection

**ClientMetadata** - Represents a connected client
- Contains `Guid` (unique identifier), `IpPort` (string), `Name`, and custom `Metadata`
- Manages underlying `TcpClient`, `NetworkStream`, and optional `SslStream`
- Uses `SemaphoreSlim` for thread-safe read/write operations
- Implements `IDisposable` for proper resource cleanup

### Result Objects

**ReadResult** - Returned from all read operations
- `Status`: `ClientNotFound`, `Success`, `Timeout`, `Disconnected`, `Canceled`
- `BytesRead`: Number of bytes read from socket
- `DataStream`: MemoryStream containing data
- `Data`: byte[] property that fully reads DataStream (use once)

**WriteResult** - Returned from all write operations
- `Status`: `ClientNotFound`, `Success`, `Timeout`, `Disconnected`, `Canceled`
- `BytesWritten`: Number of bytes written to socket

### Settings Classes

- **CavemanTcpClientSettings** / **CavemanTcpServerSettings** - Configuration options
- **CavemanTcpKeepaliveSettings** - TCP keepalive configuration (not available on .NET Standard)
- **CavemanTcpStatistics** - Connection statistics tracking

### Event Arguments

- **ClientConnectedEventArgs** - Client connection events
- **ClientDisconnectedEventArgs** - Client disconnection events (includes `DisconnectReason`)
- **ClientDeclinedEventArgs** - Server rejected client connection
- **ExceptionEventArgs** - Exception handling events

## Important Implementation Details

### Client Identification (v2.0.x Breaking Change)
- Clients are now referenced by `Guid` instead of `string ipPort`
- Old `Send(string ipPort, ...)` and `Read(string ipPort, ...)` methods are marked obsolete
- Use `Send(Guid guid, ...)` and `Read(Guid guid, ...)` instead
- Get client list via `server.GetClients()` which returns `IEnumerable<ClientMetadata>`

### Timeout Operations
When using timeout APIs (`SendWithTimeout`, `ReadWithTimeout`, etc.):
- A timeout does NOT mean data wasn't sent/received; check `BytesRead`/`BytesWritten`
- Partial operations may occur - data may still be waiting in the underlying stream
- Valid timeout values: `-1` (no timeout) or positive integers (milliseconds)
- On timeout, consider resetting the connection to avoid stream state issues

### Nagle's Algorithm
Disabled by default in v2.0.x for lower latency.

### SSL/TLS Support
Both client and server support SSL/TLS via X.509 certificates. Test certificates are provided in the repository root (`cavemantcp.pfx`, `cavemantcp.crt`, `cavemantcp.key`).

### Thread Safety
- Read and write operations use `SemaphoreSlim` per client for thread safety
- Multiple concurrent reads/writes to the same client are serialized
- Each client has separate `ReadSemaphore` and `WriteSemaphore` in `ClientMetadata`

## Testing

Run test applications to verify functionality:

```bash
# Run client/server pairs simultaneously in separate terminals
dotnet run --project src/Test.Server
dotnet run --project src/Test.Client

# Async versions
dotnet run --project src/Test.ServerAsync
dotnet run --project src/Test.ClientAsync

# SSL versions
dotnet run --project src/Test.SslServer
dotnet run --project src/Test.SslClient

# Disconnect handling
dotnet run --project src/Test.Disconnect
```

## Common Development Patterns

### Server Pattern
1. Instantiate `TcpServer` with IP, port, SSL settings
2. Wire up `Events.ClientConnected` and `Events.ClientDisconnected` handlers
3. Call `Start()` to begin listening
4. Use `Send(Guid, data)` and `Read(Guid, count)` for client I/O
5. Track client Guids from `ClientConnectedEventArgs`

### Client Pattern
1. Instantiate `TcpClient` with server IP, port, SSL settings
2. Wire up `Events.ClientConnected` and `Events.ClientDisconnected` handlers
3. Call `Connect(timeout)` to connect
4. Use `Send(data)` and `Read(count)` for server I/O
5. Handle exceptions during read/write as indicators of disconnection

### Disconnection Detection
- No background threads monitor connection state
- Disconnects are detected during `Send()` or `Read()` operations
- Check `ReadResult.Status` and `WriteResult.Status` for `Disconnected`
- TCP keepalives can help detect silent disconnections (if enabled and supported)

## Notes for Claude Code

- When making changes to the library, ensure compatibility across all target frameworks
- Pay attention to conditional compilation directives for framework-specific features (especially TCP keepalives)
- The library prioritizes explicit control over convenience - avoid adding auto-reconnect or background monitoring features
- Test changes against multiple test applications (sync/async, client/server, SSL/non-SSL)
- Events should be cleared after disposal to prevent memory leaks (see PR #19)
