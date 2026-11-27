using DeadCellsMultiplayerMod.Ghost;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class CombatAndVisualSyncTests
{
    [Theory]
    [InlineData("punch", true)]
    [InlineData("w_sword_atk1", true)]
    public void AttackAnimationVocabulary_RecognizesUnarmedAndWeaponAttacks(string? anim, bool expected)
    {
        var result = CombatSyncHelpers.IsAttackAnim(anim!);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("idle", true)]
    [InlineData("land", false)]
    [InlineData("slash", false)]
    public void RemoteAnimationLooping_ExcludesLandingAndAttacks(string? anim, bool expected)
    {
        var result = CombatSyncHelpers.ShouldLoopRemoteAnim(anim!);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShieldRaiseSpatialization_MutesDistantShieldRaiseAndScalesNearby()
    {
        // 50 cases away (remote player far away): completely muted (null)
        var distantShield = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 50, 0, 1.0, CombatSyncHelpers.DefaultMaxSoundDistanceCases);
        Assert.Null(distantShield);

        // 30 cases away (beyond max distance of 26 cases): completely muted (null)
        var beyondMax = CombatSyncHelpers.CalculateSpatializedVolume(100, 200, 100, 230, 1.0, CombatSyncHelpers.DefaultMaxSoundDistanceCases);
        Assert.Null(beyondMax);

        // Right next to each other (co-located): full volume
        var adjacentShield = CombatSyncHelpers.CalculateSpatializedVolume(10, 10, 10, 10, 1.0, CombatSyncHelpers.DefaultMaxSoundDistanceCases);
        Assert.NotNull(adjacentShield);
        Assert.Equal(1.0, adjacentShield.Value, 2);

        // Intermediate distance (e.g. 13 cases): attenuated smoothly
        var midShield = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 13, 0, 1.0, CombatSyncHelpers.DefaultMaxSoundDistanceCases);
        Assert.NotNull(midShield);
        Assert.Equal(0.25, midShield.Value, 2);
    }

    [Fact]
    public void ResolveWeaponSlashFx_MapsStartSwordStrikesToCrescentWaveTrails()
    {
        // Starter Sword Strike 1: fxAtkBackstabberA / atkBackStabberA -> fxSaberA with white/orange gradient
        var s1 = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberA", "atkBackStabberA", 7901874, 7901874);
        Assert.Equal("fxSaberA", s1.resolvedFxId);
        Assert.Equal(16777215, s1.innerColor);
        Assert.Equal(16758355, s1.outerColor);

        // Starter Sword Strike 2: fxAtkBackstabberB / atkBackStabberB -> fxSaberB with white/orange gradient
        var s2 = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberB", "atkBackStabberB", 7901874, 7901874);
        Assert.Equal("fxSaberB", s2.resolvedFxId);
        Assert.Equal(16777215, s2.innerColor);
        Assert.Equal(16758355, s2.outerColor);

        // Deduce from animId when fxId is null or empty
        var s1AnimOnly = CombatSyncHelpers.ResolveWeaponSlashFx(null, "atkBackStabberA");
        Assert.Equal("fxSaberA", s1AnimOnly.resolvedFxId);

        var s2AnimOnly = CombatSyncHelpers.ResolveWeaponSlashFx("", "atkBackStabberB");
        Assert.Equal("fxSaberB", s2AnimOnly.resolvedFxId);
    }

    [Fact]
    public void ResolveWeaponSlashFx_PreservesCustomColors_ForNonStarterSwords()
    {
        // Weapons sharing atkBackStabber animations with custom colors (e.g., QuickSword 16367179) preserve their color
        var quickSword = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberA", "atkBackStabberA", 16367179, 16367179);
        Assert.Equal("fxSaberA", quickSword.resolvedFxId);
        Assert.Equal(16367179, quickSword.innerColor);
        Assert.Equal(16367179, quickSword.outerColor);

        var lowHealth = CombatSyncHelpers.ResolveWeaponSlashFx("fxAtkBackstabberB", "atkBackStabberB", 10084688, 10084688);
        Assert.Equal("fxSaberB", lowHealth.resolvedFxId);
        Assert.Equal(10084688, lowHealth.innerColor);
        Assert.Equal(10084688, lowHealth.outerColor);
    }

    [Theory]
    [InlineData("pipe", "crouch", true)]
    [InlineData("idle", "crouch", false)]
    public void IsLandingTransition_RecognizesCrouchAndPassageLandings(string? prevAnim, string? newAnim, bool expected)
    {
        Assert.Equal(expected, CombatSyncHelpers.IsLandingTransition(prevAnim, newAnim));
    }

    [Fact]
    public void StepSkillProjectile_IntegratesPositionAndGravity()
    {
        double x = 100.0;
        double y = 200.0;
        double vx = 5.0;
        double vy = -3.0;
        double gravity = 0.5;

        // Step 1
        CombatSyncHelpers.StepSkillProjectile(ref x, ref y, ref vx, ref vy, gravity);
        Assert.Equal(105.0, x, 3);
        Assert.Equal(197.0, y, 3);
        Assert.Equal(5.0, vx, 3);
        Assert.Equal(-2.5, vy, 3);

        // Step 2
        CombatSyncHelpers.StepSkillProjectile(ref x, ref y, ref vx, ref vy, gravity);
        Assert.Equal(110.0, x, 3);
        Assert.Equal(194.5, y, 3);
        Assert.Equal(5.0, vx, 3);
        Assert.Equal(-2.0, vy, 3);

        // Step 3
        CombatSyncHelpers.StepSkillProjectile(ref x, ref y, ref vx, ref vy, gravity);
        Assert.Equal(115.0, x, 3);
        Assert.Equal(192.5, y, 3);
        Assert.Equal(5.0, vx, 3);
        Assert.Equal(-1.5, vy, 3);
    }
}

