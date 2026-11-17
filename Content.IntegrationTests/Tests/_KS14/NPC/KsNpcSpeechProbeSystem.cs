#nullable enable
using System.Collections.Generic;
using Content.Shared.Chat;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Counts what NPCs say, for tests. The test assembly's systems are registered on both sides; only the server half
///         hears anything.
/// </summary>
public sealed partial class KsNpcSpeechProbeSystem : EntitySystem
{
    public readonly List<(EntityUid Source, string Message)> Lines = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        Lines.Add((args.Source, args.Message));
    }
}
