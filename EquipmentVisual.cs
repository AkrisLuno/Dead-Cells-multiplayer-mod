using System;
using System.Linq;

namespace DeadCellsMultiplayerMod;

internal enum EquipmentKind { Projectile, Grenade, Deployable, Companion }
internal enum SpriteAtlas { Effects, Weapon, Enemy, Level, Common, Cached }

internal sealed record EquipmentVisual(int Id, EquipmentKind Kind, SpriteAtlas Atlas, string Group,
    double X, double Y, double ScaleX, double ScaleY, double Rotation, double Alpha, int Frame, string? AtlasKey = null,
    CompanionTrail? Trail = null)
{
    internal const int MaxObjects = 32;
    internal bool IsValid => Id > 0 && Enum.IsDefined(Kind) && Enum.IsDefined(Atlas) &&
        Group is { Length: > 0 and <= 96 } && Group.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') &&
        Math.Abs(X) <= 50000 && Math.Abs(Y) <= 50000 &&
        Math.Abs(ScaleX) <= 16 && Math.Abs(ScaleY) <= 16 && Math.Abs(Rotation) <= 64 &&
        Alpha is >= 0 and <= 1 && Frame is >= 0 and <= 1024 &&
        (Trail is null || Kind == EquipmentKind.Companion && Trail.IsValid) &&
        (Atlas == SpriteAtlas.Cached
            ? AtlasKey is { Length: > 0 and <= 120 } && !AtlasKey.Contains("..", StringComparison.Ordinal) &&
              AtlasKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.')
            : AtlasKey is null);
}

internal sealed record CompanionTrail(int Color, double Alpha, double Thickness, double Duration, double OffsetX, double OffsetY)
{
    internal bool IsValid => Color is >= 0 and <= 0xFFFFFF && Alpha is > 0 and <= 1 &&
        Thickness is > 0 and <= 12 && Duration is > 0 and <= 4 &&
        Math.Abs(OffsetX) <= 128 && Math.Abs(OffsetY) <= 128;
}
