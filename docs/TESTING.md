# HORIZON testing and implementation

## Validation

```powershell
dotnet build -c Release
dotnet run --project tests/Regression -c Release
dotnet run --project tests/CameraHarness -c Release
dotnet run --project tests/ConfigHarness -c Release
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

9. Scroll both directions with several PMCs alive, then after the selected PMC dies/extracts. In PMCs only, no Scavs/bosses/humans should be selected. In All bots, living AI Scavs/bosses/guards should be selectable too, but not humans or corpses. Switch filters during the same raid. The player body must never move. Check indoors camera placement.
10. BotDebug automatic activation must be OFF on a fresh install. With BotDebug installed, verify manual on/off in the spectator menu, automatic activation when explicitly enabled, and shutdown on leave. Without it, the menu must show unavailable and spectating must still work. Open Escape/F12: scrolling menus must not switch PMCs.

11. After death and extraction, keep observing for at least five minutes with the camera away from several moving bots. Check both PMC and Scav movement, stairs/doors and ground support. Open the spectator menu and wait through Killcam too. `Spectator physics` should show increasing synchronization counts with `autoSync=False`; `staleBounds` should remain zero. A warning can also describe a legitimate prolonged hold while the mover still requests movement, so review its context. Repeat without ORBIT/COMET when comparing vanilla behavior. Fika remains incompatible with this solo plugin.
12. With BotDebug 1.8.0, extract with bots still alive and confirm labels appear (not just the Enabled toggle). Move Freecam far from the extraction point and cycle PMCs: labels and their distances must follow the camera. Test manual enable with automatic activation off, death after Killcam, disabling/re-enabling BotDebug and a second raid. `BotDebug spectator data active` confirms the first populated snapshot; absence of this message with only dormant bots is not a rendering failure.

Look for `[HORIZON]` in `BepInEx/LogOutput.log`. In addition to entry/exit messages, a physics summary every 30 seconds records synchronization cost and stale collider bounds. Checks every 5 seconds report severe falls, movement without displacement and stale bounds (at most once per bot per 30 seconds). These observations do not change bot behavior. There is no per-frame file output.

## Implementation

The plugin delays the local death shutdown before `LocalGame.Stop` stops bot scenarios. It lets the real death callbacks, corpse creation and kill tracking run. Exit and timeout resume the native game shutdown and SPT profile-saving flow with the captured outcome. Extraction is intercepted at the same shutdown boundary after the game has decided Survived/Runner. The extracted body is removed from the world/AI player lists and deactivated (with its camera detached if necessary), without killing it or unloading its profile. Entry failures restore the touched state and allow the native ending. Freecam supplies the camera and its controls; it is not bundled or modified.

The optional Killcam integration observes its pending/playback state without changing the replay or its configuration. Camera ownership and spectator input start only after replay cleanup. If its API cannot be read, or a replay stays stuck for 120 seconds, the mod preserves the normal Killed exit instead of seizing the camera. This handoff has automated checks against the installed Killcam API; a real raid still needs validation.
