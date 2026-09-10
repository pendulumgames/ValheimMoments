param([string]$GamePath, [string]$ProfilePath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'))
try {
    foreach ($type in $assembly.MainModule.Types | Where-Object Name -in @('CharacterDrop', 'Ragdoll', 'ItemDrop')) {
        "TYPE $($type.FullName)"
        foreach ($field in $type.Fields) { "FIELD $($field.FullName) $($field.Attributes)" }
        foreach ($nested in $type.NestedTypes) {
            foreach ($field in $nested.Fields | Where-Object Name -in @('m_stack','m_shared')) { "FIELD $($field.FullName) $($field.Attributes)" }
            foreach ($shared in $nested.NestedTypes | Where-Object Name -eq 'SharedData') {
                foreach ($field in $shared.Fields | Where-Object Name -eq 'm_name') { "FIELD $($field.FullName) $($field.Attributes)" }
            }
        }
        foreach ($method in $type.Methods | Where-Object Name -in @('GenerateDropList','OnDeath','DropItems','SaveLootList','SpawnLoot')) {
            "METHOD $($method.FullName) PARAMS $($method.Parameters -join ', ')"
            foreach ($instruction in $method.Body.Instructions) { "$instruction" }
        }
    }
} finally { $assembly.Dispose() }
