param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [Parameter(Mandatory=$true)][string]$ProfilePath
)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$managed = Join-Path $GamePath 'valheim_Data/Managed'
$paths = @(
    (Join-Path $managed 'assembly_valheim.dll'),
    (Join-Path $managed 'UnityEngine.CoreModule.dll'),
    (Join-Path $managed 'UnityEngine.ScreenCaptureModule.dll'),
    (Join-Path $ProfilePath 'BepInEx/core/BepInEx.dll'),
    (Join-Path $ProfilePath 'BepInEx/core/0Harmony.dll'),
    (Join-Path $ProfilePath 'BepInEx/plugins/RandyKnapp-EpicLoot/EpicLoot.dll')
)
foreach ($path in $paths) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
    try {
        "ASSEMBLY $($assembly.Name.FullName)"
        "SHA256 $((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)"
        foreach ($type in $assembly.MainModule.Types) {
            if ($type.Name -match '^(Player|Character|CharacterDrop|Ragdoll|ItemDrop|Version|ScreenCapture|AsyncGPUReadback|AsyncGPUReadbackRequest|MagicItem|MagicItemEffect|ItemRarity|ItemDataExtensions|EpicLoot|LootRoller|API)$') {
                "TYPE $($type.FullName)"
                foreach ($field in $type.Fields) {
                    if ($type.IsEnum -or $field.Name -match 'boss|name|version|Rarity|Effect|Socket|Unidentified|Plugin|Version') { "FIELD $($field.FullName) $($field.Constant)" }
                }
                foreach ($nested in $type.NestedTypes) {
                    if ($nested.Name -in @('ItemData', 'SharedData')) {
                        foreach ($field in $nested.Fields) { "FIELD $($field.FullName)" }
                    }
                }
                foreach ($method in $type.Methods) {
                    if ($method.Name -match 'Die|Death|Drop|Boss|MagicItem|Rarity|Color|EffectText|Screenshot|Request|GetData|Version|Loot|Socket|Unidentified|Awake|Setup|^\.cctor$') {
                        "METHOD $($method.FullName)"
                        if ($method.HasBody -and $type.Name -in @('Player','Character','CharacterDrop','Ragdoll','Version')) {
                            foreach ($instruction in $method.Body.Instructions) {
                                if ($instruction.OpCode.Name -match '^call|^newobj|^ldftn') { "  $instruction" }
                            }
                        }
                    }
                }
            }
        }
    } finally { $assembly.Dispose() }
}
