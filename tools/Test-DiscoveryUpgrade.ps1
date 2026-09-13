$ErrorActionPreference = 'Stop'
# Synthetic profiles only: an unrelated running game cannot touch these fixtures.
function Get-Process { param($Name, $ErrorAction) }
$workspace = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $workspace ('artifacts/tests/discovery-upgrade-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'BepInEx/plugins/Pendulum-Valheim_Moments/ValheimMoments/State/Discoveries'
New-Item -ItemType Directory -Path $source -Force | Out-Null
$filename = '0000000000000001-0000000000000002.bin'
$path = Join-Path $source $filename
$writer = New-Object IO.BinaryWriter([IO.File]::Create($path))
try { $writer.Write([int]0x31444d56); $writer.Write([uint16]1); $bytes = [Text.Encoding]::UTF8.GetBytes('biome:1/'); $writer.Write([uint16]$bytes.Length); $writer.Write($bytes) } finally { $writer.Dispose() }
$before = (Get-FileHash -LiteralPath $path).Hash
& (Join-Path $PSScriptRoot 'Preserve-DiscoveryHistory.ps1') -ProfilePath $fixture
$target = Join-Path $fixture ('BepInEx/config/ValheimMoments/Discoveries/' + $filename)
if ((Get-FileHash -LiteralPath $target).Hash -ne $before) { throw 'Preservation mismatch' }
& (Join-Path $PSScriptRoot 'Preserve-DiscoveryHistory.ps1') -ProfilePath $fixture
if ((Get-FileHash -LiteralPath $target).Hash -ne $before -or (Get-FileHash -LiteralPath $path).Hash -ne $before) { throw 'Idempotence/original preservation failed' }
[IO.File]::WriteAllText($target, 'broken')
$rejected = $false
try { & (Join-Path $PSScriptRoot 'Preserve-DiscoveryHistory.ps1') -ProfilePath $fixture } catch { $rejected = $true }
if (-not $rejected -or [IO.File]::ReadAllText($target) -ne 'broken' -or (Get-FileHash -LiteralPath $path).Hash -ne $before) { throw 'Corruption must fail without overwriting data' }
Write-Output 'PASS: pre-upgrade preservation, idempotence, original retention and corrupt-destination refusal.'
