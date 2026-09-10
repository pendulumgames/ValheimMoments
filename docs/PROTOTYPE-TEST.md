# Capture regression checks

Install the current package into a separate BepInEx development profile with Valheim
closed, then launch that profile modded. Keep the entire Encoder folder next to the
plugin DLL, including Imazen.WebP and the libwebp native DLLs.

1. Enter a world and wait five seconds.
2. Move the camera and press F10, then play for two more seconds.
3. Open the resulting animated WebP from the mod's Clips folder in a browser.
4. Verify pre-event footage, duration, orientation, colors and HUD. Repeat in combat
   or a busy forest.
5. Compare performance with F9 recording paused and resumed in the same scene.
6. Try alt-tab, changing resolution and quitting during capture/encoding.

Default capture is 640x360 at 15 FPS, with five seconds before and two seconds after
manual/death triggers. Boss and loot triggers have separate four-second post windows.
The default managed frame pool is approximately 185 MiB; GPU and encoder memory are
additional. Memory and timing depend on settings and hardware.

Repeated triggers while collecting/encoding are skipped. F9 cancels a collecting clip;
encoding can finish. Focus loss stops new frame submissions. The prototype also captures
menus while focused. Wait five seconds after changing worlds for history entirely from
that world. World/session changes prevent old clips being submitted to a new session.

The Debug.LogCaptureTiming option records aggregate timings and frame counts. When
reporting problems, provide the mod version, game version, relevant log lines and your
capture settings. Never share a config containing a webhook URL.

If the result is upside down, set Capture.FlipVertically=true and repeat. Restart after
editing dimensions, FPS, time windows or memory budget. A hung encoder helper is stopped
after 120 seconds. Local recordings accumulate until removed by the user.
