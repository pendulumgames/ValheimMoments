param([string]$PackagePath)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $workspace 'artifacts/dotnet-home'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-StreamHash($Stream, [string]$Algorithm, [switch]$Base64) {
    $hasher = [Security.Cryptography.HashAlgorithm]::Create($Algorithm)
    try {
        $bytes = $hasher.ComputeHash($Stream)
        if ($Base64) { return [Convert]::ToBase64String($bytes) }
        return [BitConverter]::ToString($bytes).Replace('-', '')
    } finally { $hasher.Dispose() }
}
function Get-EntryHash($Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing archive entry: $Name" }
    $stream = $entry.Open()
    try { return Get-StreamHash $stream 'SHA256' } finally { $stream.Dispose() }
}

$manifest = Get-Content -LiteralPath (Join-Path $workspace 'release/manifest.json') -Raw | ConvertFrom-Json
if (-not $PackagePath) { $PackagePath = Join-Path $workspace ('artifacts/' + $manifest.name + '-' + $manifest.version_number + '.zip') }
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$lock = Get-Content -LiteralPath (Join-Path $workspace 'src/ValheimMoments.Encoder/packages.lock.json') -Raw | ConvertFrom-Json
$dependencies = $lock.dependencies.'.NETFramework,Version=v4.8'
$nativeId = 'Imazen.WebP.NativeRuntime.win-x64'
$wrapperId = 'Imazen.WebP'
$packages = @(
    @{ Id = $nativeId; Dependency = $dependencies.$nativeId; Prefix = 'runtimes/win-x64/native/'; Files = @('libwebp.dll','libsharpyuv.dll','libwebpdemux.dll','libwebpmux.dll') },
    @{ Id = $wrapperId; Dependency = $dependencies.$wrapperId; Prefix = 'lib/net48/'; Files = @('Imazen.WebP.dll') }
)
$rows = New-Object 'Collections.Generic.List[string]'
$release = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    # Version comes from the inspected archive, not an assumed file name.
    $entry = $release.GetEntry('manifest.json')
    if ($null -eq $entry) { throw 'Release manifest missing' }
    $reader = New-Object IO.StreamReader($entry.Open())
    try { $releaseManifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    foreach ($package in $packages) {
        $id = $package.Id.ToLowerInvariant()
        $version = $package.Dependency.resolved
        if (-not $version) { throw 'Dependency missing from encoder lockfile' }
        $nupkg = Join-Path $workspace "artifacts/packages/$id/$version/$id.$version.nupkg"
        $stream = [IO.File]::OpenRead($nupkg)
        try { $packageHash = Get-StreamHash $stream 'SHA512' -Base64 } finally { $stream.Dispose() }
        # Signed packages have separate archive and unsigned-content hashes.
        # https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs
        $archiveChecksum = [IO.File]::ReadAllText($nupkg + '.sha512').Trim()
        if ($packageHash -cne $archiveChecksum) { throw "NuGet archive checksum mismatch: $id" }
        $metadata = Get-Content -LiteralPath (Join-Path (Split-Path $nupkg -Parent) '.nupkg.metadata') -Raw | ConvertFrom-Json
        if ($metadata.contentHash -cne $package.Dependency.contentHash) { throw "NuGet restored content hash differs from lockfile: $id" }
        & dotnet nuget verify $nupkg --all --configfile (Join-Path $workspace 'NuGet.Config') --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "NuGet signature verification failed: $id" }
        $original = [IO.Compression.ZipFile]::OpenRead($nupkg)
        try {
            foreach ($name in $package.Files) {
                $upstream = Get-EntryHash $original ($package.Prefix + $name)
                $bundled = Get-EntryHash $release ('BepInEx/plugins/ValheimMoments/Encoder/' + $name)
                if ($upstream -cne $bundled) { throw "Bundled DLL differs from pinned NuGet original: $name" }
                $rows.Add("| $name | $($package.Id) $version | $bundled |")
                Write-Output "PASS: $name is identical to its pinned NuGet original."
            }
        } finally { $original.Dispose() }
    }
    foreach ($name in @('Imazen-WebP-MIT.txt','libwebp-COPYING.txt','libwebp-PATENTS.txt','libwebp-AUTHORS.txt','PROVENANCE.md')) {
        $bundled = Get-EntryHash $release ('BepInEx/plugins/ValheimMoments/Licenses/' + $name)
        $localHash = (Get-FileHash -LiteralPath (Join-Path $workspace ('third-party/webp/' + $name)) -Algorithm SHA256).Hash
        if ($bundled -cne $localHash) { throw "License/provenance file differs from repository: $name" }
    }
} finally { $release.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
$nativeVersion = $dependencies.$nativeId.resolved
$wrapperVersion = $dependencies.$wrapperId.resolved
$report = @(
    '# Binary provenance verification',
    '',
    "Package: $([IO.Path]::GetFileName($PackagePath)) (manifest version $($releaseManifest.version_number))",
    "Verified UTC: $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))",
    "Archive SHA-256: $zipHash",
    '',
    'Each DLL below matches the corresponding entry in its original NuGet archive.',
    'Both NuGet archives pass dotnet nuget verify --all and match their restored SHA-512 archive checksums.',
    'NuGet restore metadata content hashes match the encoder lockfile (signed-package content hashes differ from full archive hashes).',
    'Bundled license and provenance files match the repository copies.',
    '',
    '| DLL | Original package | SHA-256 |',
    '| --- | --- | --- |'
) + $rows.ToArray() + @(
    '',
    "[Native NuGet package](https://www.nuget.org/packages/$nativeId/$nativeVersion)",
    "[Wrapper NuGet package](https://www.nuget.org/packages/$wrapperId/$wrapperVersion)",
    "[Native binary release](https://github.com/imazen/libwebp-net/releases/tag/native-v$nativeVersion)",
    "[Native source](https://github.com/webmproject/libwebp/tree/v$nativeVersion)",
    '',
    'This report verifies binary provenance, not live gameplay, moderation approval or absence of vulnerabilities.'
)
$reportPath = Join-Path $workspace 'artifacts/BINARY-PROVENANCE.md'
[IO.File]::WriteAllLines($reportPath, [string[]]$report, (New-Object Text.UTF8Encoding $false))
Write-Output "Provenance report: $reportPath"
