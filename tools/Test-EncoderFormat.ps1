param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$helper = Join-Path $workspace 'src/ValheimEventClips.Encoder/bin/Release/net48'
$test = Join-Path $workspace ('artifacts/encoder-format-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $test | Out-Null
Get-ChildItem -LiteralPath $helper -Filter '*.dll' | Copy-Item -Destination $test
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /platform:x64 /target:exe "/r:$helper/Imazen.WebP.dll" "/out:$test/EncoderFormatTests.exe" (Join-Path $workspace 'tests/EncoderFormatTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Format test compilation failed' }
& (Join-Path $test 'EncoderFormatTests.exe') (Join-Path $helper 'ValheimEventClips.Encoder.exe') $test
if ($LASTEXITCODE -ne 0) { throw 'Encoder format test failed' }
