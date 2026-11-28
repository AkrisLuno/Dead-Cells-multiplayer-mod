using ModCore.Events.Interfaces.Game;
using ModCore.Mods;

namespace DeadCellsMultiplayerMod;

public sealed class ModEntry(ModInfo info) : ModBase(info), IOnGameEndInit
{
    public override void Initialize()
    {
        Logger.Information("Multiplayer mod initialized.");
    }

    public void OnGameEndInit()
    {
        Logger.Information("Multiplayer mod reached game initialization.");
    }
}
