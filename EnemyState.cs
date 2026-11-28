using System;
using System.Collections.Generic;

namespace DeadCellsMultiplayerMod;

internal sealed record EnemyKey(string Kind, int CellX, int CellY, int Order)
{
    internal bool IsValid => EnemyState.ValidName(Kind) && CellX is >= 0 and <= 4000 &&
        CellY is >= 0 and <= 4000 && Order is >= 0 and < EnemyWorld.MaxEnemies;
}

internal sealed record EnemyImpact(int Revision, int Damage, int Direction, bool Critical)
{
    internal const int MaxDamage = 5_000_000;
    internal bool IsValid => Revision > 0 && Damage is > 0 and <= MaxDamage && Direction is -1 or 1;
}

internal sealed record EnemyState(EnemyKey Key, double X, double Y, int Direction, int Life, int MaxLife,
    string? Animation, int Frame, int DamageTier, int LifeTier, bool Removed, EnemyImpact? Impact)
{
    internal bool IsValid => Key is { IsValid: true } && Math.Abs(X) <= 50000 && Math.Abs(Y) <= 50000 &&
        Direction is -1 or 1 && MaxLife is > 0 and <= EnemyImpact.MaxDamage && Life >= 0 && Life <= MaxLife &&
        (Animation is null || ValidName(Animation)) && Frame is >= 0 and <= 1024 &&
        DamageTier is >= 0 and <= 256 && LifeTier is >= 0 and <= 256 && Impact is not { IsValid: false };

    internal static bool ValidName(string? name)
    {
        if (name is not { Length: > 0 and <= 96 })
            return false;
        foreach (var c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-')
                return false;
        return true;
    }
}

internal sealed record EnemyStrike(int Sequence, EnemyKey Attacker, double Power, int DamageType, double X, double Y)
{
    internal bool IsValid => Sequence > 0 && Attacker is { IsValid: true } &&
        Power is > 0 and <= EnemyImpact.MaxDamage && DamageType is >= 0 and <= 11 &&
        Math.Abs(X) <= 50000 && Math.Abs(Y) <= 50000;
}

internal sealed record EnemyWorld(LootLevel Level, int Acknowledged, EnemyState[] Enemies, EnemyStrike[]? Strikes = null)
{
    internal const int MaxEnemies = 500;
    internal const int MaxStrikes = 24;

    internal bool IsValid
    {
        get
        {
            if (Level is not { IsValid: true } || Acknowledged < 0 || Enemies is not { Length: <= MaxEnemies })
                return false;
            var keys = new HashSet<EnemyKey>();
            foreach (var enemy in Enemies)
                if (enemy is not { IsValid: true } || !keys.Add(enemy.Key))
                    return false;
            if (Strikes is { } strikes)
            {
                if (strikes.Length > MaxStrikes)
                    return false;
                var previous = 0;
                foreach (var strike in strikes)
                {
                    if (strike is not { IsValid: true } || strike.Sequence <= previous || !keys.Contains(strike.Attacker))
                        return false;
                    previous = strike.Sequence;
                }
            }
            return true;
        }
    }
}

internal sealed record EnemyHit(int Sequence, EnemyKey Target, int Damage, int Direction, bool Critical)
{
    internal bool IsValid => Sequence > 0 && Target is { IsValid: true } &&
        Damage is > 0 and <= EnemyImpact.MaxDamage && Direction is -1 or 1;
}

internal sealed record EnemyHits(LootLevel Level, EnemyHit[] Hits)
{
    internal const int MaxHits = 24;

    internal bool IsValid
    {
        get
        {
            if (Level is not { IsValid: true } || Hits is not { Length: > 0 and <= MaxHits })
                return false;
            var previous = 0;
            foreach (var hit in Hits)
            {
                if (hit is not { IsValid: true } || hit.Sequence <= previous)
                    return false;
                previous = hit.Sequence;
            }
            return true;
        }
    }
}

internal sealed record EnemySync(EnemyWorld? World = null, EnemyHits? Hits = null, int ReceivedStrike = 0)
{
    internal static readonly EnemySync Empty = new();

    internal bool IsValidFor(bool receiverIsHost) => receiverIsHost
        ? World is null && Hits is not { IsValid: false } && ReceivedStrike >= 0
        : Hits is null && World is not { IsValid: false } && ReceivedStrike == 0;
}
