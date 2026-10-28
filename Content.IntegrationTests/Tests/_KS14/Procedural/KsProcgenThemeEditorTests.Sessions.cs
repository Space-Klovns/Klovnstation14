#nullable enable
using System.Linq;
using Content.Client._KS14.Procedural.Editor;
using Content.Server.Administration.Managers;
using Content.Server._KS14.Procedural.Editor;
using Content.Shared._KS14.Procedural.Editor;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenThemeEditorTests
{
    [Test]
    public async Task StaleSaveCannotOverwriteCurrentRevisionAndAValidRetryCanAdvanceIt()
    {
        Content.Server._KS14.Procedural.Editor.KsProcgenThemeEditorEui editor = null!;
        string? firstKey = null;
        string? secondKey = null;
        await AssertOnServer(() =>
        {
            Server.ResolveDependency<IAdminManager>().PromoteHost(ServerSession!);
            editor = new();
            Server.ResolveDependency<Content.Server.EUI.EuiManager>().OpenEui(editor, ServerSession!);
        });
        try
        {
            await Pair.RunTicksSync(5);
            await AssertOnServer(() =>
            {
                var values = Values();
                editor.HandleMessage(new KsProcgenThemeEditorSave { ExpectedRevision = 0, Values = values });
                var first = (KsProcgenThemeEditorState) editor.GetNewState();
                Assert.That(first.Status, Is.EqualTo("ks-procgen-theme-saved"), first.Detail);
                firstKey = first.Detail;
                values.Name = "Latest cabin";
                editor.HandleMessage(new KsProcgenThemeEditorSave { ExpectedRevision = 0, Values = values });
                var conflict = (KsProcgenThemeEditorState) editor.GetNewState();
                Assert.That(conflict.Status, Is.EqualTo("ks-procgen-theme-conflict"));
                Assert.That(conflict.Revision, Is.EqualTo(1));
                Assert.That(conflict.Values.Name, Is.EqualTo("Engineering cabin"));
                editor.HandleMessage(new KsProcgenThemeEditorSave { ExpectedRevision = 1, Values = values });
                var retried = (KsProcgenThemeEditorState) editor.GetNewState();
                Assert.That(retried.Status, Is.EqualTo("ks-procgen-theme-saved"), retried.Detail);
                Assert.That(retried.Revision, Is.EqualTo(2));
                secondKey = retried.Detail;
                var library = Server.System<KsProcgenThemeLibrarySystem>();
                Assert.That(library.TryLoad(firstKey, out var original, out _, out var issue), Is.True, issue?.Code);
                Assert.That(original!.Name, Is.EqualTo("Engineering cabin"));
                Assert.That(library.TryLoad(secondKey, out var latest, out _, out issue), Is.True, issue?.Code);
                Assert.That(latest!.Name, Is.EqualTo("Latest cabin"));
            });
        }
        finally
        {
            await Server.WaitPost(() => { if (!editor.IsShutDown) editor.Close(); Delete(firstKey); Delete(secondKey); });
            await Pair.RunTicksSync(5);
        }
    }

    [Test]
    public async Task LosingMappingPermissionsClosesTheConnectedNativeEditor()
    {
        Content.Server._KS14.Procedural.Editor.KsProcgenThemeEditorEui editor = null!;
        await AssertOnServer(() =>
        {
            Server.ResolveDependency<IAdminManager>().PromoteHost(ServerSession!);
            editor = new();
            Server.ResolveDependency<Content.Server.EUI.EuiManager>().OpenEui(editor, ServerSession!);
        });
        try
        {
            await Pair.RunTicksSync(10);
            await AssertOnServer(() =>
            {
                Server.ResolveDependency<IAdminManager>().DeAdmin(ServerSession!);
                Assert.That(editor.IsShutDown, Is.True);
            });
            await Pair.RunTicksSync(10);
            var remainingWindows = -1;
            await Client.WaitPost(() => remainingWindows = Descendants(Client.ResolveDependency<IUserInterfaceManager>().RootControl)
                .OfType<KsProcgenThemeEditorWindow>().Count());
            await AssertOnServer(() => Assert.That(remainingWindows, Is.Zero));
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                if (!editor.IsShutDown) editor.Close();
                Server.ResolveDependency<IAdminManager>().ReAdmin(ServerSession!);
            });
            await Pair.RunTicksSync(5);
        }
    }
}
