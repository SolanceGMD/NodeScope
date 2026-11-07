# Building NodeScope

## Requirements
- Windows 10/11 x64
- .NET 8 SDK (Windows Desktop workload/reference packs)
- Godot 4.x project and Godot 4.x executable

## Build
From a Windows PowerShell terminal at the repository root:

```powershell
dotnet restore .\NodeScope.sln
dotnet publish .\src\NodeScope\NodeScope.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\win-x64
```

The executable is `artifacts\win-x64\NodeScope.exe`.

## Use
1. Open NodeScope, select the target project's `project.godot`.
2. Select the Godot 4 executable.
3. Press **Install addon**. This copies `addons/nodescope` into the project and enables the editor plugin in `project.godot`.
4. Run the game from the Godot editor or use **Launch game**.
5. Press **Attach** if automatic connection did not succeed.

The runtime addon listens on loopback only at TCP port 47831. It accepts newline-delimited JSON commands. Do not expose this port outside localhost. NodeScope is a development/debugging tool; use only in projects you own or are authorized to inspect.

## Current command surface
Scene tree, node inspection, position/rotation/scale/visibility and selected primitive dimensions, create box/sphere/cylinder/box mesh, duplicate/delete/rename, free camera toggle and speed. Runtime messages appear in the NodeScope log panel. Godot's full engine log stream is not currently bridged; the Logs action returns an explanatory status.

## Notes
This repository contains the source and a Windows publish recipe. A Windows build and in-engine smoke test are required to produce and verify a distributable EXE. NodeScope does not patch game binaries or bypass anti-cheat; it requires the addon to be installed in the Godot project.
