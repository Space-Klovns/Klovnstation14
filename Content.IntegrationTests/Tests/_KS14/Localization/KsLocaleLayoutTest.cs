using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Localization;

/// <summary>
///     The fork's locale files are laid out one way (see the localization skill): messages under
///         <c>_KS14/&lt;feature&gt;/&lt;topic&gt;.ftl</c> in lowercase kebab-case, at the same path in English and
///         Russian, and prototype names under <c>_KS14/Localization/Prototypes/</c> and nowhere else.
/// </summary>
/// <remarks>
///     Nothing else notices a file that breaks this: every .ftl under a culture is loaded wherever it is. A folder that
///         differs from another only by case is one folder on Windows and two everywhere else.
/// </remarks>
[TestFixture]
public sealed class KsLocaleLayoutTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string English = "en-US";
    private const string Russian = "ru-RU";

    /// <summary>
    ///     The one folder under <c>_KS14</c> that is not a feature's: its own layout follows the prototype tree.
    /// </summary>
    private const string LocalizationFolder = "Localization";

    private static readonly Regex KebabCase = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    ///     Each culture's message files under <c>_KS14</c>, as their path below it, with what they hold: everything but
    ///         the <see cref="LocalizationFolder"/> tree.
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> ReadMessageFiles()
    {
        var resources = Server.ResolveDependency<IResourceManager>();
        var files = new Dictionary<string, Dictionary<string, string>>();

        foreach (var path in resources.ContentFindFiles(new ResPath("/Locale/")))
        {
            // "", "Locale", the culture, "_KS14", then the path below it.
            var segments = path.ToString().Split('/');
            if (segments.Length < 5 ||
                segments[3] != "_KS14" ||
                segments[4] == LocalizationFolder ||
                !segments[^1].EndsWith(".ftl"))
                continue;

            if (!files.TryGetValue(segments[2], out var cultureFiles))
                files[segments[2]] = cultureFiles = new Dictionary<string, string>();

            cultureFiles[string.Join('/', segments.Skip(4))] = resources.ContentFileReadAllText(path);
        }

        return files;
    }

    [Test]
    public async Task MessageFoldersAndFilesAreLowercaseKebabCase()
    {
        await Server.WaitAssertion(() =>
        {
            var files = ReadMessageFiles();
            Assert.That(files, Does.ContainKey(English), "precondition: there are fork locale files to check");

            using (Assert.EnterMultipleScope())
            {
                foreach (var (culture, cultureFiles) in files)
                {
                    foreach (var relativePath in cultureFiles.Keys)
                    {
                        var names = relativePath[..^".ftl".Length].Split('/');
                        Assert.That(names, Has.All.Matches(KebabCase),
                            $"Locale/{culture}/_KS14/{relativePath}: folders and files are lowercase kebab-case, as in field-commander/admin-window.ftl");
                    }
                }
            }
        });
    }

    [Test]
    public async Task EveryMessageFileIsAtTheSamePathInEnglishAndRussian()
    {
        await Server.WaitAssertion(() =>
        {
            var files = ReadMessageFiles();
            var english = files[English].Keys.ToHashSet();
            var russian = files[Russian].Keys.ToHashSet();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(english.Except(russian), Is.Empty, $"in {English} with no file at the same path in {Russian}");
                Assert.That(russian.Except(english), Is.Empty, $"in {Russian} with no file at the same path in {English}");
            }
        });
    }

    [Test]
    public async Task PrototypeNamesAreOnlyUnderLocalizationPrototypes()
    {
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (culture, cultureFiles) in ReadMessageFiles())
                {
                    foreach (var (relativePath, text) in cultureFiles)
                    {
                        var hasPrototypeName = text.Split('\n').Any(line => line.StartsWith("ent-"));
                        Assert.That(hasPrototypeName, Is.False,
                            $"Locale/{culture}/_KS14/{relativePath} has an ent- entry: those go under _KS14/{LocalizationFolder}/Prototypes/, at the prototype's own path");
                    }
                }
            }
        });
    }
}
