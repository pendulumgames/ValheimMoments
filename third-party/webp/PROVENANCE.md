# Release encoder dependencies

Imazen.WebP 11.0.0 and Imazen.WebP.NativeRuntime.win-x64 1.6.0 are unmodified NuGet
packages from https://www.nuget.org/. Package hashes are pinned in the encoder's
packages.lock.json. Both list MIT for the package; their upstream repository is
https://github.com/imazen/libwebp-net at a2d53ed552b46e7f3a9a1a1f8ccd23e19f6f1595.
Package attribution: Copyright 2017–2026 Imazen LLC. The upstream MIT license is
included unchanged as Imazen-WebP-MIT.txt.

The native release is built from webmproject/libwebp v1.6.0:
https://github.com/imazen/libwebp-net/releases/tag/native-v1.6.0
It contains libwebp, libwebpmux, libwebpdemux and libsharpyuv. The WebM project's BSD
license, patent grant and authors list are included unchanged from:
https://github.com/webmproject/libwebp/tree/v1.6.0

The net48 helper uses framework System.Runtime.InteropServices.RuntimeInformation;
no separate RuntimeInformation binary is distributed. Build-only reference assemblies
are not shipped. The old ImageMagick/Magick.NET prototype binaries and native delegates
are excluded from the public release archive.

The helper uses pinned AnimEncoder 11.0.0's _lastDuration field to supply the final
frame's exact duration instead of guessing it from the previous interval. The library
binary is unmodified. Independent RIFF timing and full-decode tests cover this boundary.
