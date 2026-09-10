param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Push-Location $workspace
try {
    $env:DOTNET_CLI_HOME = Join-Path $workspace 'artifacts/dotnet-home'
    if (-not $SkipRestore) {
        foreach ($project in @('src/ValheimMoments','src/ValheimMoments.Encoder','tests/DeathTests.csproj')) {
            $arguments = @('restore', $project, '--configfile', 'NuGet.Config')
            if ($project -ne 'tests/DeathTests.csproj') { $arguments += '--locked-mode' }
            & dotnet @arguments
            if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        }
    }
    & (Join-Path $PSScriptRoot 'Test-Core.ps1')
    & (Join-Path $PSScriptRoot 'Test-Discord.ps1')
    & dotnet build tests/DeathTests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Event test build failed' }
    & (Join-Path $workspace 'tests/bin/Release/net48/DeathTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Event tests failed' }
    & (Join-Path $PSScriptRoot 'Build-Thunderstore.ps1')

    # Verify documentation against actual binding keys, without reading any user config.
    $source = [IO.File]::ReadAllText((Join-Path $workspace 'src/ValheimMoments/Plugin.cs'))
    $reference = [IO.File]::ReadAllText((Join-Path $workspace 'docs/CONFIGURATION.md'))
    $keys = @([regex]::Matches($source, '(?:Config\.)?Bind\("[^"]+", "([^"]+)"') | ForEach-Object { $_.Groups[1].Value }) +
        @([regex]::Matches($source, 'Setting\("([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    foreach ($key in $keys) {
        if (-not $reference.Contains('| ' + $key + ' |')) { throw "Undocumented configuration key: $key" }
    }
    Write-Output "PASS: all $($keys.Count) configuration entries documented."
    & (Join-Path $PSScriptRoot 'Test-BinaryProvenance.ps1')
    Write-Output 'PASS: release checks complete. Live game/co-op tests remain separate; nothing was installed or published.'
} finally { Pop-Location }
