using System.Linq;

namespace DeadCellsMultiplayerMod;

internal sealed record ShopKey(string Level, int Seed, int Room, int X, int Y)
{
    internal bool IsValid => Level?.Length is > 0 and <= 96 && Room >= 0 &&
                             X is >= 0 and <= 4000 && Y is >= 0 and <= 4000;
}

internal sealed record RerollRequest(ShopKey Shop, int KnownRevision)
{
    internal bool IsValid => Shop is { IsValid: true } && KnownRevision is >= 0 and <= 64;
}

internal sealed record ShopStock(ShopKey Shop, int Revision, int Uses, bool Removed, BoothStock[] Booths)
{
    internal const int MaxShops = 8;
    internal bool IsValid => Shop is { IsValid: true } && Revision is > 0 and <= 64 &&
                             Uses is >= 0 and <= 64 && Booths is { Length: <= 6 } &&
                             Booths.All(booth => booth is { IsValid: true } &&
                                 booth.Y == Shop.Y && System.Math.Abs(booth.X - Shop.X) <= 35) &&
                             Booths.Select(booth => booth.X).Distinct().Count() == Booths.Length;
}

internal sealed record BoothStock(int X, int Y, int Price, ItemStock Item, ItemStock? Paired)
{
    internal bool IsValid => X is >= 0 and <= 4000 && Y is >= 0 and <= 4000 &&
                             Price is >= 0 and <= 1_000_000 && Item is { IsValid: true, Kind: 0 or 1 or 2 } &&
                             (Paired is null || Paired is { IsValid: true, Kind: 0 or 1 or 2 });
}

internal sealed record ItemStock(int Kind, string Id, int Level, int Ammo, string[] Affixes, string[] Inactive)
{
    internal bool IsValid => Kind is 0 or 1 or 2 or 5 && IsGameId(Id) && Level is >= 0 and <= 1000 &&
                             Ammo is >= -1 and <= 10000 && ValidAffixes(Affixes) && ValidAffixes(Inactive);

    private static bool ValidAffixes(string[]? affixes)
        => affixes is { Length: <= 12 } && affixes.All(IsGameId);

    private static bool IsGameId(string? id)
        => id is { Length: > 0 and <= 64 } &&
           id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
