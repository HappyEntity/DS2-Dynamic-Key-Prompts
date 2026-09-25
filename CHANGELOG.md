# Changelog

## 1.0.1

- Fixed the game closing right after start with Seamless Co-op on some systems (crash
  `0xC0000026` in `ntdll.dll`). When the game is started by Seamless Co-op's launcher the mod
  waits for Seamless to finish loading and starts without its own Arxan workaround (dearxan).
- The .NET runtime of the mod only handles faults inside the mod, not exceptions of the game
  or other mods.

## 1.0.0

First release.

- Gamepad button prompts replaced with the keys and mouse buttons bound in the game, updated live
- Menu and world prompts resolved separately (Confirm/Cancel vs Interact/Roll, …)
- Key and mouse icons drawn into a copy of the game font; themes `dark`, `minimal`, `silver`,
  editable sprite sheets and custom themes
- Text label mode (`Style=text`)
- Compatible with Seamless Co-op, DS2 Lighting Engine and OptiScaler
- Two loaders: `dinput8.dll` (default) and `xinput1_3.dll` (optional, for setups where
  dinput8.dll is taken by another mod)
