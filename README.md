# Dead Cells Multiplayer Mod

[Русский](README_RU.md)

A Windows co-op mod for Dead Cells Update 35. Each player runs their own copy of the game through Dead Cells Core Modding (DCCM). The host runs the lobby and simulates the shared world. The other players join over LAN TCP or Steam P2P, with room for one host and up to three clients.

## Install

1. Install the [.NET 10 Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) and the [Microsoft Visual C++ Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe).
2. Download `win-x64.zip` from [DCCM v35.14.1](https://github.com/dead-cells-core-modding/core/releases/tag/v35.14.1). Extract it inside a folder named `coremod` alongside the game executable. The folder should contain `core`, `plugins`, and `ModCoreVersion.txt`.
3. Build the mod using the [build guide](CONTRIBUTING.md), or download the `DeadCellsMultiplayerMod` artifact from a successful [Build workflow run](https://github.com/AkrisLuno/Dead-Cells-multiplayer-mod/actions/workflows/build.yml) for the revision you want. Put the packaged files in `coremod/mods/DeadCellsMultiplayerMod/` so `modinfo.json`, the mod DLL, and `res.pak` are directly inside it. Use the same build on every PC.
4. Run `coremod/core/host/startup/DeadCellsModding.exe` from the game installation and select **Play multiplayer** on the title screen.

## Join over LAN or a virtual LAN

1. Connect every PC to the same local network or virtual LAN. On the host, choose **Play multiplayer → Host game**.
2. The host selects **Copy Room Code** and shares the result. Players on the same LAN can also use the displayed **LAN IP** and port. If some players cannot reach the network encoded by the room code, share the host's IPv4 address on the network they are using.
3. On a client, choose **Play multiplayer → Join game**. Use **Paste from Clipboard & Connect**, or enter the code or IPv4 address and then choose **Connect**. For a custom port, share the full code including its `:port` suffix, or use an address like `192.168.1.20:4321`.

The default port is TCP `1234`. **Steam & Advanced Options → Lan host** allows the host to change it. A LAN room code encodes an IPv4 address and includes the port when it differs from the default. Each client needs a route to that address.

## Join over Steam

1. Run Steam on both PCs and launch the Steam copy of Dead Cells through DCCM.
2. The host selects **Play multiplayer → Steam & Advanced Options → Steam host**, then chooses **Steam friends-only host** or **Steam public host**.
3. The host can use **Invite Steam friends**. The client can also open **Play multiplayer → Steam join** and select a friend's lobby. To use a shared Steam lobby code, copy it to the clipboard and select **Join by Steam lobby code (fallback)** on that page.

## Start the run

After everyone appears in the lobby, each player sets **Ready: On**. The host chooses **Save: Slot N**, then **Normal Mode** or **Custom Mode** for a new run, or **Continue** when the selected co-op save is compatible. The client follows the host into the run automatically.

Multiplayer slots are stored under `save/MSave/` in each game installation. See [troubleshooting](docs/TROUBLESHOOTING.md) for connection and startup checks, or [contributing](CONTRIBUTING.md#architecture) for how the mod hooks into the game.
