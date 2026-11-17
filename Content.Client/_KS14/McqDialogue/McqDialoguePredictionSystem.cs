using Content.Client._KS14.McqDialogue.UI;
using Content.Shared._KS14.McqDialogue;
using Robust.Shared.Timing;

namespace Content.Client._KS14.McqDialogue;

/// <summary>
///     Shows a dialogue's window as soon as the client predicts it, rather than a round trip later when the server's
///         dialogue entity arrives. That entity's UI then takes the window over (<see cref="TryTakeOver"/>), with any
///         answer already given.
/// </summary>
public sealed partial class McqDialoguePredictionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    /// <summary>
    ///     How long a predicted window waits for the server's dialogue before closing.
    /// </summary>
    public static readonly TimeSpan PredictionTimeout = TimeSpan.FromSeconds(3);

    [SubscribeLocalEvent]
    private void OnPredicted(ref McqDialoguePredictedEvent args)
    {
        // Already waiting on one: a second click before the server answered.
        if (HasComp<McqDialoguePredictedComponent>(args.Source))
            return;

        var predictedEntity = new Entity<McqDialoguePredictedComponent>(args.Source, AddComp<McqDialoguePredictedComponent>(args.Source));
        predictedEntity.Comp.ExpiresAt = _gameTiming.CurTime + PredictionTimeout;

        var window = new McqDialogueWindow();
        window.SetHeader(args.Settings?.Title, args.Settings?.Prompt);
        foreach (var datum in args.Options)
        {
            var optionId = datum.Id;
            window.AddOption(datum, () => Choose(predictedEntity, optionId));
        }

        window.OnClose += () => predictedEntity.Comp.Closed = true;
        predictedEntity.Comp.Window = window;
        window.OpenCentered();
    }

    /// <summary>
    ///     Picks an option in the window still waiting for the server's dialogue about <paramref name="sourceUid"/>.
    /// </summary>
    public void Choose(EntityUid sourceUid, string optionId)
    {
        if (TryComp<McqDialoguePredictedComponent>(sourceUid, out var predictedComponent))
            Choose((sourceUid, predictedComponent), optionId);
    }

    private void Choose(Entity<McqDialoguePredictedComponent> entity, string optionId)
    {
        if (entity.Comp.Closed)
            return;

        entity.Comp.ChosenId = optionId;
        entity.Comp.Window?.Close();
    }

    /// <summary>
    ///     Hands the window waiting for the server's dialogue about <paramref name="sourceUid"/> to that dialogue's UI.
    /// </summary>
    /// <param name="window">The window, still open, or null if it was closed meanwhile.</param>
    /// <param name="chosenId">The option picked meanwhile, if any.</param>
    /// <returns>False if no window was waiting.</returns>
    public bool TryTakeOver(EntityUid sourceUid, out McqDialogueWindow? window, out string? chosenId)
    {
        window = null;
        chosenId = null;
        if (!TryComp<McqDialoguePredictedComponent>(sourceUid, out var predictedComponent))
            return false;

        chosenId = predictedComponent.ChosenId;
        if (!predictedComponent.Closed)
        {
            window = predictedComponent.Window;
            predictedComponent.Window = null;
        }

        RemComp(sourceUid, predictedComponent);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<McqDialoguePredictedComponent> entity, ref ComponentShutdown args)
    {
        entity.Comp.Window?.Close();
        entity.Comp.Window?.Dispose();
        entity.Comp.Window = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _gameTiming.CurTime;
        var predictedEnumerator = AllEntityQuery<McqDialoguePredictedComponent>();
        while (predictedEnumerator.MoveNext(out var sourceUid, out var predictedComponent))
        {
            if (now >= predictedComponent.ExpiresAt && predictedComponent.LifeStage <= ComponentLifeStage.Running)
                RemCompDeferred(sourceUid, predictedComponent);
        }
    }
}
