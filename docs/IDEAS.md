# HORIZON ideas

Community suggestions from 2026-11-22, implemented locally. The spectator menu and F12 expose all four preferences. Bot actions are shown on every bot by default.

- **side** suggested a field of view (FOV) slider for the spectator camera.
- **Snoops** suggested labels for human players that show their name and health. Bot-specific details such as ORBIT actions would stay on bot labels.
- **Snoops** suggested showing distance on both player and bot labels. In freecam, measure from the camera. When following someone, measure from the player or bot being followed.
- **Snoops** also suggested an option to show actions only for the bot being followed.

## Fixes awaiting live playtesting

- **Snoops** reported that pressing **F** while spectating a living teammate in Fika kept following the same bot regardless of the selection. F now adopts the native selection before taking camera control. Recheck with a living teammate and two different bots.
- **side** noticed the weapon model stays at the extraction point. Chazut has also seen this and considers it cosmetic. The local spectator can now hide the held model after extraction. Recheck on both camera providers.
