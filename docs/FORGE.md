# HORIZON

**Your raid ends. The world keeps going.**

Ever wondered what happens after you die or extract? Stay in the raid, explore with Freecam and watch the bots carry on.

**Requires Terkoiz Freecam 1.4.8 or later. Solo only: incompatible with Fika.**

## HORIZON {.tabset}

### Overview

- **Keep watching after death or extraction.** Enable either or both in F12.
- **Explore freely.** Fly around the map using Freecam's controls.
- **Jump between bots.** Scroll through PMCs only, or all living bots.
- **Optional BotDebug overlay.** Toggle it from the spectator menu if BotDebug is installed. OFF by default.
- **Works alongside Killcam.** Watch the replay first, then enter Freecam.

Your original raid result, loot and play time are preserved. Watching longer does not add survival time or turn a run-through into a survived raid. Transits work normally, and the raid timer still applies.

### Installation

1. Install **Terkoiz Freecam 1.4.8 or later**.
2. Extract HORIZON into your SPT folder.
3. Open **F12 > HORIZON** to adjust your settings.

**Freecam is required:** HORIZON will not load without it.

**Fika is incompatible**, including solo play with Fika installed.

ORBIT, BotDebug and Killcam are optional. With ORBIT, enable **Wake for spectators** to observe awake bots.

Updating from Raid Spectator or an earlier HORIZON build using the old folder? Remove `BepInEx/plugins/RaidSpectator` before installing. The new plugin is `BepInEx/plugins/HORIZON/HORIZON.dll`.

Keep your old config: its settings are automatically copied to `BepInEx/config/com.chazut.horizon.cfg` on first launch. An existing HORIZON config is kept as-is.

### Controls & settings

| Control | Action |
| --- | --- |
| Freecam controls | Move the camera freely |
| Mouse wheel | Jump to the next or previous bot |
| Escape | Open the spectator menu |
| Leave raid | Save your original result and return to the usual end-of-raid screens |

The spectator menu lets you switch between **PMCs only** and **all bots**, visit bots and toggle BotDebug. Camera jumps do not automatically follow a bot.

In **F12 > HORIZON**, you can also configure death/extraction spectating, wheel navigation, reverse scrolling, camera distance and the help panel.

Use **Leave raid** when you are done watching so the game can save normally.

### Credits

Thanks to **Terkoiz** for Freecam, **DrakiaXYZ** for BotDebug and **maschine** for Killcam.

[Source code](https://github.com/Chazut/HORIZON) · MIT license.
