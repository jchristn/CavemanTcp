$content = Get-Content "src\Test.AutomatedTest\Program.cs" -Raw
$content = $content -replace 'new CavemanTcpServer\(_hostname, _port, false, null, null\)', 'new CavemanTcpServer(_hostname, port, false, null, null)'
$content = $content -replace 'new CavemanTcpClient\(_hostname, _port, false, null, null\)', 'new CavemanTcpClient(_hostname, port, false, null, null)'
Set-Content "src\Test.AutomatedTest\Program.cs" $content
