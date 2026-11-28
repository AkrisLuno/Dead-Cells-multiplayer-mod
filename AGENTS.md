# Dead Cells multiplayer mod

This repository builds a Dead Cells co-op mod through DCCM. Add gameplay features in small, verifiable steps.

## Default co-op workflow

- Use [Co-op Anything](https://github.com/cranebeaker/coop-anything/tree/836ec31f0926ee64f52b4699ae7aa5c613d1a1f7) by default for co-op design, implementation, and QA. Read its `AGENTS.md` and the relevant skills under `.agents/skills/`.
- Use `coop-build-from-zero` for the rebuild, `coop-networking` for transport and authority, and `coop-qa` and `coop-sync-testing` for verification. Check its Dead Cells notes against the game build on disk.
- Verify each layer before relying on it. Start with DCCM startup and a direct-IP LAN session, then check player replicas, world authority, interactions, transitions, saves, and reconnects. Use in-game test drivers that compare state across isolated game copies.

## Working rules

- Target Dead Cells Update 35, DCCM 35.14.1 and .NET 10 on Windows x64. Use the matching MDK by setting `DCCM_MDK_ROOT` to its `core/mdk` directory. Verify with `dotnet build -c Release`.
- If the repository root contains `.codegraph/`, use `codegraph_explore` or `codegraph explore` before searching or reading source. Let the user decide whether to create an index.
- Use the `frontend-design` skill before creating or visually redesigning a UI.
- Verify each native hook against the matching GameProxy or HashLink bytecode. Function indices can change across builds. Use the symbol names in `NATIVE_FUNCTIONS.md` as a lookup list.
- Keep engine objects and native calls on the game thread. Network workers hand validated messages to that thread.
- Give the host authority over world state, enemies, shop stock and level transitions. A client sends requests and renders authoritative state. Make repeated packets safe to apply and scope updates to the current session and level.
- Keep multiplayer saves in `save/MSave/`. Use the engine's local serializer context during save and load. Use separate scratch saves for testing.
- Build and run the relevant regression tests before shipping a change. Verify native gameplay changes with two game instances over LAN, with both instances muted by default. Test Steam separately for Steam changes.
- Start both isolated QA profiles from the same save baseline when testing seeded layouts. Require both games to exit normally and check that DCCM did not write a new crash report.
- Keep generated build outputs, game installations, logs and saves out of Git.
