using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     tgui's NumberInput: dragging, clicking into the text box, and how Enter, Escape and losing focus end it. The drag and
///         click are driven through the same methods the mouse handlers call.
/// </summary>
[TestOf(typeof(KsTguiNumberInput))]
public sealed class KsTguiNumberInputTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static KsTguiNumberInput Make(List<float> commits, float value = 50f)
    {
        var input = new KsTguiNumberInput { MinValue = 0f, MaxValue = 100f, Step = 10f, StepPixelSize = 4f, Unit = "kPa" };
        input.SetValue(value);
        input.OnCommitted += commits.Add;
        return input;
    }

    [Test]
    public async Task TypedValueIsClamped()
    {
        var commits = new List<float>();
        await Client.WaitPost(() =>
        {
            var input = Make(commits);

            input.Edit.Text = "500";
            input.Commit();
            input.Edit.Text = "-20";
            input.Commit();
            input.Edit.Text = "not a number";
            input.Commit();

            Assert.That(commits, Is.EqualTo(new[] { 100f, 0f }));
            Assert.That(input.Value, Is.EqualTo(0f), "Text that is not a number changes nothing.");
        });
    }

    [Test]
    public async Task DragMovesByStepPerStepPixelSize()
    {
        var commits = new List<float>();
        await Client.WaitPost(() =>
        {
            var input = Make(commits);

            // Past the 4px start threshold the control drags, and nothing has moved yet.
            input.BeginDrag(200f);
            input.DragMove(190f);
            Assert.That(input.IsDragging);
            Assert.That(input.Value, Is.EqualTo(50f));

            // 12px up at 4px per step of 10 is 30, taken live; nothing is committed until release.
            input.DragMove(188f);
            Assert.That(input.Value, Is.EqualTo(80f));
            Assert.That(commits, Is.Empty);

            // 4px further is one more step, and less than a step's worth of pixels moves nothing.
            input.DragMove(184f);
            Assert.That(input.Value, Is.EqualTo(90f));
            input.DragMove(185f);
            Assert.That(input.Value, Is.EqualTo(90f));

            // The range holds at the top.
            input.DragMove(-200f);
            Assert.That(input.Value, Is.EqualTo(100f));

            input.EndDrag();
            Assert.That(commits, Is.EqualTo(new[] { 100f }));
            Assert.That(input.IsEditing, Is.False, "A drag does not start editing.");
        });
    }

    [Test]
    public async Task ClickWithoutDragSelectsEverything()
    {
        var commits = new List<float>();
        await Client.WaitPost(() =>
        {
            var input = Make(commits);

            input.BeginDrag(200f);
            input.DragMove(198f);
            input.EndDrag();

            Assert.That(input.IsEditing);
            Assert.That(input.Edit.Visible);
            Assert.That(input.Edit.Text, Is.EqualTo("50"));
            Assert.That(input.Edit.SelectionLength, Is.EqualTo(input.Edit.Text.Length));
            Assert.That(commits, Is.Empty);
        });
    }

    [Test]
    public async Task EscapeRevertsAndEnterCommits()
    {
        var commits = new List<float>();
        await Client.WaitPost(() =>
        {
            var input = Make(commits);

            input.BeginDrag(200f);
            input.EndDrag();
            input.Edit.Text = "80";
            input.Cancel();
            Assert.That(input.IsEditing, Is.False);
            Assert.That(input.Value, Is.EqualTo(50f));
            Assert.That(input.Edit.Text, Is.EqualTo("50"));
            Assert.That(commits, Is.Empty);

            input.BeginDrag(200f);
            input.EndDrag();
            input.Edit.Text = "80";
            input.Edit.ForceSubmitText();
            Assert.That(input.IsEditing, Is.False);
            Assert.That(input.Value, Is.EqualTo(80f));
            Assert.That(commits, Is.EqualTo(new[] { 80f }));
        });
    }
}
