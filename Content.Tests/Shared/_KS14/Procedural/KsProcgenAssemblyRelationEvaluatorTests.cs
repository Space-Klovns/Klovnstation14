using System.Collections.Generic;
using Content.Shared._KS14.Procedural;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

[TestFixture]
public sealed class KsProcgenAssemblyRelationEvaluatorTests
{
    [Test]
    public void CornerRequiresTwoPerpendicularWallsAtTheSameFootprintCorner()
    {
        Assert.That(KsProcgenAssemblyRelationEvaluator.AtCorner([new(0, 0)],
            new HashSet<Vector2i> { new(-1, 0), new(0, -1) }), Is.True);
        Assert.That(KsProcgenAssemblyRelationEvaluator.AtCorner([new(0, 0)],
            new HashSet<Vector2i> { new(-1, 0), new(1, 0) }), Is.False);
        Assert.That(KsProcgenAssemblyRelationEvaluator.AtCorner([new(0, 0), new(2, 0)],
            new HashSet<Vector2i> { new(-1, 0), new(2, 1) }), Is.False);
        Assert.That(KsProcgenAssemblyRelationEvaluator.AtCorner([new(0, 0), new(1, 0)],
            new HashSet<Vector2i> { new(1, 0), new(0, 1) }), Is.False,
            "A tile occupied by this member cannot be its own backing wall.");
    }

    [TestCase(KsProcgenRelationSeverity.Preferred, true)]
    [TestCase(KsProcgenRelationSeverity.Required, false)]
    public void CornerMissKeepsSeverityAndGeometricWitness(KsProcgenRelationSeverity severity, bool valid)
    {
        var relation = new KsProcgenAssemblyRelation
        {
            Id = "Corner", Subject = "Table", Kind = KsProcgenRelationKind.AtCorner, Severity = severity,
        };
        var evaluation = Evaluate(relation, [Proposal("Table", new(2, 2))]);
        Assert.That(evaluation.RequiredSatisfied, Is.EqualTo(valid));
        var witness = evaluation.Witnesses[0];
        Assert.That(witness.State, Is.EqualTo(KsProcgenConstraintState.Missed));
        Assert.That(witness.Severity, Is.EqualTo(severity));
        Assert.That(witness.SubjectCell, Is.EqualTo(new Vector2i(2, 2)));
        Assert.That(witness.ReasonCode, Is.EqualTo("RelationGeometryUnmet"));
        Assert.That(evaluation.PreferredApplicable, Is.EqualTo(valid ? 1 : 0));
    }

    [Test]
    public void OmittedOptionalSubjectIsNotAnEarnedPreference()
    {
        var relation = new KsProcgenAssemblyRelation
        {
            Id = "OptionalFacing", Subject = "Seat", Target = "Table",
            Kind = KsProcgenRelationKind.FacingTarget, Severity = KsProcgenRelationSeverity.Preferred,
        };
        var evaluation = Evaluate(relation, [Proposal("Table", new(0, 0))], optionalSeat: true);
        Assert.That(evaluation.RequiredSatisfied, Is.True);
        Assert.That(evaluation.Witnesses[0].State, Is.EqualTo(KsProcgenConstraintState.NotApplicable));
        Assert.That(evaluation.Witnesses[0].ReasonCode, Is.EqualTo("OptionalMemberOmitted"));
        Assert.That(evaluation.PreferredSatisfied, Is.Zero);
        Assert.That(evaluation.PreferredApplicable, Is.Zero);
    }

    [Test]
    public void MissingPreferredTargetDoesNotBecomeAHardFailure()
    {
        var relation = new KsProcgenAssemblyRelation
        {
            Id = "Facing", Subject = "Table", Target = "Seat",
            Kind = KsProcgenRelationKind.FacingTarget, Severity = KsProcgenRelationSeverity.Preferred,
        };
        var evaluation = Evaluate(relation, [Proposal("Table", new(0, 0))], optionalSeat: true);
        Assert.That(evaluation.RequiredSatisfied, Is.True);
        Assert.That(evaluation.Witnesses[0].State, Is.EqualTo(KsProcgenConstraintState.Missed));
        Assert.That(evaluation.Witnesses[0].ReasonCode, Is.EqualTo("RelationTargetOmitted"));
    }

    [Test]
    public void DiagonalAdjacencyAndFacingNeverQualify()
    {
        foreach (var kind in new[] { KsProcgenRelationKind.AdjacentTo, KsProcgenRelationKind.FacingTarget })
        {
            var relation = new KsProcgenAssemblyRelation
            {
                Id = "Spatial", Subject = "Seat", Target = "Table", Kind = kind,
            };
            var evaluation = Evaluate(relation,
                [Proposal("Table", new(0, 0)), Proposal("Seat", new(1, 1))]);
            Assert.That(evaluation.RequiredSatisfied, Is.False);
            Assert.That(evaluation.Witnesses[0].State, Is.EqualTo(KsProcgenConstraintState.Missed));
        }
    }

    [Test]
    public void NearWithoutWalkGeometryIsUnverifiedEvenWhenPreferred()
    {
        var relation = new KsProcgenAssemblyRelation
        {
            Id = "Near", Subject = "Seat", Target = "Table", Kind = KsProcgenRelationKind.Near,
            Severity = KsProcgenRelationSeverity.Preferred,
        };
        var evaluation = Evaluate(relation,
            [Proposal("Table", new(0, 0)), Proposal("Seat", new(1, 0))]);
        Assert.That(evaluation.RequiredSatisfied, Is.False);
        Assert.That(evaluation.Witnesses[0].State, Is.EqualTo(KsProcgenConstraintState.Unverified));
        Assert.That(evaluation.Witnesses[0].ReasonCode, Is.EqualTo("NearGeometryUnavailable"));
    }

    private static KsProcgenAssemblyRelationEvaluation Evaluate(KsProcgenAssemblyRelation relation,
        IReadOnlyList<KsProcgenEntityProposal> proposals, bool optionalSeat = false)
    {
        var members = new List<KsProcgenAssemblyMember>
        {
            new() { Id = "Table", Entity = "Table" },
        };
        if (relation.Subject == "Seat" || relation.Target == "Seat")
            members.Add(new KsProcgenAssemblyMember
            {
                Id = "Seat", Entity = "Chair", Role = KsProcgenEntityRole.Seat,
                Movement = KsProcgenMovementClass.Clear, MinimumCount = optionalSeat ? 0 : 1,
            });
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Fixture",
            new KsProcgenAssemblyVariant
            {
                Id = "Base", AnchorMember = "Table", Members = members, Relations = [relation],
            }, new Dictionary<string, string>(), _ => true, out var assembly, out var issue),
            Is.True, issue?.Message);
        return KsProcgenAssemblyRelationEvaluator.Evaluate("Pack", "Core", 0, assembly!, proposals,
            new HashSet<Vector2i>());
    }

    private static KsProcgenEntityProposal Proposal(string id, Vector2i cell) => new(
        "Pack", id, id, id == "Seat" ? KsProcgenEntityRole.Seat : KsProcgenEntityRole.PrimaryFurniture,
        id == "Seat" ? KsProcgenMovementClass.Clear : KsProcgenMovementClass.VaultRequired,
        cell, 0, null);
}
