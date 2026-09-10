param([Parameter(Mandatory=$true)][string]$GamePath,
      [Parameter(Mandatory=$true)][string]$ProfilePath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'))
try {
    # Read-only IL evidence for the owner-side roster and ordering of credit RPCs.
    foreach ($type in $assembly.MainModule.Types | Where-Object Name -in @('Character','Game')) {
        foreach ($method in $type.Methods | Where-Object Name -in @('OnDeath','RegisterKill','RPC_RegisterKill')) {
            "METHOD $($method.FullName)"
            if ($method.HasBody) { $method.Body.Instructions | ForEach-Object { "$_" } }
        }
    }
} finally { $assembly.Dispose() }
