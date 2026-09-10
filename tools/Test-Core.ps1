$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$output = Join-Path $workspace 'artifacts/tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install a .NET SDK or the Windows .NET Framework C# compiler.' }
$exe = Join-Path $output 'CaptureBufferTests.exe'
& $compiler /nologo /warnaserror+ /optimize+ /target:exe "/out:$exe" (Join-Path $workspace 'src/ValheimMoments.Core/CaptureBuffer.cs') (Join-Path $workspace 'tests/CaptureBufferTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
