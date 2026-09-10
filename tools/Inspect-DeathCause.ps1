param([string]$GamePath, [string]$ProfilePath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'))
try {
    foreach ($type in $assembly.MainModule.Types) {
        if ($type.Name -in @('HitData','Character','Player','Localization')) {
            "TYPE $($type.FullName)"
            foreach ($field in $type.Fields | Where-Object Name -match 'lastHit|attacker|hitType|m_name') { "FIELD $($field.FullName) attributes=$($field.Attributes)" }
            foreach ($nested in $type.NestedTypes | Where-Object Name -eq 'HitType') { foreach ($f in $nested.Fields) { "ENUM $($f.Name)=$($f.Constant)" } }
            foreach ($method in $type.Methods) {
                if ($method.Name -in @('GetAttacker','GetHoverName','GetPlayerName','Localize') -or
                    ($method.HasBody -and ($method.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and "$($_.Operand)" -match 'm_lastHit' }))) {
                    "METHOD $($method.FullName)"
                    foreach ($instruction in $method.Body.Instructions) { "$instruction" }
                }
            }
        }
    }
} finally { $assembly.Dispose() }
