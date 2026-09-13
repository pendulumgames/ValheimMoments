$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$output = Join-Path $workspace 'artifacts/tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe = Join-Path $output 'CinematicTests.exe'
& $compiler /nologo /warnaserror+ /optimize+ /target:exe "/out:$exe" (Join-Path $workspace 'src/ValheimMoments/CinematicPacket.cs') (Join-Path $workspace 'tests/CinematicTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cinematic test compilation failed' }
& $exe $output
if ($LASTEXITCODE -ne 0) { throw 'Cinematic tests failed' }
$presentation = Join-Path $output 'CinematicPresentationTests.exe'
& $compiler /nologo /warnaserror+ /optimize+ /target:exe /r:System.Drawing.dll "/out:$presentation" (Join-Path $workspace 'src/ValheimMoments.Encoder/CinematicTitle.cs') (Join-Path $workspace 'tests/CinematicPresentationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cinematic presentation test compilation failed' }
& $presentation $output
if ($LASTEXITCODE -ne 0) { throw 'Cinematic presentation tests failed' }
