using System;

namespace DeadCellsMultiplayerMod;

internal enum DropKind
{
    Gold, Gem, Cell, BonusCell, Heal, ComboGold, ComboGem, Item
}

internal sealed record MobDrop(DropKind Kind, int Value = 0, double Heal = 0, ItemStock? Item = null, ItemStock? Paired = null)
{
    internal bool IsValid => Kind == DropKind.Item
        ? Value == 0 && Heal == 0 && Item is { IsValid: true } && (Paired is null || Paired is { IsValid: true, Kind: 0 or 1 or 2 })
        : Item is null && Paired is null && Kind switch
    {
        DropKind.Gold or DropKind.Gem or DropKind.ComboGold or DropKind.ComboGem =>
            Value is > 0 and <= 1_000_000 && Heal == 0,
        DropKind.Cell or DropKind.BonusCell => Value == 0 && Heal == 0,
        DropKind.Heal => Value == 0 && Heal is > 0 and <= 1,
        _ => false
    };
}

internal sealed record LootLevel(string Name, int Seed, ulong Layout)
{
    internal bool IsValid => Name?.Length is > 0 and <= 96 && Layout != 0;
    internal bool Matches(PlayerState? player)
        => player is not null && Name == player.Level && Seed == player.LevelSeed && Layout == player.Layout;
}

internal sealed record DropBatch(int Sequence, int X, int Y, double? Delay, MobDrop[] Drops)
{
    internal const int MaxDrops = 6;

    internal bool IsValid => Sequence > 0 && X is >= 0 and <= 4000 && Y is >= 0 and <= 4000 &&
                             (Delay is null or >= 0 and <= 3600) &&
                             Drops is { Length: > 0 and <= MaxDrops } &&
                             Array.TrueForAll(Drops, drop => drop is { IsValid: true });
}

internal sealed record DropFrame(LootLevel Level, DropBatch[] Batches)
{
    internal const int MaxBatches = 4;

    internal bool IsValid
    {
        get
        {
            if (Level is not { IsValid: true } || Batches is not { Length: > 0 and <= MaxBatches })
                return false;
            var previous = 0;
            foreach (var batch in Batches)
            {
                if (batch is not { IsValid: true } || batch.Sequence <= previous)
                    return false;
                previous = batch.Sequence;
            }
            return true;
        }
    }
}

internal sealed record DropReceipt(LootLevel Level, int Sequence)
{
    internal bool IsValid => Level is { IsValid: true } && Sequence > 0;
}
