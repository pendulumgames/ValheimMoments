$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
[xml]$paths = Get-Content -LiteralPath (Join-Path $workspace 'local.props')
$profilePath = [string]$paths.Project.PropertyGroup.ProfilePath
$managerPath = Join-Path $profilePath 'BepInEx/plugins/Azumatt-Official_BepInEx_ConfigurationManager/ConfigurationManager/ConfigurationManager.dll'
if (-not (Test-Path -LiteralPath $managerPath)) { throw 'Install Configuration Manager in the configured test profile to check its attribute contract.' }
Add-Type -LiteralPath (Join-Path $profilePath 'BepInEx/core/Mono.Cecil.dll')
$manager = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($managerPath)
$plugin = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $workspace 'src/ValheimMoments/bin/Release/netstandard2.1/ValheimMoments.dll'))
try {
    $entry = $manager.MainModule.Types | Where-Object { $_.Name -eq 'SettingEntryBase' }
    $attributes = $entry.Methods | Where-Object { $_.Name -eq 'SetFromAttributes' }
    $expected = @($attributes.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object { $_.Operand })
    if ($expected -notcontains 'ConfigurationManagerAttributes') { throw 'Manager attribute name contract changed; inspect SetFromAttributes.' }
    $hostType = $plugin.MainModule.Types | Where-Object { $_.Name -eq 'HostConfiguration' }
    $tags = $hostType.NestedTypes | Where-Object { $_.Name -eq 'ConfigurationManagerAttributes' }
    if (-not $tags) { throw 'Plugin tags do not use the name recognized by Configuration Manager.' }
    foreach ($name in @('ReadOnly', 'Browsable', 'Category', 'Order', 'CustomDrawer')) {
        if (-not ($entry.Properties | Where-Object { $_.Name -eq $name })) { throw "Manager missing $name property." }
        if (-not ($tags.Fields | Where-Object { $_.Name -eq $name -and $_.IsPublic })) { throw "Plugin missing public $name field." }
    }
    'PASS: installed Configuration Manager recognizes attribute name and five presentation fields (live visual check still required).'
} finally { $manager.Dispose(); $plugin.Dispose() }
