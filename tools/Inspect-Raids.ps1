param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
      [string]$ProfilePath = 'C:\Users\mecra\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Test')
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'))
try {
    foreach ($type in $assembly.MainModule.Types | Where-Object { $_.Name -in @('RandEventSystem','RandomEvent') }) {
        $type.Fields | ForEach-Object { "FIELD $($_.FullName)" }
        foreach ($method in $type.Methods) {
            "METHOD $($method.FullName)"
            if ($method.HasBody -and $method.Name -match 'Update|Event|Active|Inside|Player') {
                $method.Body.Instructions | ForEach-Object { "$_" }
            }
        }
    }
} finally { $assembly.Dispose() }
