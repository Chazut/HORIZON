<p align="center"><img src="branding/horizon-logo.svg" width="160" alt="HORIZON logo"></p>
<p align="center">
  <img src="https://img.shields.io/github/stars/Chazut/HORIZON?style=flat-square&label=STARS&color=007ec6" />
  <img src="https://img.shields.io/github/issues/Chazut/HORIZON?style=flat-square&label=ISSUES&color=44cc11" />
  <img src="https://img.shields.io/github/downloads/Chazut/HORIZON/total?style=flat-square&label=DOWNLOADS&color=44cc11" />
</p>

# HORIZON

**Your raid ends. The world keeps going.**

Keep watching your SPT raid after death or extraction. Fly around the map and see what the bots do next.

**HORIZON 1.3.0 RC2 for SPT 4.1.6.** Solo uses Terkoiz Freecam 1.4.8 or later. Co-op uses Fika's built-in spectator camera.

## What it does

- Continue after death, extraction, or both.
- Use the mouse wheel to jump between **PMCs only** or **all living bots**.
- Follow a bot, either looking in its direction or orbiting around it with the mouse.
- When it dies or extracts, stay where you are in Freecam. Automatic switching is optional and OFF by default.
- Drag the spectator panels and watch without lingering player injury or flash effects.
- Scale the interface for your monitor, automatically or manually, and zoom with Ctrl + mouse wheel.
- Show information above bots on screen: name, type, health, SAIN personality, carried value and current action. Supports SAIN, ORBIT, COMET, QuestingBots and LootingBots when installed.
- Optionally open a list of living bots grouped and colored by squad, then click to visit. OFF by default.
- Choose HORIZON labels, BotDebug's overlay or neither. BotDebug remains optional and is never enabled by default.
- Wait for Killcam to finish, if installed.
- Keep your original raid result, loot and play time. Spectating adds no survival time.

Transits work normally. The raid timer still applies.

## Install

1. For solo SPT, install **Terkoiz Freecam 1.4.8 or later**. Fika already provides its own camera.
2. Extract the full HORIZON archive into your SPT game folder. Keep both DLLs together in `BepInEx/plugins/HORIZON/`. In Fika, install the same ZIP on the raid host and each player PC that will use HORIZON. If a headless client runs the raid, install it in that client's game folder. The SPT backend needs no installation.
3. Open **F12 > HORIZON** to change your settings.

Plugin GUID: `com.chazut.horizon`. Settings are saved in `BepInEx/config/com.chazut.horizon.cfg`; older Raid Spectator settings are imported automatically on first launch.

Updating from a build installed in `BepInEx/plugins/RaidSpectator`? Remove that old plugin folder first; keep its config to import your settings.

ORBIT, BotDebug and Killcam are optional. With ORBIT, enable **Wake for spectators** to observe awake bots.

## Controls

**Fika:** When the server allows free-camera and bot spectating, press **F3** after death or extraction to see BotInfo. The raid host reads each visible bot's current action, SAIN personality, ORBIT Ghost state, health and estimated carried value and sends them to the requesting spectator. AI mod integrations use the mods installed on the raid host. The graphical host can keep playing while a teammate spectates. Without a compatible host, the overlay shows "Host action unavailable". The separate BotDebug overlay runs on the graphical host only. Open HORIZON's menu with **F6** and use Fika's extract shortcut (**F8** by default) to leave.

Use Freecam's usual movement controls. **Mouse wheel** visits bots; **Escape** opens the spectator menu, where you can change the bot filter, toggle BotDebug or choose **Leave raid** to save and exit.

**Bot info** is the default overlay. Press **F3** to hide or show labels while spectating. The next raid starts with the overlay selected in your settings. You can rebind F3 in F12.

Labels have a transparent background and outlined text. They show through scenery at any distance while the bot is on screen. Use the spectator menu or F12 to limit their range, require line of sight, or choose **BotDebug** or **Off**.

Carried value estimates the entire equipped inventory, including gear and item stacks, using handbook prices. It is refreshed every five seconds; it is not profit earned during the raid. No Raid Review installation is needed.

Enable **Show bot list** in the menu or F12. Hold **Left Alt** or open **Escape**, then click a bot to move the camera or drag the list by its title. The list includes all living bot types regardless of your mouse-wheel filter. A click preserves follow mode if it was already enabled.

Press **F** to attach the camera and orbit the bot with your mouse. **R** locks the camera to the bot's viewing direction or returns to mouse orbit. Press **F** again to detach. Both keys, mouse sensitivity and camera distance are configurable in F12. Hold **Left Alt** to drag the help panel by its title, or drag the open menu by its title. Their positions are saved.

Hold **Left Ctrl + mouse wheel** to move closer/further without switching bots. In follow mode it adjusts the distance to the bot; in Freecam it moves along your viewing direction. Change the shortcut and speed in F12.

The interface automatically grows with your resolution (2x at 4K). Adjust **Interface size** in the spectator menu or **F12 > HORIZON > Interface**; disable automatic scaling if you prefer a fixed size.

---

Thanks to **Terkoiz** (Freecam), **DrakiaXYZ** (BotDebug) and **maschine** (Killcam).

[Build & testing](docs/TESTING.md) · [Changelog](CHANGELOG.md) · [MIT license](LICENSE)
