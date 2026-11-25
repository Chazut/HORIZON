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
7. In solo, check missing Freecam and camera-not-ready fallback. For co-op, use the Fika checks below.
8. Die with Killcam enabled: let the replay and live tail finish, then check Freecam and Leave raid. Repeat without a replay (for example, no killer recording). The replay must not fight the spectator camera.

9. Scroll both directions with several PMCs alive, then after the selected PMC dies/extracts. In PMCs only, no Scavs/bosses/humans should be selected. In All bots, living AI Scavs/bosses/guards should be selectable too, but not humans or corpses. Switch filters during the same raid. The player body must never move. Check indoors camera placement.
10. Bot info is the default overlay on a fresh install; BotDebug stays off. With BotDebug installed, verify the exclusive overlay selector, migration of the old automatic preference, and shutdown on leave. Without it, the menu must show unavailable and spectating must still work. Open Escape/F12: scrolling menus must not switch PMCs.

11. In solo, keep observing for at least five minutes after death and extraction with the camera away from moving bots. Check PMC and Scav movement, stairs, doors and ground support. Open the spectator menu and wait through Killcam. `Spectator physics` should show increasing synchronization counts with `autoSync=False` and zero stale bounds.
12. With BotDebug 1.8.0, extract with bots still alive and confirm labels appear (not just the Enabled toggle). Move Freecam far from the extraction point and cycle PMCs: labels and their distances must follow the camera. Test manual enable with automatic activation off, death after Killcam, disabling/re-enabling BotDebug and a second raid. `BotDebug spectator data active` confirms the first populated snapshot; absence of this message with only dormant bots is not a rendering failure.

Look for `[HORIZON]` in `BepInEx/LogOutput.log`. In addition to entry/exit messages, a physics summary every 30 seconds records synchronization cost and stale collider bounds. Checks every 5 seconds report severe falls, movement without displacement and stale bounds (at most once per bot per 30 seconds). These observations do not change bot behavior. There is no per-frame file output.

## Interface scaling and zoom

- At 1080p, 1440p, ultrawide 1440p and 4K, check automatic size, manual 75-250%, buttons, text, scrollbars and drag hit areas. Move both panels, change resolution and scale, then restart: relative positions and preferences must persist.
- Use the menu's size controls and Reset. Resume/Leave raid must remain accessible while the settings area scrolls.
- Hold Left Ctrl and scroll in Freecam, locked follow and mouse orbit. Only the camera moves closer/further; the selected bot and player body stay unchanged. Following still respects nearby walls.
- Release Ctrl: the wheel switches bots again. Turn cycling off: ordinary Freecam wheel behavior remains available. Ctrl zoom must work even if Freecam's own wheel setting is off.
- Scroll over Escape/F12 or while holding Left Alt to move the panel: no camera zoom or bot switch. Check a custom shortcut, None, and holding movement keys while zooming. A second raid must reset temporary follow distance.
- Console checks cover layout arithmetic, wheel ownership, saved settings, camera movement and native DLL bindings. They do not render Unity IMGUI; real 4K presentation/input still needs an in-game check.

## Bot labels, roster and target death

- With several living bots, follow one and let it die, extract or despawn. The default must keep the camera at its last position and restore Freecam input without changing its angle. Enable automatic switching, repeat, and verify the wheel filter is respected. With no candidates left, both modes detach safely. Manual wheel/list navigation must still work afterward.
- On a fresh config, Bot info and Show bots through scenery are selected, BotDebug is off, the list is off and automatic target switching is off. A legacy automatic BotDebug preference must not override the Bot info default. An explicitly saved overlay choice wins. F12 changes and menu choices must agree; manually enabling BotDebug must suppress HORIZON labels until a new overlay choice. Leaving disables BotDebug without saving its temporary setting.
- HORIZON labels must follow Freecam, remain visible behind walls by default, stay absent outside the viewport/beyond the configured range, and disappear on death. Disable Show bots through scenery and verify walls hide labels again; re-enable it without restarting the raid. Text must have no background rectangle and remain readable against bright and dark scenery with its black outline. Check names, health and inventories against the observed bot. Values include all equipped inventory and stack quantities at handbook prices, not items looted during the raid or trader sale value. Health/actions update around twice a second, inventory around every five seconds, with work spread across frames.
- Test without optional mods and with SAIN/ORBIT/COMET. Verify a stale SAIN decision cannot cover ORBIT looting and a proposed COMET action cannot replace a different native action. Magazine retrieval, crossfire, cover-mate and postcombat phases should match RR. Read-only QB component integration and LB layer attribution are covered by contract doubles; real QB/LB installations still need an in-game check. No event queues are drained and no bot behavior is changed.
- Enable the list: squad members share a header/color, solo bots are separate from squads, and type/name are shown. Hold Alt or open Escape to select; click a Scav with the wheel still set to PMC-only, then attach using F. With follow already on, clicking another living bot keeps follow on. A stale dead row must not move the camera. Spawn, extraction, group changes and a second raid must update the list without retained scene references.
- Test scrolling/dragging at 1080p, ultrawide and 4K with help hidden, labels off, BotDebug on, and the menu overlapping the list. Clicks over the menu must not activate list rows behind it. F12 input stays reserved. Saved list position is independent of menu/help position.
- Logs are limited to integration availability, overlay choices, manual list visits and target-loss transitions; integration failures warn once per raid. No per-bot snapshot spam is added. Console tests do not validate native IMGUI rendering or real game performance.

## Fika

To include the co-op binary checks, run `dotnet run --project tests/Regression -c Release -- <SPT folder> <path to Fika.Core.dll>`. The camera harness exercises the spectator controls and server permissions with test doubles.

Run `./package.ps1 -SptRoot <SPT folder> -FikaPath <path to Fika.Core.dll>` to package the single installable `HORIZON.dll`. The packaging test loads the plugin directly from the ZIP with Fika unavailable, then loads the embedded transport with the supplied Fika library. The UDP loopback regression exercises a host and two peers, action changes, health and loot, guest eligibility, stale replies and an unmodified host.

For a live check, connect two graphical players to a hosted raid. Repeat with a headless host. Enable free-camera and bot spectating in the Fika server settings.

- While both players are alive, verify HORIZON stays inactive. After one dies or extracts, check that only their screen gets the controls and the other player can finish the raid. Verify the F6 menu, bot cycling, F3 labels and Fika's extract key.
- Install the same HORIZON build on the graphical or headless raid host and the spectating client. Watch one bot through two different actions. Check that its F3 action updates on the joining player's screen, including with an AI mod running on the host. Check damage and loot changes. Repeat after extraction, an ordinary death, reconnect and with a second spectator. On a graphical host, compare labels on the same bot when both cameras are available.
- Repeat with `allowSpectateFreeCam` on and `allowSpectateBots` off. F6 should open the menu and zoom should work. F3, follow, the list and wheel cycling should reveal no bots. Re-enable bot spectating on the next raid and verify that the player's saved overlay and list settings return.
- Turn off spectator freecam and verify that Fika keeps control. Check automatic extraction, transit, revive, disconnect and a second raid for stale overlays or input. Compare each player's saved outcome to their own exit.

## Spectator labels, FOV and extraction visuals

- With DynamicMaps installed, leave its minimap on, then die or extract. Fly to another floor, attach to a moving PMC, orbit it and detach. Check the local arrow, heading, floor and center hotkey in each mode. The corpse marker should stay at the death site. Repeat as a Fika guest while the battle HUD is hidden, toggle **Track spectator on DynamicMaps** off and on, then check the next raid while alive.

- Follow a teammate in Fika, press F, then repeat after selecting another human and a bot. Click a bot in HORIZON with a different native target selected. Each visit should use the latest selection. Repeat with bot spectating disabled. Only human labels and human follow should be available.
- Compare label distances from freecam, native Fika follow and HORIZON orbit. Toggle player labels and follow-only actions separately. Filtering actions should leave health and distance visible on other labels.
- Adjust the FOV slider, disable it mid-raid and return to gameplay. Check that the original FOV returns in each provider.
- Extract with a weapon equipped. Toggle weapon hiding and check the extraction point. Repeat with an item in hands and after death. Check that world loot and teammates' weapons are visible and the extracted loadout is intact.

## Solo raid lifecycle

The plugin delays the local death shutdown before `LocalGame.Stop` stops bot scenarios. It lets the real death callbacks, corpse creation and kill tracking run. Exit and timeout resume the native game shutdown and SPT profile-saving flow with the captured outcome. Extraction is intercepted at the same shutdown boundary after the game has decided Survived/Runner. The extracted body is removed from the world/AI player lists and deactivated (with its camera detached if necessary), without killing it or unloading its profile. Entry failures restore the touched state and allow the native ending. Freecam supplies the camera and its controls; it is not bundled or modified.

The optional Killcam integration observes its pending/playback state without changing the replay or its configuration. Camera ownership and spectator input start only after replay cleanup. If its API cannot be read, or a replay stays stuck for 120 seconds, the mod preserves the normal Killed exit instead of seizing the camera. This handoff has automated checks against the installed Killcam API; a real raid still needs validation.
