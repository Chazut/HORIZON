# HORIZON

Did the PMC who killed you make it to extract? HORIZON lets you stay in your solo SPT raid after death or extraction. Fly back to the fight, follow a squad or watch the bots loot what's left.

**HORIZON 1.2.0 for SPT 4.1.6.** Requires a solo installation without Fika and [Terkoiz Freecam 1.4.8 or later](https://sp-mod.com/mod/164/freecam).

## HORIZON {.tabset}

### Overview

- **See what a bot is doing.** BotInfo shows its name, type, health, estimated gear value and current action. With SAIN, you can also see its personality. Actions from ORBIT, COMET, QuestingBots and LootingBots appear when those mods are installed and active.
- **Pick out a squad.** PMC squadmates share a label color. A dotted line connects each label to its bot, even at a distance. Turn on the optional **Show bot list** and click a name to visit that squad member.
- **Follow the fight.** Fly freely or attach the camera to a bot. When that bot dies or extracts, the camera stays where you last saw it. You can enable automatic switching if you'd like to follow someone else immediately.

When you're done, choose **Leave raid** in the spectator menu. You keep your original result, loot and play time. The raid timer still applies and transits work normally. If you use Killcam, its replay finishes before HORIZON takes over.

### Installation

1. Close the game and install Freecam.
2. Extract the HORIZON ZIP into the SPT folder that contains `EscapeFromTarkov.exe`. Allow it to replace the old HORIZON DLL if you're updating.
3. Start the game. Under **F12 > HORIZON**, choose whether to spectate after death, extraction or both.

**Updating from Raid Spectator?** Remove the old `BepInEx/plugins/RaidSpectator` folder and keep its config. HORIZON imports your old settings the first time it creates its own config.

### Controls & settings

| Control | Action |
| --- | --- |
| Mouse wheel | Visit the previous or next PMC. Choose **All bots** in the menu to include Scavs, bosses and guards. |
| F | Attach to the selected bot and orbit it with the mouse. Press again to detach. |
| R | Switch between mouse orbit and the bot's viewing direction while following. |
| Left Ctrl + mouse wheel | Zoom in or out. Works in Freecam and while following. |
| F3 | Hide or show BotInfo for this raid. |
| Esc | Open the spectator menu or return to the camera. |
| Hold Left Alt | Click a bot in the list or drag a panel by its title. |

BotInfo is on by default and shows on-screen bots through scenery at any distance. The spectator menu lets you limit the distance, require line of sight or switch to the optional BotDebug overlay. For a bigger interface, adjust the scale in the menu or F12. The default scale adapts to your screen.

If you use ORBIT, enable **Wake for spectators** to wake bots across the map once all human players have died or extracted.

### Credits

Thanks to **Terkoiz** for Freecam, **DrakiaXYZ** for BotDebug, **maschine** for Killcam and **ekky** for Raid Review, which contributed code for identifying bot actions.

[Source code](https://github.com/Chazut/HORIZON) · MIT license.
