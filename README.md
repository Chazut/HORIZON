# HORIZON

Your raid ends. The world keeps going.

Keep watching a solo SPT raid after death or extraction, using **Terkoiz Freecam**. Move freely, jump between bots and optionally show BotDebug's overlay.

**0.3.0 test build** for SPT 4.1.6. Previously called Raid Spectator. Automated checks pass; the new controls still need testing in a real raid.

## Features

- Continue after death, extraction, or both. Each is configurable.
- Scroll between **PMCs only** or **all living AI bots**, including Scavs, bosses and guards.
- Open the spectator menu to switch filters, visit the previous/next bot, toggle BotDebug and leave the raid.
- Optional BotDebug overlay, **OFF by default**, with camera-relative labels after extraction too.
- Waits for Killcam to finish before entering Freecam, when Killcam is installed.
- Keeps the original raid result, exit and play time. Spectating does not upgrade a run-through or grant extra survival time.

## Installation

1. Install **Terkoiz Freecam 1.4.8 or later** separately. It is a required dependency: HORIZON will not load without it.
2. Extract the HORIZON ZIP into your SPT folder and overwrite the existing DLL if updating.
3. Launch the game. Configure it under **F12 > HORIZON**.

**Solo only. Fika is incompatible**, even when playing alone with Fika installed. No server component is needed. ORBIT, COMET, BotDebug and Killcam are optional. BotDebug integration is inactive when BotDebug is absent.

Existing Raid Spectator users can overwrite directly: HORIZON keeps its original DLL path and plugin ID to prevent duplicate installations and retain settings. The configuration remains `BepInEx/config/com.chazut.raidspectator.cfg`.

## Controls

- Move using Freecam's controls and settings.
- **Mouse wheel:** jump the camera to the next/previous bot in your chosen filter. It does not follow the bot automatically or move its body.
- **Escape:** open the spectator menu. The raid keeps running while it is open.
- **Previous / Next:** visit a bot from the menu, even if wheel navigation is disabled.
- **Leave raid:** end observation and save your original result through the normal game flow.

The normal raid timer still applies. Transit keeps its normal behavior. Use Leave raid to save rather than force-closing the game.

## Settings

| Setting | Default | Effect |
| --- | --- | --- |
| Enabled | ON | Enables HORIZON. Turning it off while spectating ends observation and saves. |
| Spectate after death | ON | Watch after death, once Killcam has finished if present. |
| Spectate after extraction | ON | Watch after a successful extraction or run-through. |
| Camera targets | PMCs only | Choose PMCs only or all living AI bots. Humans, dead and inactive characters are excluded. |
| Cycle bots with mouse wheel | ON | Switch bots with the wheel. OFF restores Freecam's wheel zoom. |
| Reverse mouse wheel | OFF | Reverse next/previous scroll direction. |
| Camera visit distance | 2.5 m | Distance behind a bot when visiting it. Walls can shorten it. |
| Automatic BotDebug overlay | OFF | Enable BotDebug when spectating starts, if installed. |
| Show spectator help | ON | Show the small help panel. Escape still works if hidden. |
| Spectator menu | Escape | Menu shortcut; Escape always remains available. |

The menu's **BotDebug overlay (this raid)** toggle changes only the current session, independently of the automatic preference. The overlay is turned off on leaving without changing BotDebug's saved settings. Other menu preferences are saved in HORIZON's configuration.

With ORBIT, keep **Wake for spectators** enabled to observe awake bots after the last human dies or extracts. Keeping more bots awake can cost FPS.

## Troubleshooting

- No automatic Freecam: check that Freecam loaded, HORIZON is enabled, and the appropriate death/extraction toggle is ON. Missing Freecam or installed Fika prevents HORIZON from loading.
- No BotDebug labels: install BotDebug and enable its overlay in the spectator menu. Its own label/distance settings still apply.
- Scrolling changes camera zoom: enable Cycle bots with mouse wheel. Scrolling F12 or the spectator menu does not visit bots.
- Need diagnostic details: look for `[HORIZON]` in `BepInEx/LogOutput.log`.

Build instructions, regression coverage and the in-raid checklist are in [docs/TESTING.md](docs/TESTING.md).

Thanks to **Terkoiz** for Freecam, **DrakiaXYZ** for BotDebug and **maschine** for Killcam. Dependencies are installed separately and are not bundled.
