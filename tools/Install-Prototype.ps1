param([Parameter(Mandatory=$true)][string]$ProfilePath)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before installing the prototype.' }
$stage = (Get-Content -LiteralPath (Join-Path $workspace 'artifacts/latest-package-path.txt') -Raw).Trim()
$stage = [IO.Path]::GetFullPath($stage)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $stage.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package must be inside workspace artifacts.' }
$profile = (Resolve-Path -LiteralPath $ProfilePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $profile 'BepInEx/core/BepInEx.dll'))) { throw 'Not a BepInEx profile.' }
$source = Join-Path $stage 'BepInEx/plugins/ValheimEventClips'
$destination = Join-Path $profile 'BepInEx/plugins/ValheimEventClips'
if (Test-Path -LiteralPath $destination) {
    $backupFolder = Join-Path $workspace 'artifacts/backups'
    New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
    $backup = Join-Path $backupFolder ('ValheimEventClips-' + [Guid]::NewGuid().ToString('N') + '.zip')
    Compress-Archive -LiteralPath $destination -DestinationPath $backup
    Write-Output "Previous plugin backed up to $backup"
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
    $relative = $file.FullName.Substring($source.Length + 1)
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
        throw "Installed file hash mismatch: $relative"
    }
}
Write-Output "Installed and hash-verified: $destination"
$configPath = Join-Path $profile 'BepInEx/config/local.valheimeventclips.cfg'
if ((Test-Path -LiteralPath $configPath) -and -not (Select-String -LiteralPath $configPath -Pattern '^\[Discord\]' -Quiet)) {
    @'

[Discord]
# Enter your webhook locally. Do not share this configuration file.
Enabled = false
WebhookURL =
Username = Valheim Moments
UploadClips = true
SaveLocalCopy = true
MaxUploadMiB = 10
'@ | Add-Content -LiteralPath $configPath -Encoding UTF8
    Write-Output 'Added disabled Discord configuration; enter the webhook locally to test.'
}
Write-Output 'Launch this profile modded, wait five seconds in-game, then press F10.'
