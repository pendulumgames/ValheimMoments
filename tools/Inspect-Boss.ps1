param([string]$GamePath, [string]$ProfilePath)
$ErrorActionPreference = 'Stop'
Add-Type -LiteralPath (Join-Path $ProfilePath 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'valheim_Data/Managed/assembly_valheim.dll'))
try {
 foreach ($type in $assembly.MainModule.Types) {
  if ($type.Name -in @('Game','PlayerProfile','Character','CharacterDrop','Ragdoll','Player')) {
   foreach ($method in $type.Methods) {
    if (($type.Name -eq 'Game' -and $method.Name -match 'RegisterKill|GetPlayerProfile') -or
        ($type.Name -eq 'PlayerProfile' -and ($method.Name -match 'GetPlayerID|GetName|Stat|SavePlayerData|LoadPlayerData' -or ($method.HasBody -and ($method.Body.Instructions | Where-Object { "$($_.Operand)" -match 'm_playerStats' })))) -or
        ($type.Name -eq 'Character' -and $method.Name -in @('OnDeath','IsBoss','IsDead','GetHealth')) -or
        ($type.Name -eq 'CharacterDrop' -and $method.Name -in @('Awake','OnDeath','DropItems')) -or
        ($type.Name -eq 'Ragdoll' -and $method.Name -in @('Setup','SpawnLoot','OnDestroy','DestroyNow','SaveLootList'))) {
     "METHOD $($method.FullName) PARAMS $($method.Parameters -join ', ')"
     foreach ($instruction in $method.Body.Instructions) { "$instruction" }
    }
   }
  }
 }
} finally { $assembly.Dispose() }
