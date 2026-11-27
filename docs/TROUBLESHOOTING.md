# Troubleshooting

## The launcher closes or Play multiplayer is missing

- Confirm the directory is named `coremod` and sits alongside the game executable.
- Confirm `coremod/mods/DeadCellsMultiplayerMod/modinfo.json` is present directly inside the mod folder.
- Start `coremod/core/host/startup/DeadCellsModding.exe`. In `coremod/logs/log_latest.log`, look for the mod load line and `[NetMod] Source build`.
- This project uses DCCM v35.14.1 with .NET 10 x64. Install the Visual C++ x64 redistributable if Windows reports a missing native runtime. See [installation](../README.md#install).

## A client cannot join a LAN host

- Check that the host is still on the lobby screen and that both players installed the same build.
- Use the host's IPv4 address on the network both PCs can access. `127.0.0.1` reaches only the same machine. A room code uses one of the host's addresses, so use the matching LAN or VPN IP directly when the room code routes elsewhere.
- Test from the client while the host is listening. Substitute the displayed address and port:

  ```powershell
  Test-NetConnection -ComputerName 192.168.1.20 -Port 1234
  ```

  If `TcpTestSucceeded` is false, check that the client can route to the address and that the host firewall permits inbound TCP on the listening port for the relevant network.

## A Steam lobby is missing

- Run the Steam version of the game with Steam running on both PCs. Check the DCCM log for Steam API initialization errors.
- Try the host's invite, the friend list under **Steam join**, or the code copied to the clipboard under **Join by Steam lobby code (fallback)**.
- If you have a GOG installation, use [LAN or virtual LAN](../README.md#join-over-lan-or-a-virtual-lan).

## The host cannot start or continue

- Check that the host and each connected client have set **Ready: On**.
- **Continue** requires a compatible multiplayer save. Try a new run in **Normal Mode** to verify the connection first.
- A missing `save/MSave/user_N.dat` during slot listing can mean the slot has never been saved. Check whether the game shows the slot as empty and stays open before treating the message as a crash.
