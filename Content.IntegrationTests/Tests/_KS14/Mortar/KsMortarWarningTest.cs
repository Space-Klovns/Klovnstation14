#nullable enable
using System;
using Content.IntegrationTests.Fixtures;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.SupplyPod.LandingZone;
using Content.Shared._KS14.SupplyPod.StrikeWarning;
using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Mortar;

/// <summary>
///     Content: a mortar shell and a cruise missile each mark the ground with a warning of their own, each sized to its own
///         blast, the mortar's the gentler of the two.
/// </summary>
[TestFixture]
[TestOf(typeof(KsStrikeWarningComponent))]
public sealed class KsMortarWarningTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly EntProtoId MortarShell = "KsMortarShell";
    private static readonly EntProtoId Missile = "SupplyPodMissileNtExplosive";

    [Test]
    public async Task AShellAndAMissileEachMarkTheirOwnBlast()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid shellUid = default, missileUid = default;

        await Server.WaitPost(() =>
        {
            shellUid = SEntMan.SpawnEntity(MortarShell, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            missileUid = SEntMan.SpawnEntity(Missile, new EntityCoordinates(testMap.Grid, 30.5f, 0.5f));
        });

        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var shellWarningUid = AssertWarning(shellUid, out var shellWarning, out var shellBlast);
            var missileWarningUid = AssertWarning(missileUid, out var missileWarning, out var missileBlast);

            Assert.That(SEntMan.GetComponent<MetaDataComponent>(shellWarningUid).EntityPrototype?.ID,
                Is.Not.EqualTo(SEntMan.GetComponent<MetaDataComponent>(missileWarningUid).EntityPrototype?.ID),
                "a mortar shell should be marked otherwise than a cruise missile");
            Assert.That(shellBlast, Is.Not.EqualTo(missileBlast).Within(0.01f), "precondition: blasts of different sizes");

            // The lesser threat: pulsing slower all the way, and never as faint.
            Assert.That(shellWarning.PulseStartFrequency, Is.LessThan(missileWarning.PulseStartFrequency), "the mortar's should pulse slower at first");
            Assert.That(shellWarning.PulseEndFrequency, Is.LessThan(missileWarning.PulseEndFrequency), "the mortar's should pulse slower at the end");
            Assert.That(shellWarning.PulseMinimumAlpha, Is.GreaterThan(missileWarning.PulseMinimumAlpha), "the mortar's should never fade as far");

            // Off before either lands.
            SEntMan.DeleteEntity(shellUid);
            SEntMan.DeleteEntity(missileUid);
        });

        EntityUid AssertWarning(EntityUid podUid, out KsStrikeWarningComponent warning, out float blastRadius)
        {
            var indicatorUid = SEntMan.GetComponent<SupplyPodLandingZoneComponent>(podUid).IndicatorEntity;
            Assert.That(indicatorUid, Is.Not.Null, $"{SEntMan.ToPrettyString(podUid)} should mark where it lands");
            Assert.That(SEntMan.TryGetComponent(indicatorUid!.Value, out KsStrikeWarningComponent? warningComponent), Is.True,
                $"{SEntMan.ToPrettyString(podUid)} should mark it as a strike");
            warning = warningComponent!;

            var explosionComponent = SEntMan.GetComponent<ExplosionOnTriggerComponent>(podUid);
            blastRadius = SEntMan.System<ExplosionSystem>().IntensityToRadius(
                explosionComponent.TotalIntensity,
                explosionComponent.IntensitySlope,
                explosionComponent.MaxTileIntensity);
            Assert.That(warning.Radius, Is.EqualTo(MathF.Max(blastRadius, warning.MinimumRadius)).Within(0.001f),
                $"{SEntMan.ToPrettyString(podUid)}'s warning should be sized to its own blast");
            return indicatorUid.Value;
        }
    }
}
