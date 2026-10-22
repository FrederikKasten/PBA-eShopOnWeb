# Sends a few sample JSON log lines to Logstash on TCP 5000.
param(
    [string]$LogstashHost = "localhost",
    [int]$Port = 5000,
    [int]$Count = 5
)

$levels = "INFO", "WARN", "ERROR"
$client = [System.Net.Sockets.TcpClient]::new($LogstashHost, $Port)
$writer = [System.IO.StreamWriter]::new($client.GetStream())

for ($i = 1; $i -le $Count; $i++) {
    $event = @{
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
        level     = $levels[$i % $levels.Count]
        service   = "test-script"
        message   = "Test log message #$i"
    } | ConvertTo-Json -Compress
    $writer.WriteLine($event)
}

$writer.Flush()
$client.Close()
Write-Host "Sent $Count events to ${LogstashHost}:$Port"
