$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$output = Join-Path $workspace 'artifacts/tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe = Join-Path $output 'DiscordTests.exe'
& $compiler /nologo /warnaserror+ /optimize+ /target:exe /r:System.Net.Http.dll /r:System.Runtime.Serialization.dll "/out:$exe" (Join-Path $workspace 'src/ValheimEventClips/DiscordWebhook.cs') (Join-Path $workspace 'src/ValheimEventClips/DiscordRouting.cs') (Join-Path $workspace 'tests/DiscordTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Discord test compilation failed' }
& $exe $output
if ($LASTEXITCODE -ne 0) { throw 'Discord tests failed' }
