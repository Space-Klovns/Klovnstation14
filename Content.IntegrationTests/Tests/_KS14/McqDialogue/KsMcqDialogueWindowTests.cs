using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.McqDialogue.UI;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.McqDialogue;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.McqDialogue;

/// <summary>
///     The dialogue window can't be resized small enough to cut off its title, its question or an option.
/// </summary>
[TestOf(typeof(McqDialogueWindow))]
public sealed class KsMcqDialogueWindowTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const string LongText = "ks-specific-orevent-drone-escape-failure";
    private const string ShortText = "ks-specific-orevent-excavate-yes";

    /// <summary>
    ///     Each of them in turn the widest, so each has to set the minimum width itself; the long text is wider than the
    ///         window's default minimum.
    /// </summary>
    [TestCase(LongText, ShortText, ShortText)]
    [TestCase(ShortText, LongText, ShortText)]
    [TestCase(ShortText, ShortText, LongText)]
    public async Task ShrinkingNeverCutsOffText(string titleLoc, string promptLoc, string optionLoc)
    {
        var title = new LocId(titleLoc);
        var prompt = new LocId(promptLoc);
        var option = new LocId(optionLoc);

        McqDialogueWindow window = null;
        await Client.WaitPost(() =>
        {
            window = new McqDialogueWindow();
            window.SetHeader(title, prompt);
            window.AddOption(new McqDialogueData(option, "a"), () => { });
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        // What a drag towards the top-left corner leaves behind: a SetSize far smaller than the contents.
        foreach (var dragged in new[] { new Vector2(50f, 50f), new Vector2(120f, 400f), new Vector2(600f, 40f) })
        {
            await Client.WaitPost(() => window.SetSize = dragged);

            // A resize takes a couple of layout passes to settle: the new minimum is found after the first.
            await Pair.RunTicksSync(5);

            await Client.WaitPost(() =>
            {
                var windowBox = UIBox2.FromDimensions(window.GlobalPosition, window.Size);
                var texts = new[] { Loc.GetString(title), Loc.GetString(prompt), Loc.GetString(option) }.Distinct().ToArray();
                var labels = Descendants(window).OfType<Label>().Where(label => texts.Contains(label.Text)).ToList();

                Assert.That(labels, Has.Count.EqualTo(3), "precondition: the title, question and option are all labels in the window");
                Assert.Multiple(() =>
                {
                    foreach (var label in labels)
                    {
                        AssertShown(windowBox, label, dragged);
                    }
                });
            });
        }

        await Client.WaitPost(() => window.Close());
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    /// <summary>
    ///     A squeezed label is laid out, and measured, at the squeezed size, and a clipped one measures as zero wide, so
    ///         neither its size nor its DesiredSize says it was cut off. Compare against what it needs unclipped, with
    ///         room to spare, instead.
    /// </summary>
    private static void AssertShown(UIBox2 windowBox, Label label, Vector2 dragged)
    {
        var shownSize = label.Size;
        var labelBox = UIBox2.FromDimensions(label.GlobalPosition, shownSize);

        var clipText = label.ClipText;
        label.ClipText = false;
        label.Measure(Vector2Helpers.Infinity);
        var neededSize = label.DesiredSize;
        label.ClipText = clipText;
        label.InvalidateMeasure();

        Assert.That(windowBox.Encloses(labelBox), Is.True,
            $"'{label.Text}' ({labelBox}) must stay inside the window ({windowBox}) after resizing to {dragged}");
        Assert.That(shownSize.X, Is.GreaterThanOrEqualTo(neededSize.X - 0.5f),
            $"'{label.Text}' must get the width it needs ({neededSize.X}) after resizing to {dragged}, not {shownSize.X}");
        Assert.That(shownSize.Y, Is.GreaterThanOrEqualTo(neededSize.Y - 0.5f),
            $"'{label.Text}' must get the height it needs ({neededSize.Y}) after resizing to {dragged}, not {shownSize.Y}");
    }
}
