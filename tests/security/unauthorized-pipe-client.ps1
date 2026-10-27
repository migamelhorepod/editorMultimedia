param([string]$PipeName)
$p = New-Object System.IO.Pipes.NamedPipeClientStream(".", $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
try { $p.Connect(5000); Start-Sleep -Milliseconds 200 } finally { $p.Dispose() }
