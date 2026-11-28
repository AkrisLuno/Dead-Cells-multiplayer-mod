using System;
using System.Linq;

namespace DeadCellsMultiplayerMod;

internal enum EffectKind
{
    LandSmoke, LandDirt, LandHeavy, Dive, Dodge, Dash, Slash,
    ShieldGuard, ShieldParry, ShieldHit, Heal, Explosion, Frost, Smoke, Aura, Teleport, Deflagration, OilBurst
}

internal sealed record PlayerEffect(int Sequence, EffectKind Kind, double X, double Y,
    int Variant = 0, int? Color = null, int? OuterColor = null, double Scale = 1, WeaponTrail? Trail = null,
    double Extra = 0)
{
    internal const int MaxCues = 12;

    internal bool IsValid => Sequence > 0 && Math.Abs(X) <= 50000 && Math.Abs(Y) <= 50000 &&
                             Color is null or >= 0 and <= 0xFFFFFF && OuterColor is null or >= 0 and <= 0xFFFFFF &&
                             (Kind switch
                             {
                                 EffectKind.LandSmoke => Variant is >= -1 and <= 96,
                                 EffectKind.LandDirt => Variant is -1 or 0 or 1,
                                 EffectKind.LandHeavy => Variant is >= -1 and <= 96,
                                 EffectKind.Dive => Scale is > 0 and <= 6,
                                 EffectKind.Dodge => Variant is -1 or 1,
                                 EffectKind.Dash => Variant is -1 or 1 && Scale is > 0 and <= 6 && Math.Abs(X) <= 120,
                                 EffectKind.Slash => Trail is { IsValid: true } && Math.Abs(X) <= 160 &&
                                                     Math.Abs(Y) <= 160 && Scale is >= -8 and <= 8 and not 0,
                                 EffectKind.ShieldGuard => Math.Abs(Scale) <= 96,
                                 EffectKind.ShieldParry => Variant is >= -1 and <= 16,
                                 EffectKind.ShieldHit or EffectKind.Heal => true,
                                 EffectKind.Explosion => Scale is > 0 and <= 1024 && Extra is >= 0 and <= 120,
                                 EffectKind.Deflagration => Scale is > 0 and <= 480 && Extra is > 0 and <= 6 &&
                                                            Variant is 0 or 1,
                                 EffectKind.OilBurst => Scale is > 0 and <= 480,
                                 EffectKind.Frost => Scale is > 0 and <= 1024 && Variant is >= -1 and <= 1,
                                 EffectKind.Smoke => Scale is > 0 and <= 1024,
                                 EffectKind.Aura => Scale is > 0 and <= 1024 && Extra is >= 0 and <= 1,
                                 EffectKind.Teleport => Math.Abs(Scale) <= 1024 && Math.Abs(Extra) <= 1024 &&
                                                        Math.Abs(X) <= 1024 && Math.Abs(Y) <= 1024,
                                 _ => false
                             }) && (Kind == EffectKind.Slash || Trail is null);
}

internal sealed record WeaponTrail(string Group, double ScaleX, double ScaleY)
{
    internal bool IsValid => Group is { Length: > 0 and <= 96 } &&
                             Group.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') &&
                             Math.Abs(ScaleX) is > 0 and <= 8 && Math.Abs(ScaleY) is > 0 and <= 8;
}
