using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureSystem
{
    private static bool TryCaptureOverrideImages(SourceEntity entity, EntityWork work, CancellationToken cancellationToken,
        [NotNullWhen(true)] out IReadOnlyList<KsProcgenMapComponentOverride>? images, out KsProcgenIssue? issue)
    {
        images = null; issue = null;
        var result = new List<KsProcgenMapComponentOverride>();
        foreach (var component in entity.OverrideData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingNodes = work.Limits.MaximumOverrideNodes - work.OverrideNodes;
            var remainingCharacters = work.Limits.MaximumOverrideCharacters - work.OverrideCharacters;
            if (remainingNodes <= 0 || remainingCharacters <= 0)
                return Reject("RoomEntityOverrideImageBudget", "Selected component images exhausted their cumulative allowance.", out issue);
            var limits = new KsProcgenPrototypeManifestLimits
            {
                MaximumNodes = remainingNodes,
                MaximumCharacters = remainingCharacters,
                MaximumDepth = work.Limits.MaximumOverrideDepth,
            };
            if (!KsProcgenPrototypeManifestSystem.TryCanonicalBody(component, limits, out var body, out var code, out var nodes, out var characters))
                return Reject(code == "PrototypeManifestDepthBudget" ? "RoomEntityOverrideDepthBudget" : "RoomEntityOverrideImageBudget",
                    "Selected component images exceed their structural allowance.", out issue);
            work.OverrideNodes += nodes;
            work.OverrideCharacters += characters;
            result.Add(new(component.Get<ValueDataNode>("type").Value, body!));
        }
        images = result.AsReadOnly(); return true;
    }
}
