param([Parameter(Mandatory=$true)][string]$ProfilePath)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before preserving discovery history.' }
$profile = (Resolve-Path -LiteralPath $ProfilePath).Path
$pluginRoot = Join-Path $profile 'BepInEx/plugins'
if (-not (Test-Path -LiteralPath $pluginRoot -PathType Container)) { throw 'Choose the mod profile containing BepInEx/plugins.' }
$destination = Join-Path $profile 'BepInEx/config/ValheimMoments/Discoveries'
$backups = Join-Path $profile ('BepInEx/config/ValheimMoments/DiscoveryBackups/' + [Guid]::NewGuid().ToString('N'))
function Read-Journal([string]$Path) {
 if ((Get-Item -LiteralPath $Path).Length -gt 262144) { throw "Oversized discovery journal: $Path" }
 $reader = New-Object IO.BinaryReader([IO.File]::OpenRead($Path))
 $keys = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
 try {
  if ($reader.ReadInt32() -ne 0x31444d56) { throw "Unknown discovery journal: $Path" }
  $count = $reader.ReadUInt16()
  if ($count -gt 256) { throw "Too many discoveries: $Path" }
  $utf8 = New-Object Text.UTF8Encoding($false, $true)
  for ($i=0; $i -lt $count; $i++) {
   $length = $reader.ReadUInt16()
   if ($length -lt 1 -or $length -gt 768) { throw 'Invalid discovery key length' }
   $bytes = $reader.ReadBytes($length)
   if ($bytes.Length -ne $length) { throw 'Truncated discovery journal' }
   $key = $utf8.GetString($bytes)
   if ([string]::IsNullOrWhiteSpace($key) -or $key.Length -gt 192 -or -not $keys.Add($key)) { throw 'Invalid/duplicate discovery key' }
  }
  if ($reader.BaseStream.Position -ne $reader.BaseStream.Length) { throw 'Trailing discovery data' }
 } finally { $reader.Dispose() }
 return ,$keys
}
$pending = @{}
foreach ($source in Get-ChildItem -LiteralPath $pluginRoot -Filter '*.bin' -Recurse -File) {
 if ($source.Directory.Name -ne 'Discoveries' -or $source.Directory.Parent.Name -ne 'State' -or $source.Name -notmatch '^[0-9A-F]{16}-[0-9A-F]{16}\.bin$') { continue }
 $target = Join-Path $destination $source.Name
 if (-not $pending.ContainsKey($target)) {
  $pending[$target] = if (Test-Path -LiteralPath $target) { Read-Journal $target } else { New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal) }
 }
 $keys = Read-Journal $source.FullName
 foreach ($key in $keys) { $null = $pending[$target].Add($key) }
 if ($pending[$target].Count -gt 256) { throw 'Merged journal exceeds capacity; originals preserved. Resolve before upgrading.' }
 New-Item -ItemType Directory -Path $backups -Force | Out-Null
 Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $backups ([Guid]::NewGuid().ToString('N') + '-' + $source.Name))
}
$existing = @(Get-ChildItem -LiteralPath $destination -File -ErrorAction SilentlyContinue).Count
$new = @($pending.Keys | Where-Object { -not (Test-Path -LiteralPath $_) }).Count
if ($existing + $new -gt 128) { throw 'Discovery history exceeds context capacity; originals preserved.' }
foreach ($target in $pending.Keys) {
 New-Item -ItemType Directory -Path $destination -Force | Out-Null
 $temporary = $target + '.pending'
 $stream = New-Object IO.FileStream($temporary, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
 $writer = New-Object IO.BinaryWriter($stream)
 try {
  $writer.Write([int]0x31444d56); $writer.Write([uint16]$pending[$target].Count)
  foreach ($key in @($pending[$target] | Sort-Object -CaseSensitive)) { $bytes = [Text.Encoding]::UTF8.GetBytes($key); $writer.Write([uint16]$bytes.Length); $writer.Write($bytes) }
  $writer.Flush(); $stream.Flush($true)
 } finally { $writer.Dispose() }
 if (Test-Path -LiteralPath $target) { [IO.File]::Replace($temporary, $target, [NullString]::Value) } else { [IO.File]::Move($temporary, $target) }
}
Write-Output "Preserved $($pending.Count) discovery journals in $destination. Original plugin files remain untouched. You can now update the mod."
