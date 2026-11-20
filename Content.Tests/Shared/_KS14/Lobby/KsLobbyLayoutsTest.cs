using System;
using Content.Shared._KS14.Lobby;
using NUnit.Framework;

namespace Content.Tests.Shared._KS14.Lobby;

/// <summary>
///     The lobby's <c>auto</c> layout is Kharkiv on clown holidays and tactical otherwise, both ends of each range
///         included; a forced layout ignores the date.
/// </summary>
[TestFixture]
[TestOf(typeof(KsLobbyLayouts))]
[Parallelizable(ParallelScope.All)]
public sealed class KsLobbyLayoutsTest
{
    [Test]
    // April Fools', and two days after
    [TestCase(3, 31, false)]
    [TestCase(4, 1, true)]
    [TestCase(4, 3, true)]
    [TestCase(4, 4, false)]
    // International Clown Week
    [TestCase(7, 31, false)]
    [TestCase(8, 1, true)]
    [TestCase(8, 7, true)]
    [TestCase(8, 8, false)]
    // World Clown Day
    [TestCase(11, 4, false)]
    [TestCase(11, 5, true)]
    [TestCase(11, 6, false)]
    // The end of the year, across New Year
    [TestCase(12, 26, false)]
    [TestCase(12, 27, true)]
    [TestCase(12, 31, true)]
    [TestCase(1, 1, true)]
    [TestCase(1, 2, false)]
    // An ordinary day
    [TestCase(6, 15, false)]
    public void AutoFollowsTheClownCalendar(int month, int day, bool kharkiv)
    {
        var date = new DateTime(2026, month, day, 12, 0, 0);

        Assert.That(KsLobbyLayouts.IsKharkivSeason(date), Is.EqualTo(kharkiv));
        Assert.That(KsLobbyLayouts.Resolve(KsLobbyLayouts.Auto, date),
            Is.EqualTo(kharkiv ? KsLobbyLayouts.Kharkiv : KsLobbyLayouts.Tactical));
    }

    [Test]
    public void ForcedLayoutsIgnoreTheDate()
    {
        var aprilFools = new DateTime(2026, 4, 1);
        var ordinary = new DateTime(2026, 6, 15);

        Assert.Multiple(() =>
        {
            Assert.That(KsLobbyLayouts.Resolve(KsLobbyLayouts.Tactical, aprilFools), Is.EqualTo(KsLobbyLayouts.Tactical));
            Assert.That(KsLobbyLayouts.Resolve(KsLobbyLayouts.Kharkiv, ordinary), Is.EqualTo(KsLobbyLayouts.Kharkiv));
            Assert.That(KsLobbyLayouts.Resolve("nonsense", aprilFools), Is.EqualTo(KsLobbyLayouts.Kharkiv), "unknown ids act as auto");
            Assert.That(KsLobbyLayouts.Resolve("nonsense", ordinary), Is.EqualTo(KsLobbyLayouts.Tactical), "unknown ids act as auto");
        });
    }
}
