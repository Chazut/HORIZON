# HORIZON 1.0.0

First public version.

- Keep watching solo raids after death or extraction with Terkoiz Freecam.
- Switch between PMCs only or all living bots using the mouse wheel or spectator menu.
- Configure spectating, camera controls and the optional BotDebug overlay in F12.
- Wait for Killcam before entering Freecam, when installed.
- Preserve the original raid result and play time. Transits continue normally.
- Use the plugin GUID `com.chazut.horizon`, with automatic import of older Raid Spectator settings when no HORIZON config exists.

Requires Freecam 1.4.8 or later. Fika is incompatible. Version bump from 0.3.0; no gameplay changes.

## HORIZON 0.3.0 - test build

Raid Spectator is now HORIZON, with configurable spectator controls.

- Choose PMCs only or all living AI bots for camera cycling.
- Switch filters and visit the previous/next bot directly from the Escape menu.
- Configure death and extraction spectating independently.
- Configure wheel navigation, reverse scroll direction and camera visit distance.
- Hide the help panel while keeping access to the spectator menu.
- Toggle BotDebug for the current raid. Automatic activation is OFF by default; BotDebug is optional and absent installations are ignored.
- Keep Freecam as a required dependency and Fika explicitly incompatible.
- Preserve existing Raid Spectator settings and migrate the renamed wheel setting.
- Retain the physics synchronization and post-extraction BotDebug fixes from 0.2.1.

This build has automated lifecycle, camera, overlay and real BepInEx configuration checks. In-raid validation of the new controls remains pending.
