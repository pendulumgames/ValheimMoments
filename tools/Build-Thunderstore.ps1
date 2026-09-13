param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Push-Location $workspace
try {
    & (Join-Path $PSScriptRoot 'Build-Prototype.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & (Join-Path $PSScriptRoot 'Test-EncoderFormat.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Encoder format checks failed' }
    $prototype = (Get-Content artifacts/latest-package-path.txt -Raw).Trim()
    $stage = Join-Path $workspace ('artifacts/thunderstore-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in @('manifest.json','README.md','CHANGELOG.md','icon.png')) {
        Copy-Item -LiteralPath (Join-Path $workspace ('release/' + $name)) -Destination $stage
    }
    if (Test-Path -LiteralPath (Join-Path $workspace 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination $stage }
    Copy-Item -LiteralPath (Join-Path $workspace 'tools/Preserve-DiscoveryHistory.ps1') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $prototype 'BepInEx') -Destination $stage -Recurse
    $manifest = Get-Content (Join-Path $stage 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.name -notmatch '^[A-Za-z0-9_]{1,128}$' -or $manifest.version_number -notmatch '^\d+\.\d+\.\d+$' -or $manifest.description.Length -gt 250) { throw 'Invalid manifest' }
    $dll = Join-Path $stage 'BepInEx/plugins/ValheimMoments/ValheimMoments.dll'
    $version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version
    if ($version.ToString(3) -ne $manifest.version_number) { throw 'Plugin/manifest version mismatch' }
    Add-Type -AssemblyName System.Drawing
    $icon = [Drawing.Image]::FromFile((Join-Path $stage 'icon.png'))
    try { if ($icon.Width -ne 256 -or $icon.Height -ne 256 -or $icon.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'Icon must be a 256x256 PNG' } } finally { $icon.Dispose() }
    $allowedBinaries = @('ValheimMoments.dll','ValheimMoments.Encoder.exe','Imazen.WebP.dll','libwebp.dll','libwebpmux.dll','libwebpdemux.dll','libsharpyuv.dll')
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        if ($file.Extension -in @('.dll','.exe') -and $file.Name -notin $allowedBinaries) { throw "Unexpected binary: $($file.Name)" }
        if ($file.Extension -in @('.cfg','.webp','.pdb') -or $file.Name -in @('local.props','LogOutput.log')) { throw 'Private/test file in release' }
        if ($file.Extension -in @('.md','.json','.txt','.config')) {
            if (Select-String -LiteralPath $file.FullName -Pattern 'https://(?:\w+\.)?discord(?:app)?\.com/api/(?:v\d+/)?webhooks/\d+/[A-Za-z0-9_-]+' -Quiet) { throw 'Webhook-like secret found in release' }
        }
    }
    foreach ($name in $allowedBinaries) {
        if (@(Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object Name -eq $name).Count -ne 1) { throw "Missing or duplicated binary: $name" }
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $workspace ('artifacts/' + $manifest.name + '-' + $manifest.version_number + '.zip')
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Create($zip)
    $writer = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
            $relative = $file.FullName.Substring($stage.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $writer.Dispose(); $stream.Dispose() }
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($name in @('manifest.json','README.md','icon.png','CHANGELOG.md')) { if ($null -eq $archive.GetEntry($name)) { throw "Missing root entry: $name" } }
        if ($null -eq $archive.GetEntry('BepInEx/plugins/ValheimMoments/Encoder/ValheimMoments.Encoder.exe')) { throw 'Encoder folder was not preserved' }
        Write-Output "Validated $($archive.Entries.Count) ZIP entries; no configs, footage, game DLLs or webhook URLs."
    } finally { $archive.Dispose() }
    $stage | Set-Content artifacts/latest-thunderstore-path.txt
    Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Select-Object Hash,Path
    Write-Output "Thunderstore package: $zip ($((Get-Item $zip).Length) bytes)"
} finally { Pop-Location }
