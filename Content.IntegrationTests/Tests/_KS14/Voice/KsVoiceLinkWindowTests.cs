using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Voice.UI;
using Content.IntegrationTests.Fixtures;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Voice;

/// <summary>
///     The voice link window can't be resized small enough to hide any of its contents.
/// </summary>
[TestOf(typeof(KsVoiceLinkWindow))]
public sealed class KsVoiceLinkWindowTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     Text that wraps: it may get narrower, as long as it gets the height its wrapped lines need.
    /// </summary>
    private static readonly string[] WrappingNames = ["Explanation", "Warning"];

    /// <summary>
    ///     Everything else can't shrink at all without being cut off.
    /// </summary>
    private static readonly string[] FixedNames = ["KeybindLabel", "StatusLabel", "OpenButton", "CopyButton", "ResetButton"];

    [Test]
    public async Task ShrinkingNeverHidesContents()
    {
        KsVoiceLinkWindow window = null;
        await Client.WaitPost(() =>
        {
            window = new KsVoiceLinkWindow();
            window.SetStatus("No microphone page connected.");
            window.SetKeybind("Push-to-talk key: N");
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        // What a drag towards the top-left corner leaves behind: a SetSize far smaller than the contents.
        foreach (var dragged in new[] { new Vector2(120f, 80f), new Vector2(200f, 400f), new Vector2(600f, 60f) })
        {
            await Client.WaitPost(() => window.SetSize = dragged);

            // A resize takes a couple of layout passes to settle: the new minimum is found after the first.
            await Pair.RunTicksSync(5);

            await Client.WaitPost(() =>
            {
                var windowBox = UIBox2.FromDimensions(window.GlobalPosition, window.Size);
                Assert.Multiple(() =>
                {
                    foreach (var name in WrappingNames)
                        AssertShown(window, windowBox, name, dragged, needFullWidth: false);

                    foreach (var name in FixedNames)
                        AssertShown(window, windowBox, name, dragged, needFullWidth: true);
                });
            });
        }

        await Client.WaitPost(() => window.Close());
    }

    /// <summary>
    ///     A squeezed control is laid out, and measured, at the squeezed size, so neither its size nor its DesiredSize
    ///         says it was cut off. Compare against what it needs with room to spare instead.
    /// </summary>
    private static void AssertShown(KsVoiceLinkWindow window, UIBox2 windowBox, string name, Vector2 dragged, bool needFullWidth)
    {
        var control = window.FindControl<Control>(name);
        var shownSize = control.Size;
        var controlBox = UIBox2.FromDimensions(control.GlobalPosition, shownSize);

        control.Measure(new Vector2(needFullWidth ? float.PositiveInfinity : shownSize.X, float.PositiveInfinity));
        var neededSize = control.DesiredSize;

        Assert.That(windowBox.Encloses(controlBox), Is.True,
            $"{name} ({controlBox}) must stay inside the window ({windowBox}) after resizing to {dragged}");
        Assert.That(shownSize.Y, Is.GreaterThanOrEqualTo(neededSize.Y - 0.5f),
            $"{name} must get the height it needs ({neededSize.Y}) after resizing to {dragged}, not {shownSize.Y}");

        if (needFullWidth)
        {
            Assert.That(shownSize.X, Is.GreaterThanOrEqualTo(neededSize.X - 0.5f),
                $"{name} must get the width it needs ({neededSize.X}) after resizing to {dragged}, not {shownSize.X}");
        }
    }
}
