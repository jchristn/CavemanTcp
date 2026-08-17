# Change Log

## Current Version

v2.1.1

- Add BCL metrics and tracing with stable `CavemanTcp` meter/activity source names
- Instrument client/server lifecycle, connections, send/read operations, TLS handshakes, authorization callbacks, direct stream APIs, and not-found outcomes
- Emit low-cardinality metric tags for role, operation, status, transport, and disconnect reason
- Configure package metadata for portable PDB symbol packages with Source Link
- Target `netstandard2.0`, `netstandard2.1`, `net462`, `net472`, `net48`, `net8.0`, and `net10.0`
- Replace heavy connection probing with lightweight socket-poll monitoring
- Rework timeout handling to cancel the underlying async I/O operations
- Remove hot-path polling loops and per-chunk flush overhead
- Eliminate extra `MemoryStream` copies for byte-array sends and reduce read-copy churn
- Add server-side `Callbacks.AuthorizeConnection` plumbing and `Events.ClientDeclined`
- Add IP:port lookup cache and fix rejected-connection cleanup
- Replace `Test.AutomatedTest` with Touchstone-based `Test.Automated`, `Test.Shared`, `Test.XUnit`, and `Test.NUnit`
- Expand automated coverage to 62 shared scenarios across positive, negative, concurrency, cancellation, and lifecycle cases

## Previous Versions

v1.3.3

- Breaking change, disabled TCP keepalives by default due to incompatibility in certain platforms
 
v1.3.2

- .NET 5.0 support
- Better async handling and cancellation

v1.3.0

- Breaking changes
- Retarget to include .NET Core 3.1 (in addition to .NET Standard and .NET Framework)
- Consolidated settings and events into their own separate classes
- Added support for TCP keepalives

v1.2.1

- Better threading for scalability

v1.2.0

- `SendWithTimeout`, `SendWithTimeoutAsync`, `ReadWithTimeout`, and `ReadWithTimeoutAsync` APIs
- Async test client and server
- Disable MutuallyAuthenticate for SSL by default on the client

v1.1.1

- Disable MutuallyAuthenticate for SSL by default

v1.1.0

- Breaking changes; read now returns ReadResult and write now returns WriteResult

v1.0.3

- Dispose fix

v1.0.2

- IsListening (server) and IsConnected (client) properties

v1.0.1

- Async APIs
- Minor refactor
- Dispose bugfixes

v1.0.0

- Initial release
