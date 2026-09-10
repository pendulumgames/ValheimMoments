param([switch]$Restore)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Push-Location $workspace
try {
    $env:DOTNET_CLI_HOME = Join-Path $workspace 'artifacts/dotnet-home'
    foreach ($project in @('src/ValheimEventClips', 'src/ValheimEventClips.Encoder')) {
        if ($Restore) {
            & dotnet restore $project --configfile NuGet.Config --locked-mode
            if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        }
        & dotnet build $project -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    $stage = Join-Path $workspace ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
    $plugin = Join-Path $stage 'BepInEx/plugins/ValheimEventClips'
    $encoder = Join-Path $plugin 'Encoder'
    New-Item -ItemType Directory -Path $encoder -Force | Out-Null
    Copy-Item -LiteralPath 'src/ValheimEventClips/bin/Release/netstandard2.1/ValheimEventClips.dll' -Destination $plugin
    Get-ChildItem -LiteralPath 'src/ValheimEventClips.Encoder/bin/Release/net48' -File |
        Where-Object { $_.Extension -in @('.dll', '.exe', '.config') } |
        Copy-Item -Destination $encoder
    Copy-Item -LiteralPath third-party/webp -Destination (Join-Path $plugin 'Licenses') -Recurse
    Copy-Item -LiteralPath docs/RELAY-TEST.md -Destination (Join-Path $stage 'READ-ME-FIRST.md')
    Copy-Item -LiteralPath docs/PROTOTYPE-TEST.md -Destination (Join-Path $stage 'CAPTURE-TEST.md')
    Copy-Item -LiteralPath docs/DEPENDENCIES.md -Destination $plugin
    & (Join-Path $PSScriptRoot 'Test-Encoder.ps1') -EncoderPath (Join-Path $encoder 'ValheimEventClips.Encoder.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Staged encoder verification failed' }
    $hashes = foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash, $file.FullName.Substring($stage.Length + 1)
    }
    $hashes | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt')
    $zip = Join-Path $workspace 'artifacts/ValheimMoments-0.8.1-test.zip'
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    $stage | Set-Content -LiteralPath (Join-Path $workspace 'artifacts/latest-package-path.txt')
    Write-Output "Package: $zip"
    Write-Output "Staging: $stage"
} finally { Pop-Location }
