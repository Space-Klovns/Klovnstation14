using System.Linq;
using System.Numerics;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureSystem
{
    private bool TryReadSourceDefaults(string prototypeId, KsProcgenPrototypeManifest definitions, EntityWork work,
        CancellationToken cancellationToken, out SourceDefaults defaults, out KsProcgenIssue? issue)
    {
        defaults = default; issue = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (prototypeId.Length == 0) return true;
        if (work.Defaults.TryGetValue(prototypeId, out defaults)) return true;
        if (!work.Spend()) return EntityBudget(out issue);
        var body = definitions.Bodies.FirstOrDefault(body => body.Kind == KsProcgenPrototypeBodyKind.Entity && body.PrototypeId == prototypeId);
        if (body == null)
            return Reject("UncapturedPrototypeReference", "Source defaults require a captured concrete entity body.", out issue);
        var characters = (long) work.DefaultImageCharacters + (long) body.CanonicalBody.Length + (long) body.CanonicalMetadata.Length;
        if (characters > (long) work.Limits.MaximumDefaultImageCharacters)
            return Reject("RoomEntityExtractionDefaultImageBudget", "Source default image inputs exceed their cumulative character allowance.", out issue);
        work.DefaultImageCharacters = (int) characters;
        if (!_definitions.TryCreatePrototypeSnapshot(definitions, prototypeId, out var prototype, out issue)) return false;
        work.DefaultPrototypeImages++;
        prototype!.TryComp<TransformComponent>(out var transform, Factory);
        if (transform?.ParentUid.IsValid() == true)
            return Reject("UnsupportedRoomEntityPrototypeParent", "Prototype defaults cannot reference a live world parent.", out issue);
        defaults = new(transform?.LocalPosition ?? Vector2.Zero, transform?.LocalRotation.Theta ?? 0.0,
            transform?.Anchored ?? false, transform?.NoLocalRotation ?? false, prototype.TryComp<MapComponent>(out _, Factory));
        work.Defaults[prototypeId] = defaults;
        return true;
    }
}
