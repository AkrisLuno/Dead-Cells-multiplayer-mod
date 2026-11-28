# Dead Cells multiplayer mod

This repository starts with a DCCM module and a list of native symbols. Add gameplay features in small, verifiable steps.

## Working rules

- Target Dead Cells Update 35, DCCM 35.14.1 and .NET 10 on Windows x64. Use the matching MDK by setting `DCCM_MDK_ROOT` to its `core/mdk` directory. Verify with `dotnet build -c Release`.
- If the repository root contains `.codegraph/`, use `codegraph_explore` or `codegraph explore` before searching or reading source. Let the user decide whether to create an index.
- Use the `frontend-design` skill before creating or visually redesigning a UI.
- Verify each native hook against the matching GameProxy or HashLink bytecode. Function indices can change across builds. Use the symbol names in `NATIVE_FUNCTIONS.md` as a lookup list.
- Keep engine objects and native calls on the game thread. Network workers hand validated messages to that thread.
- Give the host authority over world state, enemies, shop stock and level transitions. A client sends requests and renders authoritative state. Make repeated packets safe to apply and scope updates to the current session and level.
- Keep multiplayer saves in `save/MSave/`. Use the engine's local serializer context during save and load. Use separate scratch saves for testing.
- Build and run the relevant regression tests before shipping a change. Verify native gameplay changes with two game instances over LAN, with both instances muted by default. Test Steam separately for Steam changes.
- Keep generated build outputs, game installations, logs and saves out of Git.
