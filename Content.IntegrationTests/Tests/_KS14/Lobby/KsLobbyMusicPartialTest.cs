using System.Linq;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Lobby;

/// <summary>
///     The module's lobby music is a partial of upstream's <c>LobbyMusic</c>: it adds to the collection, once, rather
///         than replacing it or failing the load as a duplicate id.
/// </summary>
public sealed class KsLobbyMusicPartialTest : GameTest
{
    private static readonly ProtoId<SoundCollectionPrototype> LobbyMusic = "LobbyMusic";
    private static readonly ResPath UpstreamTrack = new("/Audio/Lobby/blindrage.ogg");
    private static readonly ResPath ModuleTrack = new("/Audio/_KsModule/LobbyMusic/melcom-cyberpunk.ogg");

    [Test]
    public void ModuleTrackIsAddedToUpstreamCollection()
    {
        // The module is private; without it there is nothing to merge.
        if (!Server.Resolve<IResourceManager>().ContentFileExists(ModuleTrack))
            Assert.Ignore("_KsModule audio is not present.");

        // Both sides: the client picks the track, and a side that missed the partial would play the upstream list.
        foreach (var protoMan in new[] { SProtoMan, CProtoMan })
        {
            var files = protoMan.Index(LobbyMusic).PickFiles;
            Assert.That(files.Count(file => file == ModuleTrack), Is.EqualTo(1), "added once");
            Assert.That(files, Does.Contain(UpstreamTrack), "upstream tracks kept");
        }
    }
}
