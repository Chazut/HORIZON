# Unreleased

- Larger interface on high-resolution monitors, with automatic scaling and manual size controls in the spectator menu and F12.
- Hold Left Ctrl and scroll to zoom without switching bots, in Freecam or while following. Shortcut and speed are configurable.

# HORIZON 1.1.0

- **Follow a bot:** press F to attach/detach the camera, and R to switch between following its view and rotating around it with your mouse.
- **Move the interface:** drag the menu by its title, or hold Left Alt to drag the help panel. Positions are saved.
- **Cleaner spectator view:** removes lingering death, injury, flash and visor effects.
- **Keep watching:** automatically switches to another bot when your target dies or extracts, or returns to Freecam if none remain.

Camera keys, sensitivity and distance can be adjusted in F12.

# HORIZON 1.0.0

First public version.

- Keep watching solo raids after death or extraction with Terkoiz Freecam.
- Switch between PMCs only or all living bots using the mouse wheel or spectator menu.
- Configure spectating, camera controls and the optional BotDebug overlay in F12.
- Wait for Killcam before entering Freecam, when installed.
- Preserve the original raid result and play time. Transits continue normally.
- Use the plugin GUID `com.chazut.horizon`, with automatic import of older Raid Spectator settings when no HORIZON config exists.
- Package the plugin as `BepInEx/plugins/HORIZON/HORIZON.dll`. Remove the old `BepInEx/plugins/RaidSpectator` folder before updating.

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
