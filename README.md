# NodeScope

**Experimental Windows x64 runtime injector for Godot games.**

The launcher lists visible running application windows and can request that the native NodeScopeHook.dll be loaded into the selected process. The current native hook displays an in-game status panel; press **=** to hide/show it.

## Current build scope

- [x] Separate Windows launcher
- [x] Running-window/process picker
- [x] Native x64 DLL loading into the selected process
- [x] In-process overlay status panel with = toggle
- [ ] Godot 4.7.2 scene-tree discovery
- [ ] Runtime node/property editing and object creation

**Important:** this is an injection/overlay prototype, not yet the completed UnityExplorer-style runtime inspector. The overlay explicitly reports that the Godot scene bridge is not connected. It does not read or modify project files, install an addon, or persist game-session changes. The scene-tree bridge requires compatible engine integration and is not implemented in this build.

## Build

On Windows with .NET 8 SDK, CMake, and Visual Studio C++ Build Tools, build the CMake DLL and publish the .NET launcher. The GitHub Actions workflow builds both as the NodeScope-experimental-win-x64 artifact.

## Use

1. Start a windowed Godot game.
2. Run NodeScope.exe as a user with permission to access that process.
3. Select the game window and click **Inject NodeScope**.
4. Press **=** in the game to hide or show the status overlay.

The launcher and hook must both be x64. Injection can fail for elevated games, protected processes, incompatible architectures, or games whose windowing/rendering setup does not support this simple top-level overlay.