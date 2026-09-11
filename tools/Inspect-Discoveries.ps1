param([Parameter(Mandatory=$true)][string]$GamePath,
      [Parameter(Mandatory=$true)][string]$ProfilePath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'))
try {
    foreach ($type in $assembly.MainModule.Types) {
        if ($type.Name -in @('Player','PlayerProfile','ZNet','World','BiomeSector','AltBiome','Trader','Game')) {
            foreach ($method in $type.Methods | Where-Object { $_.Name -match 'KnownBiome|KnownLocation|CurrentBiome|UpdateBiome|GetWorld|GetPlayerID|GetPlayerProfile|GetName|DiscoverClosestLocation' -or ($type.Name -eq 'Trader' -and $_.Name -eq 'Update') }) {
                "METHOD $($method.FullName)"
                if ($method.HasBody -and ($method.Name -match 'KnownBiome|KnownLocation|UpdateBiome|GetWorldUID|GetPlayerID|DiscoverClosestLocation' -or $type.Name -in @('Trader','BiomeSector'))) { $method.Body.Instructions | ForEach-Object { "$_" } }
            }
            if ($type.Name -in @('BiomeSector','AltBiome','World','Trader')) { $type.Fields | ForEach-Object { "FIELD $($_.FullName)" } }
        }
        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                if ($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.Name -in @('AddKnownBiome','AddKnownLocationName')) {
                    "CALLER $($method.FullName) -> $($instruction.Operand.FullName)"
                }
            }
        }
    }
} finally { $assembly.Dispose() }
