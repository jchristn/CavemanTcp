# Change Log

## Current Version

v2.0.x

- Breaking changes
- Clients now referenced by ```Guid``` instead of ```string ipPort```
- ```ListClients``` now returns an enumeration of ```ClientMetadata```
- ```Send``` and ```Read``` methods using ```string ipPort``` are marked obsolete
- ```AddClient``` moved closer to connection acceptance
- Target ```net7.0``` and ```net472```
- Better detection of disconnects
- Disable Nagle's algorithm by default

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

- ```SendWithTimeout```, ```SendWithTimeoutAsync```, ```ReadWithTimeout```, and ```ReadWithTimeoutAsync``` APIs
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