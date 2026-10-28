#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Content.Server._KS14.Procedural.Editor;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenThemeEditorTests
{
    [Test]
    public async Task SavedThemesWithoutNewUtilityFieldsStillLoadWithNoUtilitySelection()
    {
        await AssertOnServer(() =>
        {
            var documentId = Guid.NewGuid();
            var key = documentId.ToString("N") + "-1";
            var oldFile = JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = 1, DocumentId = documentId, Revision = 1,
                Values = new { Name = "Old cabin", TilePack = "KsProcgenOfficeTiles",
                    WallPack = "KsProcgenNoDoorWallsFixture", FurnishingDensity = 0f, LightingCoverage = 0f },
            });
            try
            {
                WriteThemeFile(key, oldFile);
                var library = Server.System<KsProcgenThemeLibrarySystem>();
                Assert.That(library.TryLoad(key, out var values, out _, out var issue), Is.True, issue?.Code);
                Assert.That(values!.AtmosphereProfile, Is.Empty);
                Assert.That(values.ElectricalProfile, Is.Empty);
                Assert.That(library.TryCaptureCatalog(key, out var catalog, out issue), Is.True, issue?.Code);
                Assert.That(catalog!.Theme.Atmosphere, Is.Null);
                Assert.That(catalog.Theme.Electrical, Is.Null);
            }
            finally { Delete(key); }
        });
    }

    [TestCase("oversized", "ThemeEditorFileBudget")]
    [TestCase("malformed", "ThemeEditorLoadFailed")]
    [TestCase("wrong-identity", "InvalidThemeEditorFile")]
    [TestCase("unknown-schema", "InvalidThemeEditorFile")]
    public async Task InvalidStoredFilesCannotBecomeGeneratorCatalogs(string defect, string expectedCode)
    {
        await AssertOnServer(() =>
        {
            var documentId = Guid.NewGuid();
            var key = documentId.ToString("N") + "-1";
            byte[] bytes;
            if (defect == "oversized") bytes = new byte[65_537];
            else if (defect == "malformed") bytes = Encoding.UTF8.GetBytes("{invalid");
            else bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = defect == "unknown-schema" ? 2 : 1,
                DocumentId = defect == "wrong-identity" ? Guid.NewGuid() : documentId,
                Revision = 1,
            });
            try
            {
                WriteThemeFile(key, bytes);
                var library = Server.System<KsProcgenThemeLibrarySystem>();
                Assert.That(library.TryCaptureCatalog(key, out var catalog, out var issue), Is.False);
                Assert.That(catalog, Is.Null);
                Assert.That(issue!.Code, Is.EqualTo(expectedCode));
            }
            finally { Delete(key); }
        });
    }

    [Test]
    public async Task InvalidStoredProfileDoesNotReturnAPartialDraft()
    {
        await AssertOnServer(() =>
        {
            var documentId = Guid.NewGuid();
            var key = documentId.ToString("N") + "-1";
            var values = Values();
            values.AtmosphereProfile = "MissingUtilityProfile";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = 1, DocumentId = documentId, Revision = 1, Values = values,
            }, new JsonSerializerOptions { IncludeFields = true });
            try
            {
                WriteThemeFile(key, bytes);
                Assert.That(Server.System<KsProcgenThemeLibrarySystem>().TryLoad(key,
                    out var loaded, out var draft, out var issue), Is.False);
                Assert.That(loaded, Is.Null);
                Assert.That(draft, Is.Null);
                Assert.That(issue, Is.Not.Null);
            }
            finally { Delete(key); }
        });
    }

    private void WriteThemeFile(string key, byte[] bytes)
    {
        var userData = Server.ResolveDependency<IResourceManager>().UserData;
        userData.CreateDir(new ResPath("/Procgen/RoomThemes"));
        using var output = userData.Open(new ResPath("/Procgen/RoomThemes/" + key + ".json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
    }
}
