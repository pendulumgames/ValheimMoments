# Release dependencies

The plugin targets .NET Standard 2.1 and builds against installed Valheim/Unity,
BepInEx and Harmony assemblies. None of those binaries are redistributed. Epic Loot
is an optional runtime integration, tested with release 0.14.2; it is not bundled.

The Windows x64 helper targets .NET Framework 4.8 and bundles:

- Imazen.WebP **11.0.0**, MIT.
- Imazen.WebP.NativeRuntime.win-x64 **1.6.0**, containing libwebp, libwebpmux,
  libwebpdemux and libsharpyuv from libwebp **1.6.0**, BSD-3-Clause with its patent grant.

NuGet hashes are pinned in packages.lock.json. Complete license/attribution files and
source provenance are in the plugin's Licenses directory. The helper uses framework
RuntimeInformation rather than redistributing a separate DLL. Build-only reference
assemblies are not shipped.

Sources: https://github.com/imazen/libwebp-net/releases/tag/v11.0.0 and
https://github.com/imazen/libwebp-net/releases/tag/native-v1.6.0 (native source:
https://github.com/webmproject/libwebp/tree/v1.6.0).

The earlier local prototype used Magick.NET 14.16.0. That bundle and its unrelated
image delegates are excluded from 0.8.1 public-release packages. Historical notices
remain in the repository for provenance of old test archives.

Encoding remains isolated in a hidden, below-normal-priority helper. It imports RGBA
frames directly, uses method 3 and one libwebp encoding thread, and writes only the
final WebP through a temporary .partial file. No raw-frame cache is written to disk.
The pinned wrapper's final-frame duration is supplied explicitly; independent RIFF
and decode tests validate the exact animation timing.
