# Raid Spectator

Keep watching your solo SPT raid after you die. The mod automatically switches to **Terkoiz Freecam** while the bots and raid timer continue running.

**Early test build: 0.1.0.** Built against SPT 4.1.6 and Freecam 1.4.8. Automated checks pass; in-game validation is still required.

## Install and use

- Requires [Freecam 1.4.8](https://sp-mod.com/mod/164/freecam), installed separately.
- Extract the ZIP into your SPT game folder.
- After death, move using Freecam's controls and settings.
- If maschine-Killcam is installed, its replay and live tail finish first, then Freecam takes over automatically. Killcam is optional.
- Press **Escape** to release the mouse, then click **Leave raid**. **Resume Freecam** returns to the camera.
- Configure the enable toggle and menu key under **F12 > Raid Spectator**.

Your death remains a death. Leaving saves the normal **Killed** result; spectator time is excluded from your survival statistics. The normal raid time limit still ends the session. Use **Leave raid** to save, rather than force-closing the game.

**Fika is incompatible**, including a Fika installation used alone. BepInEx refuses to load this mod when Fika is present. No server component is needed.

Freecam's teleport and return-to-player shortcuts are suspended while spectating. The living-player experience and normal extraction are unchanged.

With ORBIT, keep **Wake for spectators** enabled so Ghost bots wake after the last human dies. This can cost more FPS than Ghost Mode while playing. ORBIT is optional. No additional bot-control logic is included here.

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
5. Extract alive and play another raid without restarting: neither case inherits spectator state.
6. Try dying with inventory open, without ORBIT, and as a player Scav.
7. Check Fika/missing Freecam loader refusal and camera-not-ready fallback.
8. Die with Killcam enabled: let the replay and live tail finish, then check Freecam and Leave raid. Repeat without a replay (for example, no killer recording). The replay must not fight the spectator camera.

Look for `[Raid Spectator]` in `BepInEx/LogOutput.log`. Logging is limited to startup, death, exit and errors; there is no per-frame file output.

## Implementation

The plugin delays the local death shutdown before `LocalGame.Stop` stops bot scenarios. It lets the real death callbacks, corpse creation and kill tracking run. Exit and timeout resume the native game shutdown and SPT profile-saving flow with the original Killed outcome. Freecam supplies the camera and its controls; it is not bundled or modified.

The optional Killcam integration observes its pending/playback state without changing the replay or its configuration. Camera ownership and spectator input start only after replay cleanup. If its API cannot be read, or a replay stays stuck for 120 seconds, the mod preserves the normal Killed exit instead of seizing the camera. This handoff has automated checks against the installed Killcam API; a real raid still needs validation.
