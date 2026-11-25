# HORIZON 1.4.0

## Features

- **Build your own BotInfo labels.** Choose which details to show and which ones share a line, including bot difficulty and distance. Actions stay visible for all bots by default, or you can show them only for the bot you're following.
- **Follow the raid on DynamicMaps.** The map arrow tracks your free camera or the bot or teammate you're following, including their floor and heading. Hold **Left Alt** to drag the minimap where you want it.
- **See COMET squad roles.** BotInfo shows who's watching an exit during a siege, who's throwing a suppression grenade and who's holding the angle.
- **Adjust your spectator view.** Set your own field of view. In Fika, you can also show living players' names, health and distance.

## Changes

- If footsteps go quiet in solo after Killcam or after losing a followed bot, HORIZON now records what the spectator camera could hear for troubleshooting.

## Fixes

- **Fika follow selection.** Pressing **F** now follows the bot or teammate selected in Fika. Clicking a name in HORIZON's list still takes priority.
- Your held weapon is now hidden by default when you spectate after extracting. You can turn it back on in the spectator settings.

# HORIZON 1.3.0

## Features

- **Spectate Fika raids.** After death or extraction, press **F6** to open HORIZON. Fly freely or follow a bot, depending on the permissions set by the host.
- **See what bots are doing in co-op.** BotInfo gets current actions, SAIN personalities, health and carried value from the raid host, including headless hosts.
- **Read bot names in English.** The bot list, labels and follow camera now show Cyrillic names in Latin characters, using Raid Review's spellings where available.

## Changes

- One DLL works for solo SPT, Fika players and headless hosts. Solo still uses Terkoiz Freecam. Install HORIZON on the raid host and each PC that wants to spectate.

## Fixes

- Freecam and zoom work when the host allows freecam but disables bot spectating.
- Joining spectators see the host's squad groups and lone bots in the list, even with overhead labels turned off.

# HORIZON 1.3.0 RC4

- The bot list, overhead labels and spectator camera now show Cyrillic bot names in English. HORIZON uses Raid Review's spellings for known names and transliterates the rest.

# HORIZON 1.3.0 RC3

- Joining Fika players request bot actions and SAIN personalities directly from the raid host. The host checks its own raid and spectator state before replying.
- The bot list uses the host's squad IDs and can identify solo bots even with F3 labels off.
- For troubleshooting, search both BepInEx logs for `Fika bot sync` to see the request, the host's decision and the client's receipt. Install RC3 on both the raid host (including headless) and the spectating PCs.

# HORIZON 1.3.0 RC2

- On Fika servers with `allowSpectateFreeCam` enabled, F6 now opens HORIZON's menu after death or extraction even when bot spectating is off. Camera movement and zoom work in this mode.
- Bot labels (F3), the bot list, cycling and following still require `allowSpectateBots`. The menu shows when the server has disabled these features.

# HORIZON 1.3.0 RC1

- Added Fika spectator camera support for graphical hosts and clients, including clients on a headless-hosted raid. Fika controls permissions, extraction and saving.
- Fika clients can cycle, follow and label replicated bots. The default HORIZON menu key is F6 in co-op.
- BotInfo now requests current bot actions, personality, Ghost state, health and estimated carried value directly from the raid host. Works with graphical and headless hosts. The host checks the spectator's death or extraction and Fika permissions before sending a label.
- Solo SPT, Fika players and headless raid hosts use the same `HORIZON.dll`. It loads the built-in Fika integration when Fika is present. Install it on the raid host and each PC that wants to spectate. The SPT backend needs no files.

# HORIZON 1.2.0

## Features

- **BotInfo** shows health, gear value, and current action, plus personality with SAIN. Press **F3** to toggle labels.
- **Squad colors and an optional bot list** help you find PMC squadmates. Click a name to jump to that bot.
- **Camera zoom** uses **Left Ctrl + mouse wheel**. The interface scales for 4K screens.

## Changes

- **F** follows a bot in mouse orbit by default. **R** locks the camera to its facing direction.
- When the bot dies or extracts, the camera returns to Freecam by default.

## Fixes

- Dotted lines keep distant or crowded labels connected to the right bots.

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
