# Contributing

[Русский](CONTRIBUTING_RU.md)

## Build and test

Use Windows x64, the .NET 10 SDK, and the full [DCCM v35.14.1 Windows archive](https://github.com/dead-cells-core-modding/core/releases/tag/v35.14.1). The build needs the MDK, including its `packages` and `tools` directories. You can point to an existing game installation or to a separately extracted DCCM archive.

From the repository root in PowerShell:

```powershell
$env:DCCM_MDK_ROOT = 'C:\Games\Dead Cells\coremod\core\mdk'
dotnet --list-sdks
dotnet build -c Release
dotnet test -c Release --no-build
```

Replace the example path with yours. If your SDK is installed at `$env:USERPROFILE\.dotnet`, put that directory first on `Path` for this shell so both the build and the DCCM packaging tool use .NET 10:

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:Path = "$env:DOTNET_ROOT;$env:Path"
```

The packaged output is `src/bin/Release/net10.0/win-x64/output/DeadCellsMultiplayerMod/`. The build leaves your game installation alone. To run it, use the [installation guide](README.md#install) to copy the package and start the DCCM launcher. CI uses the same MDK version, runs the tests, and uploads the mod package.

## Architecture

1. **Game startup.** DCCM loads the package from `modinfo.json` and invokes [ModEntry](src/ModEntry/ModEntry.cs). The mod installs hooks into Dead Cells' existing game lifecycle and adds the multiplayer entry to the title screen.
2. **Lobby and transport.** [LobbySession](src/UI/ConnectionUI/LobbySession.cs) connects the title menu to [NetNode](src/server/server.cs). TCP listens on the host's IPv4 interfaces. Steam discovers the lobby and routes gameplay through Steam P2P. Both use the mod protocol, which checks build and protocol version at the handshake.
3. **Run startup.** [RunLaunchCoordinator](src/LaunchSync/RunLaunchCoordinator.cs) tracks the host's run settings and seed, the clients' acknowledgments, and the execute message. [GameDataSync](src/GameDataSync/GameDataSync.cs) synchronizes the level seed and graph, and [MultiplayerSaves](src/UI/ConnectionUI/MultiplayerSaves.cs) routes multiplayer saves to `save/MSave/`.
4. **During the run.** Each player controls their own hero. [Ghost](src/Ghost/) renders remote players. [Mobs](src/Mobs/) handles host-owned enemies and bosses, including state snapshots for clients. [Interaction](src/Interaction/) and [LevelExitSync](src/UI/LevelExitSync.cs) handle objects and transitions.

## Verify gameplay changes

The suite under `tests/` covers protocol, launch ordering, reconnect and synchronization regression cases. For in-game checks, run two installations with separate saves. Verify that both peers reach the lobby, mark Ready, start the same run, and transition together. Changes to Steam and LAN should be tested on their respective transport.
