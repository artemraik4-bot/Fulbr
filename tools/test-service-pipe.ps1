param(
    [string]$Command = 'ping',
    [string]$Link = '',
    [string]$InterfaceName = 'Fulbr',
    [int]$TimeoutMs = 5000
)

$pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', 'Fulbr.Service', [System.IO.Pipes.PipeDirection]::InOut)
try {
    $pipe.Connect($TimeoutMs)
    $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false), 4096, $true)
    $writer.AutoFlush = $true
    $reader = [System.IO.StreamReader]::new($pipe, [System.Text.UTF8Encoding]::new($false), $false, 4096, $true)
    $request = @{ command = $Command; link = $Link; interfaceName = $InterfaceName } | ConvertTo-Json -Compress
    $writer.WriteLine($request)
    $response = $reader.ReadLine()
    Write-Output $response
}
finally {
    $pipe.Dispose()
}
