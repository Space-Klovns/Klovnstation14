using System;
using System.Numerics;
using Content.Shared._KS14.Sensors;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Sensors;

/// <summary>
///     Dead-reckoning rules for the drawn contact position: only a live, moving,
///         position-fixed track advances along its last velocity, and never past the
///         staleness cap. Everything else must stay frozen at the last fix, or the
///         scope would invent motion the sensors never confirmed.
/// </summary>
[TestFixture]
public sealed class KsSensorContactStateTest
{
    private static KsSensorContactState MakeContact()
    {
        return new KsSensorContactState
        {
            WorldPosition = new Vector2(100f, 50f),
            LinearVelocity = new Vector2(10f, -4f),
            LastSeen = TimeSpan.FromSeconds(20),
            Live = true,
        };
    }

    [Test]
    public void LiveContactAdvancesAlongVelocity()
    {
        var contact = MakeContact();

        var estimated = contact.EstimatedPosition(TimeSpan.FromSeconds(20.5));

        Assert.That(estimated, Is.EqualTo(new Vector2(105f, 48f)));
    }

    [Test]
    public void ExtrapolationClampsAtStalenessCap()
    {
        var contact = MakeContact();

        // Way past the cap: the blip parks at LastSeen + cap instead of flying off.
        var estimated = contact.EstimatedPosition(TimeSpan.FromSeconds(120));

        var capped = contact.WorldPosition + contact.LinearVelocity * (float) KsSensorContactState.MaxDeadReckonSeconds;
        Assert.That(estimated, Is.EqualTo(capped));
    }

    [Test]
    public void ClockBehindLastSeenNeverExtrapolatesBackwards()
    {
        var contact = MakeContact();

        var estimated = contact.EstimatedPosition(TimeSpan.FromSeconds(19));

        Assert.That(estimated, Is.EqualTo(contact.WorldPosition));
    }

    [Test]
    public void GhostStaysFrozenAtLastFix()
    {
        var contact = MakeContact();
        contact.Live = false;

        var estimated = contact.EstimatedPosition(TimeSpan.FromSeconds(25));

        Assert.That(estimated, Is.EqualTo(contact.WorldPosition));
    }

    [Test]
    public void StaticContactNeverMoves()
    {
        var contact = MakeContact();
        contact.Static = true;

        var estimated = contact.EstimatedPosition(TimeSpan.FromSeconds(25));

        Assert.That(estimated, Is.EqualTo(contact.WorldPosition));
    }

    [Test]
    public void BearingContactHasNoPositionToAdvance()
    {
        var contact = MakeContact();
        contact.Quality = KsPositionQuality.Bearing;

        var estimated = contact.EstimatedPosition(TimeSpan.FromSeconds(25));

        Assert.That(estimated, Is.EqualTo(contact.WorldPosition));
    }
}
