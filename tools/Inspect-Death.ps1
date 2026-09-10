param([Parameter(Mandatory=$true)][string]$GamePath, [Parameter(Mandatory=$true)][string]$ProfilePath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$path = Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
try {
    "SHA256 $((Get-FileHash -LiteralPath $path).Hash)"
    foreach ($type in $assembly.MainModule.Types | Where-Object Name -in @('Player','Character')) {
        foreach ($field in $type.Fields | Where-Object Name -match 'localPlayer|m_dead|m_nview') { "FIELD $($field.FullName)" }
        foreach ($method in $type.Methods | Where-Object Name -in @('OnDeath','IsDead','GetPlayerName')) {
            "METHOD $($method.FullName) attributes=$($method.Attributes)"
            foreach ($instruction in $method.Body.Instructions) { "$instruction" }
        }
    }
} finally { $assembly.Dispose() }
