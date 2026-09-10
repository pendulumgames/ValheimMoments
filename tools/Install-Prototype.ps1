param(
    [Parameter(Mandatory=$true)][string]$ProfilePath,
    [string]$PreviousPluginDirectory,
    [string]$PreviousConfigPath
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before installing the prototype.' }
$stage = (Get-Content -LiteralPath (Join-Path $workspace 'artifacts/latest-package-path.txt') -Raw).Trim()
$stage = [IO.Path]::GetFullPath($stage)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $stage.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package must be inside workspace artifacts.' }
$profile = (Resolve-Path -LiteralPath $ProfilePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $profile 'BepInEx/core/BepInEx.dll'))) { throw 'Not a BepInEx profile.' }
$source = Join-Path $stage 'BepInEx/plugins/ValheimMoments'
$destination = Join-Path $profile 'BepInEx/plugins/ValheimMoments'
$configPath = Join-Path $profile 'BepInEx/config/local.valheimmoments.cfg'
$migration = $null
if ($PreviousPluginDirectory -or $PreviousConfigPath) {
    $pluginRoot = [IO.Path]::GetFullPath((Join-Path $profile 'BepInEx/plugins')) + '\'
    $configRoot = [IO.Path]::GetFullPath((Join-Path $profile 'BepInEx/config')) + '\'
    if ($PreviousPluginDirectory) {
        $PreviousPluginDirectory = (Resolve-Path -LiteralPath $PreviousPluginDirectory).Path
        if (-not $PreviousPluginDirectory.StartsWith($pluginRoot,[StringComparison]::OrdinalIgnoreCase) -or $PreviousPluginDirectory -eq $destination) { throw 'Previous plugin must be a different folder inside this profile plugins directory.' }
        if (Test-Path -LiteralPath $destination) { throw 'New plugin folder already exists; resolve the two installs before migrating.' }
    }
    if ($PreviousConfigPath) {
        $PreviousConfigPath = (Resolve-Path -LiteralPath $PreviousConfigPath).Path
        if (-not $PreviousConfigPath.StartsWith($configRoot,[StringComparison]::OrdinalIgnoreCase) -or $PreviousConfigPath -eq $configPath) { throw 'Previous config must be a different file inside this profile config directory.' }
        if (Test-Path -LiteralPath $configPath) { throw 'New config already exists; refusing to overwrite it.' }
    }
    $migration = [IO.Path]::GetFullPath((Join-Path $workspace ('artifacts/backups/ValheimMoments-migration-' + [Guid]::NewGuid().ToString('N'))))
    if (-not $migration.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Migration backup outside workspace artifacts.' }
    New-Item -ItemType Directory -Path $migration -Force | Out-Null
}
if (Test-Path -LiteralPath $destination) {
    $backupFolder = Join-Path $workspace 'artifacts/backups'
    New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
    $backup = Join-Path $backupFolder ('ValheimMoments-' + [Guid]::NewGuid().ToString('N') + '.zip')
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
if ($PreviousConfigPath) {
    Copy-Item -LiteralPath $PreviousConfigPath -Destination $configPath
    if ((Get-FileHash -LiteralPath $PreviousConfigPath).Hash -ne (Get-FileHash -LiteralPath $configPath).Hash) { throw 'Migrated config hash mismatch' }
}
if ($PreviousPluginDirectory) {
    $clips = Join-Path $PreviousPluginDirectory 'Clips'
    if (Test-Path -LiteralPath $clips) { Copy-Item -LiteralPath $clips -Destination $destination -Recurse }
    # Both absolute paths were checked before any recursive move. Preserve the whole
    # previous install outside the active profile so two plugin identities cannot load.
    Move-Item -LiteralPath $PreviousPluginDirectory -Destination (Join-Path $migration 'PreviousPlugin')
}
if ($PreviousConfigPath) { Move-Item -LiteralPath $PreviousConfigPath -Destination (Join-Path $migration 'PreviousConfig.cfg') }
if ($migration) { Write-Output 'Migration complete: settings and clips preserved; previous install archived outside the profile.' }
if ((Test-Path -LiteralPath $configPath) -and -not (Select-String -LiteralPath $configPath -Pattern '^\[Discord\]' -Quiet)) {
    @'

[Discord]
# Enter your webhook locally. Do not share this configuration file.
Enabled = false
WebhookURL =
Username = Valheim Moments
UploadClips = true
SaveLocalCopy = false
MaxUploadMiB = 10
'@ | Add-Content -LiteralPath $configPath -Encoding UTF8
    Write-Output 'Added disabled Discord configuration; enter the webhook locally to test.'
}
Write-Output 'Launch this profile modded, wait five seconds in-game, then press F10.'
