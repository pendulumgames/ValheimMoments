param([switch]$Restore)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Push-Location $workspace
try {
    $env:DOTNET_CLI_HOME = Join-Path $workspace 'artifacts/dotnet-home'
    foreach ($project in @('src/ValheimMoments', 'src/ValheimMoments.Encoder')) {
        if ($Restore) {
            & dotnet restore $project --configfile NuGet.Config --locked-mode
            if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        }
        & dotnet build $project -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    $stage = Join-Path $workspace ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
    $plugin = Join-Path $stage 'BepInEx/plugins/ValheimMoments'
    $encoder = Join-Path $plugin 'Encoder'
    New-Item -ItemType Directory -Path $encoder -Force | Out-Null
    Copy-Item -LiteralPath 'src/ValheimMoments/bin/Release/netstandard2.1/ValheimMoments.dll' -Destination $plugin
    Get-ChildItem -LiteralPath 'src/ValheimMoments.Encoder/bin/Release/net48' -File |
        Where-Object { $_.Extension -in @('.dll', '.exe', '.config') } |
        Copy-Item -Destination $encoder
    Copy-Item -LiteralPath third-party/webp -Destination (Join-Path $plugin 'Licenses') -Recurse
    Copy-Item -LiteralPath docs/RELAY-TEST.md -Destination (Join-Path $stage 'READ-ME-FIRST.md')
    Copy-Item -LiteralPath docs/NATURAL-LOOT-TEST.md -Destination (Join-Path $stage 'NATURAL-LOOT-TEST.md')
    Copy-Item -LiteralPath docs/CONFIGURATION.md -Destination (Join-Path $stage 'CONFIGURATION.md')
    Copy-Item -LiteralPath release/CHANGELOG.md -Destination (Join-Path $stage 'CHANGELOG.md')
    Copy-Item -LiteralPath LICENSE -Destination (Join-Path $stage 'LICENSE')
    Copy-Item -LiteralPath docs/PROTOTYPE-TEST.md -Destination (Join-Path $stage 'CAPTURE-TEST.md')
    Copy-Item -LiteralPath docs/DEPENDENCIES.md -Destination $plugin
    & (Join-Path $PSScriptRoot 'Test-Encoder.ps1') -EncoderPath (Join-Path $encoder 'ValheimMoments.Encoder.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Staged encoder verification failed' }
    $hashes = foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash, $file.FullName.Substring($stage.Length + 1)
    }
    $hashes | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt')
    $zip = Join-Path $workspace 'artifacts/ValheimMoments-0.17.0-test.zip'
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    $stage | Set-Content -LiteralPath (Join-Path $workspace 'artifacts/latest-package-path.txt')
    Write-Output "Package: $zip"
    Write-Output "Staging: $stage"
} finally { Pop-Location }
