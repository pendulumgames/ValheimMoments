param([Parameter(Mandatory=$true)][string]$GamePath,
      [Parameter(Mandatory=$true)][string]$ProfilePath,
      [string]$EpicLootPath, [switch]$IncludeIL)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'))
try {
    $targets = @{
        Container = @('AddDefaultItems','Load','OnDestroyed')
        Inventory = @('Load','AddItem','MoveAll','MoveItemToThis')
        Humanoid = @('Pickup')
        ItemDrop = @('OnCreateNew','OnPlayerDrop','AutoStackItems','Save','SaveToZDO','LoadFromZDO')
        Pickable = @('RPC_Pick','Drop')
        PickableItem = @('RPC_Pick','Drop')
        DropOnDestroyed = @('OnDestroyed')
        Piece = @('IsPlacedByPlayer','GetCreator')
        Character = @('OnDeath')
        Ragdoll = @('SpawnLoot')
    }
    foreach ($name in $targets.Keys | Sort-Object) {
        $type = $assembly.MainModule.Types | Where-Object Name -eq $name
        if (-not $type) { throw "Missing inspected type $name" }
        foreach ($method in $type.Methods | Where-Object Name -in $targets[$name]) {
            "METHOD $($method.FullName) PARAMS $($method.Parameters -join ', ')"
            if ($IncludeIL -and $method.HasBody) { $method.Body.Instructions | ForEach-Object { "$_" } }
        }
        foreach ($nested in $type.NestedTypes | Where-Object Name -eq 'ItemData') {
            foreach ($field in $nested.Fields | Where-Object Name -in @('m_customData','m_pickedUp','m_crafterID')) { "FIELD $($field.FullName)" }
            foreach ($method in $nested.Methods | Where-Object Name -in @('Clone','Save','Load')) {
                "METHOD $($method.FullName)"
                if ($IncludeIL -and $method.HasBody) { $method.Body.Instructions | ForEach-Object { "$_" } }
            }
        }
    }
} finally { $assembly.Dispose() }
if ($EpicLootPath) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($EpicLootPath)
    try {
        $type = $assembly.MainModule.Types | Where-Object FullName -eq 'EpicLoot.PendingChestLoot'
        $method = $type.Methods | Where-Object Name -eq 'RollInternal'
        if (-not $method -or $method.Parameters.Count -ne 3 -or $method.Parameters[0].ParameterType.FullName -ne 'Container') {
            throw 'Epic Loot deferred chest signature changed.'
        }
        "METHOD $($method.FullName) PARAMS $($method.Parameters -join ', ')"
        if ($IncludeIL) { $method.Body.Instructions | ForEach-Object { "$_" } }
    } finally { $assembly.Dispose() }
}
