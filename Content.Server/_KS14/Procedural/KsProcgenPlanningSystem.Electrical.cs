using System.Linq;
using System.Numerics;
using Content.Server.Power.Components;
using Content.Shared._KS14.Procedural;
using Content.Shared.Wall;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPlanningSystem
{
    public bool TryCaptureElectricalContext(KsProcgenThemeCatalog catalog, KsProcgenElectricalProfile profile,
        out KsProcgenElectricalPlanningContext? context, out KsProcgenIssue? issue, KsProcgenThemeCatalog? utilityContent = null)
    {
        context = null;
        if (!TryResolveElectricalUtilityContent(profile, utilityContent, out utilityContent, out issue)) return false;
        if (!KsProcgenElectricalCapabilityInspector.TryInspect(ProtoMan, Factory, profile, out var capabilities, out issue)) return false;
        var extras = ElectricalPrototypeIds(profile).ToHashSet(StringComparer.Ordinal);
        var catalogs = utilityContent == null ? new[] { catalog } : new[] { catalog, utilityContent };
        if (!_prototypeManifestSystem.TryCapture(catalogs.SelectMany(item => item.CandidateEntityIds()).Concat(extras),
                catalogs.SelectMany(item => item.CandidateTileIds()), out var manifest, out issue)) return false;
        var converterIds = profile.Substations.Concat(profile.Apcs).Where(item => item.Weight > 0f).Select(item => item.Prototype).ToHashSet(StringComparer.Ordinal);
        var facts = new List<KsProcgenElectricalPrototypeFact>();
        foreach (var root in manifest!.Roots.Where(item => item.Kind == KsProcgenPrototypeBodyKind.Entity))
        {
            if (!_prototypeManifestSystem.TryCreatePrototypeSnapshot(manifest, root.PrototypeId, out var prototype, out issue)) return false;
            prototype!.TryComp<TransformComponent>(out var transform, Factory);
            prototype.TryComp<ApcPowerReceiverComponent>(out var powered, Factory);
            prototype.TryComp<ExtensionCableReceiverComponent>(out var receiver, Factory);
            var footprint = new HashSet<Vector2i> { Vector2i.Zero };
            if (converterIds.Contains(root.PrototypeId) && prototype.TryComp<FixturesComponent>(out var fixtures, Factory))
            {
                if (fixtures.Fixtures.Count > 64) { issue = new("ElectricalConverterFixtureBudget", "Converter fixtures exceed supported bounds."); return false; }
                foreach (var fixture in fixtures.Fixtures.Values.Where(item => item.Hard))
                {
                    if (fixture.Shape.ChildCount != 1) { issue = new("ElectricalConverterShapeUnsupported", "Converter footprints need single-child bounded native shapes."); return false; }
                    var bounds = fixture.Shape.ComputeAABB(new Transform(Vector2.Zero, Angle.Zero), 0);
                    if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Bottom) ||
                        Math.Abs(bounds.Left) > 8f || Math.Abs(bounds.Right) > 8f || Math.Abs(bounds.Top) > 8f || Math.Abs(bounds.Bottom) > 8f)
                    { issue = new("ElectricalConverterShapeUnsupported", "Converter bounds must be finite and local."); return false; }
                    var left = (int) Math.Floor((double) bounds.Left + 0.5); var right = (int) Math.Ceiling((double) bounds.Right + 0.5) - 1;
                    var bottom = (int) Math.Floor((double) bounds.Bottom + 0.5); var top = (int) Math.Ceiling((double) bounds.Top + 0.5) - 1;
                    for (var x = left; x <= right; x++)
                    for (var y = bottom; y <= top; y++) footprint.Add(new(x, y));
                }
            }
            facts.Add(new(root.PrototypeId, transform?.Anchored ?? false, transform?.NoLocalRotation ?? false,
                powered != null, receiver != null, receiver?.ReceptionRange ?? 0, powered?.Load,
                footprint.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray(), prototype.TryComp<WallMountComponent>(out _, Factory)));
        }
        var range = capabilities!.Devices.Single(item => item.Role == KsProcgenElectricalDeviceRole.LowVoltageCable).TransferRange;
        return KsProcgenElectricalPlanningContext.TryCreate(profile, facts, manifest.ContentHash, range ?? -1, out context, out issue,
            utilityContent: utilityContent);
    }

    public bool TryResolveElectricalUtilityContent(KsProcgenElectricalProfile? profile, KsProcgenThemeCatalog? supplied,
        out KsProcgenThemeCatalog? utilityContent, out KsProcgenIssue? issue)
    {
        utilityContent = supplied; issue = null;
        if (profile?.Distribution is not { } distribution)
        {
            if (supplied == null) return true;
            issue = new("UnexpectedElectricalUtilityCatalog", "A closet catalog needs a distribution profile."); return false;
        }
        if (utilityContent == null && !KsProcgenThemeCatalog.TryCapture(ProtoMan, distribution.ClosetTheme, out utilityContent, out issue)) return false;
        if (utilityContent!.Theme.Id != distribution.ClosetTheme || utilityContent.Theme.Electrical != null)
        { issue = new("ElectricalUtilityRoomContextRequired", "The closet catalog must match the profile and cannot add another electrical override."); return false; }
        return true;
    }

    internal static IEnumerable<string> ElectricalPrototypeIds(KsProcgenElectricalProfile profile) =>
        new[] { profile.HighVoltageCable, profile.MediumVoltageCable, profile.LowVoltageCable }
            .Concat(profile.Substations.Concat(profile.Apcs).Where(item => item.Weight > 0f).Select(item => item.Prototype));
}
