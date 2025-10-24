$content = Get-Content "src\Test.AutomatedTest\Program.cs" -Raw

# Pattern 1: Add port declaration before server creation (when server comes first)
$content = $content -replace '(try\s*\{\s*)(server = new CavemanTcpServer\(_hostname, port)', '$1int port = GetNextPort();$2'

# Pattern 2: Add port declaration before client creation (when client comes first)
$content = $content -replace '(try\s*\{\s*bool eventFired[^}]*?\s*)(client = new CavemanTcpClient\(_hostname, port)', '$1int port = GetNextPort();$2'

# Pattern 3: Add Thread.Sleep after server.Start() where missing
$content = $content -replace '(server\.Start\(\);)(\s*\n\s*)(client = new)', '$1$2Thread.Sleep(100);$2$3'
$content = $content -replace '(server\.Start\(\);)(\s*\n\s*)(DateTime startTime)', '$1$2Thread.Sleep(100);$2$3'
$content = $content -replace '(server\.Start\(\);)(\s*\n\s*\n\s*)(client\.Connect)', '$1$2Thread.Sleep(100);$2$3'

# Pattern 4: Add cleanup delays in finally blocks where missing
$content = $content -replace '(finally\s*\{\s*client\?\.Dispose\(\);\s*server\?\.Dispose\(\);)(\s*\})','$1$2Thread.Sleep(100);$2'

Set-Content "src\Test.AutomatedTest\Program.cs" $content
