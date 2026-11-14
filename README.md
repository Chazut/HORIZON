<p align="center"><img src="branding/horizon-logo.svg" width="160" alt="HORIZON logo"></p>

# HORIZON

**Your raid ends. The world keeps going.**

Keep watching your solo SPT raid after death or extraction. Fly freely around the map and see what the bots do next.

**SPT 4.1.6 | 1.0.0 | Solo only: incompatible with Fika.**

## What it does

- Continue after death, extraction, or both.
- Use the mouse wheel to jump between **PMCs only** or **all living bots**.
- Follow a bot, either looking in its direction or orbiting around it with the mouse.
- Drag the spectator panels and watch without lingering player injury or flash effects.
- Optionally show BotDebug's overlay. OFF by default; requires BotDebug.
- Wait for Killcam to finish, if installed.
- Keep your original raid result, loot and play time. Spectating adds no survival time.

Transits work normally. The raid timer still applies.

## Install

1. Install **Terkoiz Freecam 1.4.8 or later**. HORIZON will not load without it.
2. Extract the HORIZON archive into your SPT folder. The plugin is `BepInEx/plugins/HORIZON/HORIZON.dll`.
3. Open **F12 > HORIZON** to change your settings.

Plugin GUID: `com.chazut.horizon`. Settings are saved in `BepInEx/config/com.chazut.horizon.cfg`; older Raid Spectator settings are imported automatically on first launch.

Updating from a build installed in `BepInEx/plugins/RaidSpectator`? Remove that old plugin folder first; keep its config to import your settings.

ORBIT, BotDebug and Killcam are optional. With ORBIT, enable **Wake for spectators** to observe awake bots.

## Controls

Use Freecam's usual movement controls. **Mouse wheel** visits bots; **Escape** opens the spectator menu, where you can change the bot filter, toggle BotDebug or choose **Leave raid** to save and exit.

**F** attaches/detaches the camera. **R** switches between the bot's viewing direction and free mouse orbit. Both keys, mouse sensitivity and camera distance are configurable in F12. Hold **Left Alt** to drag the help panel by its title; drag the menu by its title while it is open. Their positions are saved.

---

Thanks to **Terkoiz** (Freecam), **DrakiaXYZ** (BotDebug) and **maschine** (Killcam).

[Build & testing](docs/TESTING.md) · [Changelog](CHANGELOG.md) · [MIT license](LICENSE)
