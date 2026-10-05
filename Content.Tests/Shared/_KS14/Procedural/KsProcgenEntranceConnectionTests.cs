using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenEntranceConnectionTests
{
    [Test]
    public void AlternativesRemainCompleteSeparateContractsWithoutUnioningTheirGroups()
    {
        Assert.That(KsProcgenEntranceShorthand.TryImport("1-3-4;9-8\n1-8;3-4-9", ["First", "Second"], out var imported, out var issue), Is.True, issue?.Code);
        var contract = Normalize(imported);
        Assert.That(contract.Status, Is.EqualTo(KsProcgenStatus.Success), contract.Issue?.Code);
        Assert.That(contract.Configurations.Count, Is.EqualTo(2));
        var first = contract.Configurations.Single(configuration => configuration.Id == "First");
        var second = contract.Configurations.Single(configuration => configuration.Id == "Second");
        Assert.That(first.Groups.Single(group => group.Ports.Contains("1")).Ports, Is.EqualTo(new[] { "1", "3", "4" }));
        Assert.That(second.Groups.Single(group => group.Ports.Contains("1")).Ports, Is.EqualTo(new[] { "1", "8" }));
        Assert.That(first.Mode, Is.EqualTo(KsProcgenEntranceConnectionMode.ExactComponents));
        Assert.That(first.IsolationScope, Is.EqualTo(KsProcgenEntranceIsolationScope.DomainLocal));
    }

    [Test]
    public void ShorthandWhitespaceAndMembershipOrderMatchCanonicalDataAndDetachedReplay()
    {
        Assert.That(KsProcgenEntranceShorthand.TryImport(" 4 - 3 - 1 ; 8 - 9 \r\n", ["Choice"], out var imported, out _), Is.True);
        Assert.That(KsProcgenEntranceShorthand.TryImport("9-8;1-3-4", ["Choice"], out var reordered, out _), Is.True);
        var canonical = new KsProcgenEntranceConfigurationSpec
        {
            Id = "Choice", Groups = imported[0].Groups.Select(group => new KsProcgenEntranceGroupSpec
                { Id = group.Id, Ports = group.Ports.AsEnumerable().Reverse().ToList() }).Reverse().ToList(),
        };
        var contract = Normalize(imported);
        Assert.That(Normalize(reordered).ContractHash, Is.EqualTo(contract.ContractHash));
        Assert.That(Normalize([canonical], entrances: Ports().Reverse().ToArray()).ContractHash, Is.EqualTo(contract.ContractHash));
        imported[0].Groups[0].Ports.Clear();
        Assert.That(contract.Configurations.Single().Groups.Sum(group => group.Ports.Count), Is.EqualTo(5));
    }

    [Test]
    public void EveryPortRequiresExactlyOneKnownDispositionEvenInDisabledAlternatives()
    {
        AssertFailure(Normalize([Configuration("All", ["1", "3", "4", "8"])]), "MissingEntranceDisposition");
        AssertFailure(Normalize([Configuration("All", ["1", "3", "4", "8", "9", "Unknown"])]), "InvalidEntranceMembership");
        AssertFailure(Normalize([Configuration("All", ["1", "3", "4", "8", "9", "1"])]), "InvalidEntranceMembership");
        var disabled = Configuration("Disabled", ["1"]);
        disabled.Weight = 0f;
        AssertFailure(Normalize([Configuration("All", ["1", "3", "4", "8", "9"]), disabled]), "MissingEntranceDisposition");
    }

    [Test]
    public void SealingRequiresPermissionAndCannotAlsoGroupTheSamePort()
    {
        var configuration = Configuration("Seal", ["1", "3", "4", "8"]);
        configuration.SealedPorts = ["9"];
        AssertFailure(Normalize([configuration]), "InvalidEntranceSealing");
        var ports = Ports().Select(port => port with { OptionalSealable = port.PortId == "9" }).ToArray();
        var accepted = Normalize([configuration], entrances: ports);
        Assert.That(accepted.Status, Is.EqualTo(KsProcgenStatus.Success));
        Assert.That(accepted.Configurations.Single().SealedPorts, Is.EqualTo(new[] { "9" }));
        configuration.Groups[0].Ports.Add("9");
        AssertFailure(Normalize([configuration], entrances: ports), "InvalidEntranceSealing");
    }

    [Test]
    public void GlobalSingleNetworkRejectsMultipleRequestWideExactGroupsButAllowsLocalOrRequiredGroups()
    {
        Assert.That(KsProcgenEntranceShorthand.TryImport("1-3-4;9-8", ["Choice"], out var imported, out _,
            isolationScope: KsProcgenEntranceIsolationScope.RequestNetwork), Is.True);
        AssertFailure(Normalize(imported), "ContradictoryEntranceNetworks");
        imported[0].Mode = KsProcgenEntranceConnectionMode.RequiredConnections;
        Assert.That(Normalize(imported).Status, Is.EqualTo(KsProcgenStatus.Success));
        imported[0].Mode = KsProcgenEntranceConnectionMode.ExactComponents;
        imported[0].IsolationScope = KsProcgenEntranceIsolationScope.DomainLocal;
        Assert.That(Normalize(imported).Status, Is.EqualTo(KsProcgenStatus.Success), "Host attachment remains unverified, not fabricated here.");
    }

    [Test]
    public void InvalidWeightsEmptyEligibleSetsAndMalformedDefinitionsFailAtomically()
    {
        foreach (var weight in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            var configuration = Configuration("Choice", ["1", "3", "4", "8", "9"]);
            configuration.Weight = weight;
            AssertFailure(Normalize([configuration]), "InvalidEntranceConfiguration");
        }
        var disabled = Configuration("Choice", ["1", "3", "4", "8", "9"]);
        disabled.Weight = 0f;
        AssertFailure(Normalize([disabled]), "NoEligibleEntranceConfigurations");
        AssertFailure(Normalize([disabled, disabled]), "InvalidEntranceConfiguration");
        AssertFailure(Normalize([]), "InvalidEntranceConnectionInput");
        AssertFailure(Normalize(Enumerable.Range(0, 65).Select(index => Configuration($"Choice{index}", ["1", "3", "4", "8", "9"])).ToArray()), "InvalidEntranceConnectionInput");
        var malformed = Configuration("Choice", ["1", "3", "4", "8", "9"]);
        malformed.Groups[0].Ports = [null!];
        AssertFailure(Normalize([malformed]), "InvalidEntranceMembership");
    }

    [Test]
    public void ReplayIncludesWeightsModesAndSealPermissionButCanonicalizesZeroWeight()
    {
        var configuration = Configuration("Choice", ["1", "3", "4", "8", "9"]);
        var original = Normalize([configuration]);
        configuration.Weight = 2f;
        Assert.That(Normalize([configuration]).ContractHash, Is.Not.EqualTo(original.ContractHash));
        configuration.Weight = 1f;
        configuration.Mode = KsProcgenEntranceConnectionMode.RequiredConnections;
        Assert.That(Normalize([configuration]).ContractHash, Is.Not.EqualTo(original.ContractHash));
        configuration.Mode = KsProcgenEntranceConnectionMode.ExactComponents;
        Assert.That(Normalize([configuration], entrances: Ports().Select(port => port with { OptionalSealable = true }).ToArray()).ContractHash,
            Is.Not.EqualTo(original.ContractHash));
        var disabled = Configuration("Disabled", ["1", "3", "4", "8", "9"]);
        disabled.Weight = 0f;
        var positiveZero = Normalize([configuration, disabled]);
        disabled.Weight = -0f;
        Assert.That(Normalize([disabled, configuration]).ContractHash, Is.EqualTo(positiveZero.ContractHash));
    }

    [Test]
    public void ShorthandRejectsEmptySegmentsDuplicatesDelimitersAndUnpersistedIds()
    {
        foreach (var text in new[] { "", "1--3", "1-3;", ";1-3", "1-3;3-4", "1-3\n\n4", "1.$", "1:3" })
        {
            Assert.That(KsProcgenEntranceShorthand.TryImport(text, ["Choice"], out var imported, out var issue), Is.False, text);
            Assert.That(imported, Is.Empty);
            Assert.That(issue?.Code, Is.EqualTo("InvalidEntranceShorthand"));
        }
        Assert.That(KsProcgenEntranceShorthand.TryImport("1\n3", ["Repeated", "Repeated"], out _, out _), Is.False);
        Assert.That(KsProcgenEntranceShorthand.TryImport("1\n3", ["OnlyOne"], out _, out _), Is.False);
        Assert.That(KsProcgenEntranceShorthand.TryImport(new string('x', 131073), ["Choice"], out _, out _), Is.False);
    }

    [Test]
    public void SingletonMembershipIsPreservedAndAreaProfileReferencesAreScopedToEachBlob()
    {
        var single = KsProcgenEntranceConnectionNormalizer.Normalize("Blob", [Ports()[0]], [Configuration("Only", ["1"])],
            KsProcgenConnectivityPolicy.SingleNetwork);
        Assert.That(single.Configurations.Single().Groups.Single().Ports, Is.EqualTo(new[] { "1" }));
        var profile = new KsProcgenAreaProfile("Office", KsProcgenMode.Procedural, KsProcgenGeometryMode.Footprint,
            KsProcgenConnectivityPolicy.SingleNetwork, null, 65536, 1000000, EntranceConnections: "Connections");
        var markers = new KsProcgenAreaMarker[] { new(new(0, 0), "Default", "Office"), new(new(0, 3), "Default", "Office") };
        var entrances = new KsProcgenEntranceMarker[]
        {
            new("1", "Default", [new(-1, 0)], new(1, 0)), new("1", "Default", [new(-1, 3)], new(1, 0)),
        };
        Assert.That(KsProcgenAreaNormalizer.Normalize("Host", markers, [profile], entrances: entrances).Issue?.Code,
            Is.EqualTo("InvalidEntranceConnectionProfile"));
        var result = KsProcgenAreaNormalizer.Normalize("Host", markers, [profile], entrances: entrances,
            connectionProfiles: new Dictionary<string, IReadOnlyList<KsProcgenEntranceConfigurationSpec>> { ["Connections"] = [Configuration("Only", ["1"])] });
        Assert.That(result.Status, Is.EqualTo(KsProcgenStatus.Success), result.Issue?.Code);
        Assert.That(result.Blobs.Count, Is.EqualTo(2));
        Assert.That(result.Blobs.All(blob => blob.ConnectionContract?.Configurations.Count == 1), Is.True);
        Assert.That(result.Blobs[0].ConnectionContract!.ContractHash, Is.Not.EqualTo(result.Blobs[1].ConnectionContract!.ContractHash));
    }

    private static KsProcgenEntranceConfigurationSpec Configuration(string id, IReadOnlyList<string> ports) => new()
    {
        Id = id, Groups = [new() { Id = "Group", Ports = ports.ToList() }],
    };

    private static KsProcgenAreaEntrance[] Ports() => new[] { "1", "3", "4", "8", "9" }.Select(port =>
        new KsProcgenAreaEntrance("Blob", port, "Default", new Vector2i[] { new(-1, 0) }, [new(0, 0)], [new(-2, 0)], new(1, 0), false)).ToArray();

    private static KsProcgenEntranceConnectionContract Normalize(IReadOnlyList<KsProcgenEntranceConfigurationSpec> configurations,
        IReadOnlyList<KsProcgenAreaEntrance>? entrances = null) => KsProcgenEntranceConnectionNormalizer.Normalize("Blob", entrances ?? Ports(),
        configurations, KsProcgenConnectivityPolicy.SingleNetwork);

    private static void AssertFailure(KsProcgenEntranceConnectionContract result, string code)
    {
        Assert.That(result.Status, Is.EqualTo(KsProcgenStatus.InvalidInput));
        Assert.That(result.Issue?.Code, Is.EqualTo(code));
        Assert.That(result.Configurations, Is.Empty);
        Assert.That(result.ContractHash, Is.Zero);
    }
}
