param([Parameter(Mandatory=$true)][string]$GamePath, [Parameter(Mandatory=$true)][string]$ProfilePath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'))
try {
    foreach ($type in $assembly.MainModule.Types) {
        if ($type.Name -notin @('Character','StatusEffect','SE_Burning','SE_Poison')) { continue }
        foreach ($field in $type.Fields | Where-Object Name -match 'm_character|DamageLeft|m_damageLeft|m_lastHit') {
            "FIELD $($field.FullName)"
        }
        foreach ($method in $type.Methods) {
            if (($type.Name -eq 'Character' -and $method.Name -in @('RPC_Damage','ApplyDamage','AddFireDamage','AddSpiritDamage','AddPoisonDamage')) -or
                ($type.Name -in @('SE_Burning','SE_Poison') -and $method.Name -in @('AddFireDamage','AddSpiritDamage','AddDamage','UpdateStatusEffect'))) {
                "METHOD $($method.FullName)"
                foreach ($instruction in $method.Body.Instructions) { "$instruction" }
            }
        }
    }
} finally { $assembly.Dispose() }
