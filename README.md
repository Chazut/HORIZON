# Raid Spectator

Keep watching your solo SPT raid after death or extraction. The mod automatically switches to **Terkoiz Freecam** while the bots and raid timer continue running.

**Early test build: 0.2.1.** Built against SPT 4.1.6 and Freecam 1.4.8. Automated checks pass; in-game validation is still required.

0.2.1 maintains physics transform synchronization after death/extraction, including during Killcam and while the spectator menu is open. EFT normally performs that work from the local player's hands update, which is no longer reliable once the player is gone. This addresses a cause of bots falling through the map or failing to move while spectating. It does not alter AI decisions, teleport bots, or force all scenery to render. Verification in a real raid is still needed for the reported Shoreline falls and stationary bot.

It also fixes the BotDebug overlay after extraction: EFT's debug accessor returns empty snapshots when its human-player list is empty. While spectating, BotDebug now reads the same native debug snapshots directly from living bots. Projection, draw distance and the displayed distance use the Freecam camera. The spectator remains removed from combat and normal BotDebug behavior resumes on exit. BotDebug stays optional and its DLL is not replaced.

## Install and use

- Requires [Freecam 1.4.8](https://sp-mod.com/mod/164/freecam), installed separately.
- Extract the ZIP into your SPT game folder.
- After death or a successful extraction, move using Freecam's controls and settings. Transit continues normally.
- Scroll up/down to jump the camera to the next/previous living AI PMC. Scavs, bosses, raiders and humans are excluded. This moves only the camera; you can move freely again immediately.
- BotDebug is optional. If installed, its overlay turns on when spectator Freecam starts and off when you leave. Its saved settings are not changed.
- If maschine-Killcam is installed, its replay and live tail finish first, then Freecam takes over automatically. Killcam is optional.
- Press **Escape** to release the mouse, then click **Leave raid**. **Resume Freecam** returns to the camera.
- Under **F12 > Raid Spectator**, you can toggle spectating after extraction, PMC cycling and automatic BotDebug independently (all enabled by default), or change the menu key.

Your original result is preserved: **Killed**, **Survived** or **Runner**, with the original extraction name and play time. Spectator time is excluded from your survival statistics and cannot upgrade a run-through. After extraction, your body is removed from AI targets and hidden, while health simulation and damage are suspended for that player only. The normal raid time limit still ends the session. Use **Leave raid** to save, rather than force-closing the game.

**Fika is incompatible**, including a Fika installation used alone. BepInEx refuses to load this mod when Fika is present. No server component is needed.

Freecam's teleport and return-to-player shortcuts are suspended while spectating. Freecam wheel zoom is suspended only while spectator PMC cycling is enabled. Normal play and transit are unchanged.

With ORBIT, keep **Wake for spectators** enabled so Ghost bots wake after the last human dies or leaves the active player roster. This can cost more FPS than Ghost Mode while playing. ORBIT is optional. No additional bot-control logic is included here.

## Validation

```powershell
dotnet build -c Release
dotnet run --project tests/Regression -c Release
dotnet run --project tests/CameraHarness -c Release
```

Before release, verify in real raids:

1. Die with Freecam initially off, then with it already on. The corpse stays dead and bots continue moving/fighting.
2. Camera movement, audio, rendering and Escape menu work. Freecam teleport cannot move the corpse.
3. Leave using the button: death screen, killer, quests, insurance and profile save remain correct. Spectator time adds no survival XP.
4. Let the raid timer expire after dying: normal shutdown still reports Killed.
5. Extract normally, then separately with a run-through: observe for several minutes and leave. Verify original result, exit, inventory, health and play time. Bots must ignore the extracted body. Test transit too; it must not open spectator mode. Play another raid without restarting to check cleanup.
6. Try dying with inventory open, without ORBIT, and as a player Scav.
7. Check Fika/missing Freecam loader refusal and camera-not-ready fallback.
8. Die with Killcam enabled: let the replay and live tail finish, then check Freecam and Leave raid. Repeat without a replay (for example, no killer recording). The replay must not fight the spectator camera.

9. Scroll both directions with several PMCs alive, then after the selected PMC dies/extracts. No Scavs/bosses/humans should be selected and the body must never move. Check indoors camera placement.
10. With BotDebug installed, verify overlay activation and shutdown; without it, spectating must still work. Open Escape/F12: scrolling menus must not switch PMCs.

11. After death and extraction, keep observing for at least five minutes with the camera away from several moving bots. Check both PMC and Scav movement, stairs/doors and ground support. Open the spectator menu and wait through Killcam too. `Spectator physics` should show increasing synchronization counts with `autoSync=False`; `staleBounds` should remain zero. A warning can also describe a legitimate prolonged hold while the mover still requests movement, so review its context. Repeat without ORBIT/COMET when comparing vanilla behavior. Fika remains incompatible with this solo plugin.
12. With BotDebug 1.8.0, extract with bots still alive and confirm labels appear (not just the Enabled toggle). Move Freecam far from the extraction point and cycle PMCs: labels and their distances must follow the camera. Test manual enable with automatic activation off, death after Killcam, disabling/re-enabling BotDebug and a second raid. `BotDebug spectator data active` confirms the first populated snapshot; absence of this message with only dormant bots is not a rendering failure.

Look for `[Raid Spectator]` in `BepInEx/LogOutput.log`. In addition to entry/exit messages, a physics summary every 30 seconds records synchronization cost and stale collider bounds. Checks every 5 seconds report severe falls, movement without displacement and stale bounds (at most once per bot per 30 seconds). These observations do not change bot behavior. There is no per-frame file output.

## Implementation

The plugin delays the local death shutdown before `LocalGame.Stop` stops bot scenarios. It lets the real death callbacks, corpse creation and kill tracking run. Exit and timeout resume the native game shutdown and SPT profile-saving flow with the captured outcome. Extraction is intercepted at the same shutdown boundary after the game has decided Survived/Runner. The extracted body is removed from the world/AI player lists and deactivated (with its camera detached if necessary), without killing it or unloading its profile. Entry failures restore the touched state and allow the native ending. Freecam supplies the camera and its controls; it is not bundled or modified.

The optional Killcam integration observes its pending/playback state without changing the replay or its configuration. Camera ownership and spectator input start only after replay cleanup. If its API cannot be read, or a replay stays stuck for 120 seconds, the mod preserves the normal Killed exit instead of seizing the camera. This handoff has automated checks against the installed Killcam API; a real raid still needs validation.
