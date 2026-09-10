# Valheim Moments

![Valheim Moments icon](release/icon.png)

Animated gameplay highlights for Valheim: manual captures, player deaths, boss kills
and great loot, with optional Epic Loot details and host-controlled Discord delivery.

**Windows x64 beta — version 0.9.1.** Manual capture, deaths, boss summaries and loot
highlights have been tested in game. The WebP-only encoder and host/joining-player
F10 Discord delivery have passed a live co-op check. Dedicated-server checks remain pending.

This is the public source repository for the Pendulum Thunderstore package, provided
for review and development. See [player instructions and settings](release/README.md).
The initial Thunderstore listing is under moderation review; this repository does not
imply approval or availability in the mod manager.

## Build from source

Requirements: Windows x64, a .NET SDK capable of building netstandard2.1/net48 (tested
with SDK 10.0.401), installed Valheim, and a BepInEx-enabled Valheim profile. Running
the helper requires .NET Framework 4.8. No game or BepInEx assemblies are included here.

1. Copy `local.props.example` to `local.props`.
2. Set `GamePath` to your Valheim installation and `ProfilePath` to your BepInEx profile.
3. From PowerShell in the repository root, run:

```powershell
./tools/Build-Prototype.ps1 -Restore
./tools/Build-Thunderstore.ps1
```

The first command restores pinned NuGet packages and builds/tests a local package.
The second validates and creates `artifacts/Valheim_Moments-0.9.1.zip`, including
manifest, README, changelog, icon, plugin, encoder and license notices. It does not
publish anything. Do not reupload changed contents under an already published version.

Close Valheim before installing into a development profile:

```powershell
./tools/Install-Prototype.ps1 -ProfilePath 'C:\path\to\your\Valheim\profile'
```

The installer backs up the previous plugin and preserves configuration. Keep only one
installed copy when switching between a manual install and the mod manager.

## Verification

```powershell
./tools/Test-Core.ps1
./tools/Test-Discord.ps1
./tools/Test-Encoder.ps1
./tools/Test-EncoderFormat.ps1
dotnet restore tests/DeathTests.csproj --configfile NuGet.Config
dotnet build tests/DeathTests.csproj -c Release --no-restore
./tests/bin/Release/net48/DeathTests.exe
```

Event tests use behavioral game/API stand-ins with real Harmony; the production build
separately checks compatibility against the supplied installed game assemblies.
Discord tests use fake HTTP and never send messages. Relay tests simulate connected
peers; they are not a replacement for Steam/PlayFab or dedicated-server testing.
Encoder tests check RIFF timing, full decode, color channels, vertical flip, unequal
frame durations, cancellation and malformed input.

See [capture checks](docs/PROTOTYPE-TEST.md) and [co-op checks](docs/RELAY-TEST.md).

## How it works

- A bounded five-second RGBA history uses GPU downsampling and asynchronous readback.
- A trigger retains recent frames and collects post-event footage. Extra triggers are
  skipped while a clip is collecting or encoding.
- A hidden helper receives frames through a pipe and encodes animated WebP using
  libwebp on one encoding thread. Only the completed media is written to disk.
- Read-only Harmony observers correlate player deaths, credited kills and generated
  loot. Optional Epic Loot integration reads completed item data without rerolling it.
- In multiplayer, direct peer RPCs send bounded clip transfers to the host. Webhook
  URLs and bot name stay host-owned. Clients retain their original files.

The host accepts client event captions and fixed event types, adds a recorder name
from the connected peer, and applies its own destination settings. It does not verify
that client footage depicts the claimed event. Relay transfers are limited to 10 MiB,
one incoming transfer/upload at a time, with pacing and timeouts. No automatic binary
updates or runtime executable downloads are performed.

## Configuration and privacy

The plugin GUID is `local.valheimmoments`; the config is `local.valheimmoments.cfg`.
See the [complete configuration reference](docs/CONFIGURATION.md) for defaults,
limits and host/client ownership, and [integration notes](docs/INTEGRATIONS.md) for
every Harmony patch, the capture pipeline, validation evidence and remaining tests.
Version 0.9.0 renames the technical identity. Before upgrading an older install, close
Valheim and migrate its config to the new filename, preserve its Clips folder, and
remove the previous plugin from the active profile. The development installer supports
explicit `-PreviousPluginDirectory` and `-PreviousConfigPath` arguments for this upgrade.
The generated config can contain Discord webhook secrets: **do not commit or share it**.
Local paths, configs, footage, logs, game assemblies, build outputs and internal
working notes are excluded from Git. The icon and source code are included.

## License and attribution

Original project code is licensed under [MIT](LICENSE), copyright 2026 Pendulum.
Dependency licenses and source provenance are in [third-party/webp](third-party/webp)
and [DEPENDENCIES.md](docs/DEPENDENCIES.md). Game, Unity, BepInEx, Harmony and Epic Loot
binaries are not redistributed.

Significant portions of the code and icon were created with AI tools. The Thunderstore
listing is categorized AI Generated. This is an unofficial community project with no
affiliation with Iron Gate or Coffee Stain.
