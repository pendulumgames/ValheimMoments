param([string]$EncoderPath)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (-not $EncoderPath) { $EncoderPath = Join-Path $workspace 'src/ValheimEventClips.Encoder/bin/Release/net48/ValheimEventClips.Encoder.exe' }
$output = Join-Path $workspace 'artifacts/tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe = Join-Path $output 'EncoderSmokeTests.exe'
& $compiler /nologo /warnaserror+ /optimize+ /target:exe "/out:$exe" (Join-Path $workspace 'src/ValheimEventClips.Core/CaptureBuffer.cs') (Join-Path $workspace 'src/ValheimEventClips/EncoderClient.cs') (Join-Path $workspace 'tests/EncoderSmokeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Encoder test compilation failed' }
$clip = Join-Path $output ('synthetic-' + [Guid]::NewGuid().ToString('N') + '.webp')
& $exe $EncoderPath $clip
if ($LASTEXITCODE -ne 0) { throw 'Encoder smoke test failed' }
