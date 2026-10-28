#nullable enable
using System;
using System.Linq;
using Content.Client._KS14.Procedural.Editor;
using Content.Server.Administration.Managers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Content.Server._KS14.Procedural;
using Content.Server._KS14.Procedural.Editor;
using Content.Shared._KS14.Procedural;
using Content.Shared._KS14.Procedural.Editor;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenThemeEditorTests : KsProcgenGameTest
{
    private static KsProcgenThemeEditorValues Values() => new()
    {
        Name = "Engineering cabin", TilePack = "KsProcgenOfficeTiles", WallPack = "KsProcgenNoDoorWallsFixture",
        FurnishingDensity = 0, LightingCoverage = 0,
    };

    [Test]
    public async Task SavedThemeReloadsIntoNormalGenerationWithoutSourceFilesOrPreviewTools()
    {
        await AssertOnServer(() =>
        {
            var library = Pair.Server.System<KsProcgenThemeLibrarySystem>();
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var id = Guid.NewGuid(); var values = Values(); string? key = null;
            try
            {
                Assert.That(library.TrySave(id, 1, values, out key, out var issue), Is.True, issue?.ToString());
                values.TilePack = "KsProcgenMissingPack";
                Assert.That(library.TryLoad(key!, out var reloaded, out var draft, out issue), Is.True, issue?.ToString());
                Assert.That(reloaded!.TilePack, Is.EqualTo("KsProcgenOfficeTiles"));
                Assert.That(draft!.Revision, Is.EqualTo(1));
                Assert.That(library.TryCaptureCatalog(key!, out var catalog, out issue), Is.True, issue?.ToString());
                var generated = stages.TryGenerate(new() { RequestId = "SavedTheme", Seed = 17,
                    Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } },
                    ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland }, "Theme", content: catalog,
                    planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.ToString());
                Assert.That(stages.Verify(generated.Stage!), Is.True);
                reloaded.Name = "changed copy";
                Assert.That(library.TryLoad(key!, out var again, out _, out issue), Is.True, issue?.ToString());
                Assert.That(again!.Name, Is.EqualTo("Engineering cabin"));
            }
            finally { stages.DiscardAll(); Delete(key); }
        });
    }

    [Test]
    public async Task SavedRevisionsCannotOverwriteEarlierThemesAndBadPacksDoNotPersist()
    {
        await AssertOnServer(() =>
        {
            var library = Pair.Server.System<KsProcgenThemeLibrarySystem>();
            var id = Guid.NewGuid(); string? first = null; string? second = null;
            try
            {
                var values = Values();
                Assert.That(library.TrySave(id, 1, values, out first, out var issue), Is.True, issue?.ToString());
                values.Name = "second";
                Assert.That(library.TrySave(id, 1, values, out var duplicate, out issue), Is.False);
                Assert.That(duplicate, Is.Null);
                Assert.That(library.TrySave(id, 2, values, out second, out issue), Is.True, issue?.ToString());
                Assert.That(library.TryLoad(first!, out var original, out _, out issue), Is.True, issue?.ToString());
                Assert.That(original!.Name, Is.EqualTo("Engineering cabin"));
                values.EntityPack = "KsProcgenMissingEntityPack";
                Assert.That(library.TrySave(id, 3, values, out var rejected, out issue), Is.False);
                Assert.That(rejected, Is.Null);
            }
            finally { Delete(first); Delete(second); }
        });
    }

    [TestCase("../other")]
    [TestCase("not-a-key")]
    [TestCase("00000000000000000000000000000000-1")]
    public async Task ThemeLoadKeysCannotSelectArbitraryFiles(string key)
    {
        await AssertOnServer(() =>
        {
            Assert.That(Pair.Server.System<KsProcgenThemeLibrarySystem>().TryLoad(key,
                out var values, out var draft, out var issue), Is.False);
            Assert.That(values, Is.Null); Assert.That(draft, Is.Null);
            Assert.That(issue!.Code, Is.EqualTo("InvalidThemeEditorKey"));
        });
    }

    [Test]
    public async Task NativeWindowSavesThroughConnectedEuiAndShowsPersistentKey()
    {
        Content.Server._KS14.Procedural.Editor.KsProcgenThemeEditorEui editor = null!;
        KsProcgenThemeEditorWindow window = null!;
        string? savedKey = null;
        string? copiedKey = null;
        await AssertOnServer(() =>
        {
            Server.ResolveDependency<IAdminManager>().PromoteHost(ServerSession!);
            editor = new();
            Server.ResolveDependency<Content.Server.EUI.EuiManager>().OpenEui(editor, ServerSession!);
        });
        try
        {
            await Pair.RunTicksSync(10);
            await Client.WaitPost(() =>
            {
                var root = Client.ResolveDependency<IUserInterfaceManager>().RootControl;
                window = Descendants(root).OfType<KsProcgenThemeEditorWindow>().Single();
                window.FindControl<LineEdit>("ThemeName").Text = "Native editor cabin";
                Select(window, "Tiles", "KsProcgenOfficeTiles");
                Select(window, "Walls", "KsProcgenOfficeWalls");
                Select(window, "Lights", "KsProcgenOfficeLights");
                Select(window, "Entities", "KsProcgenOfficeWorkstations");
                Select(window, "Atmosphere", "KsProcgenNitrogenRoom");
                Select(window, "Electricity", "KsProcgenClosetElectrical");
            });
            await ClickSave(window);
            await Pair.RunTicksSync(10);
            await AssertOnServer(() =>
            {
                var state = (KsProcgenThemeEditorState) editor.GetNewState();
                Assert.That(state.Status, Is.EqualTo("ks-procgen-theme-saved"), state.Detail);
                Assert.That(state.Revision, Is.EqualTo(1));
                savedKey = state.Detail;
                Assert.That(Server.System<KsProcgenThemeLibrarySystem>().TryLoad(savedKey,
                    out var values, out _, out var issue), Is.True, issue?.ToString());
                Assert.That(values!.Name, Is.EqualTo("Native editor cabin"));
                Assert.That(values.AtmosphereProfile, Is.EqualTo("KsProcgenNitrogenRoom"));
                Assert.That(values.ElectricalProfile, Is.EqualTo("KsProcgenClosetElectrical"));
                Assert.That(values.LightPack, Is.EqualTo("KsProcgenOfficeLights"));
                Assert.That(values.EntityPack, Is.EqualTo("KsProcgenOfficeWorkstations"));
            });
            string shownKey = ""; bool enabled = false;
            await Client.WaitPost(() =>
            {
                shownKey = window.FindControl<LineEdit>("SavedKey").Text;
                enabled = !window.FindControl<Button>("Save").Disabled;
            });
            await AssertOnServer(() =>
            {
                Assert.That(shownKey, Is.EqualTo(savedKey));
                Assert.That(enabled, Is.True);
                editor.Close();
            });
            await Pair.RunTicksSync(5);
            await AssertOnServer(() =>
            {
                editor = new(loadKey: savedKey);
                Server.ResolveDependency<Content.Server.EUI.EuiManager>().OpenEui(editor, ServerSession!);
                var state = (KsProcgenThemeEditorState) editor.GetNewState();
                Assert.That(state.Revision, Is.Zero);
                Assert.That(state.Status, Is.EqualTo("ks-procgen-theme-loaded-copy"));
                Assert.That(state.DocumentId.ToString("N"), Is.Not.EqualTo(savedKey!.Split('-')[0]));
            });
            await Pair.RunTicksSync(10);
            string copiedAtmosphere = ""; string copiedElectricity = "";
            await Client.WaitPost(() =>
            {
                window = Descendants(Client.ResolveDependency<IUserInterfaceManager>().RootControl)
                    .OfType<KsProcgenThemeEditorWindow>().Single();
                var gas = window.FindControl<OptionButton>("Atmosphere");
                var power = window.FindControl<OptionButton>("Electricity");
                copiedAtmosphere = (string) gas.GetItemMetadata(gas.GetIdx(gas.SelectedId))!;
                copiedElectricity = (string) power.GetItemMetadata(power.GetIdx(power.SelectedId))!;
                window.FindControl<LineEdit>("ThemeName").Text = "Copied engineering cabin";
            });
            await AssertOnServer(() =>
            {
                Assert.That(copiedAtmosphere, Is.EqualTo("KsProcgenNitrogenRoom"));
                Assert.That(copiedElectricity, Is.EqualTo("KsProcgenClosetElectrical"));
            });
            await ClickSave(window);
            await Pair.RunTicksSync(10);
            await AssertOnServer(() =>
            {
                var state = (KsProcgenThemeEditorState) editor.GetNewState();
                Assert.That(state.Status, Is.EqualTo("ks-procgen-theme-saved"), state.Detail);
                copiedKey = state.Detail;
                Assert.That(copiedKey, Is.Not.EqualTo(savedKey));
                var library = Server.System<KsProcgenThemeLibrarySystem>();
                Assert.That(library.TryLoad(savedKey!, out var original, out _, out var issue), Is.True, issue?.Code);
                Assert.That(original!.Name, Is.EqualTo("Native editor cabin"));
                Assert.That(library.TryLoad(copiedKey, out var copy, out _, out issue), Is.True, issue?.Code);
                Assert.That(copy!.Name, Is.EqualTo("Copied engineering cabin"));
                Assert.That(copy.AtmosphereProfile, Is.EqualTo(original.AtmosphereProfile));
                Assert.That(copy.ElectricalProfile, Is.EqualTo(original.ElectricalProfile));
            });
        }
        finally
        {
            await Server.WaitPost(() => { if (!editor.IsShutDown) editor.Close(); Delete(savedKey); Delete(copiedKey); });
            await Pair.RunTicksSync(5);
        }
    }

    private async Task ClickSave(KsProcgenThemeEditorWindow window)
    {
        Button button = null!;
        GUIBoundKeyEventArgs pressed = null!;
        GUIBoundKeyEventArgs released = null!;
        await Client.WaitPost(() =>
        {
            button = window.FindControl<Button>("Save");
            var coordinates = new ScreenCoordinates(button.GlobalPixelPosition + button.PixelSize / 2,
                button.Window?.Id ?? default);
            var relativePosition = coordinates.Position / button.UIScale - button.GlobalPosition;
            var relativePixelPosition = coordinates.Position - button.GlobalPixelPosition;
            pressed = new(EngineKeyFunctions.UIClick, BoundKeyState.Down, coordinates,
                default, relativePosition, relativePixelPosition);
            released = new(EngineKeyFunctions.UIClick, BoundKeyState.Up, coordinates,
                default, relativePosition, relativePixelPosition);
        });
        await Client.DoGuiEvent(button, pressed);
        await Client.DoGuiEvent(button, released);
    }

    private static void Select(KsProcgenThemeEditorWindow window, string controlName, string prototypeId)
    {
        var button = window.FindControl<OptionButton>(controlName);
        var index = Enumerable.Range(0, button.ItemCount).Single(index =>
            button.GetItemMetadata(index) as string == prototypeId);
        button.SelectId(button.GetItemId(index));
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void Delete(string? key)
    {
        if (key != null) Pair.Server.ResolveDependency<IResourceManager>().UserData.Delete(new ResPath("/Procgen/RoomThemes/" + key + ".json"));
    }
}
