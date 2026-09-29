// KS14: added in this fork
using System.Linq;
using Content.Client._KS14.TTS;
using Content.Shared._KS14.TTS;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

// The character's TTS voice: a picker of the selectable voices, and a button playing the server-baked preview.
public sealed partial class HumanoidProfileEditor
{
    /// <summary>
    ///     The picker's item id for "random". Voices are numbered from 1, in the order of <see cref="_ttsVoiceIds"/>.
    /// </summary>
    private const int TtsVoiceRandomId = 0;

    /// <summary>
    ///     The voices listed, in picker order, to find a profile's voice among them.
    /// </summary>
    private readonly List<ProtoId<TtsVoicePrototype>> _ttsVoiceIds = new();

    private KsTtsPreviewManager _ttsPreviewManager = default!;

    private void InitializeTtsVoice()
    {
        _ttsPreviewManager = IoCManager.Resolve<KsTtsPreviewManager>();

        TtsVoiceButton.OnItemSelected += args =>
        {
            TtsVoiceButton.SelectId(args.Id);

            // Each voice's item carries its id; random's carries nothing.
            SetTtsVoice(TtsVoiceButton.GetItemMetadata(TtsVoiceButton.GetIdx(args.Id)) as ProtoId<TtsVoicePrototype>?);
        };

        TtsVoicePreviewButton.OnPressed += _ =>
        {
            if (Profile?.TtsVoice is { } voice)
                _ttsPreviewManager.TryPlay(voice);
        };

        RefreshTtsVoices();
    }

    /// <summary>
    ///     Rebuilds the picker from the voice prototypes. Unselectable voices are left out entirely.
    /// </summary>
    public void RefreshTtsVoices()
    {
        _ttsVoiceIds.Clear();
        TtsVoiceButton.Clear();
        TtsVoiceButton.AddItem(Loc.GetString("humanoid-profile-editor-tts-voice-random"), TtsVoiceRandomId);

        var voices = _prototypeManager.EnumeratePrototypes<TtsVoicePrototype>()
            .Where(voice => voice.Selectable)
            .Select(voice => (Id: new ProtoId<TtsVoicePrototype>(voice.ID), Name: Loc.GetString(voice.Name)))
            .OrderBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase);

        foreach (var (id, name) in voices)
        {
            _ttsVoiceIds.Add(id);
            TtsVoiceButton.AddItem(name, _ttsVoiceIds.Count);
            TtsVoiceButton.SetItemMetadata(TtsVoiceButton.GetIdx(_ttsVoiceIds.Count), id);
        }

        UpdateTtsVoiceControls();
    }

    private void SetTtsVoice(ProtoId<TtsVoicePrototype>? voice)
    {
        Profile = Profile?.WithTtsVoice(voice);
        SetDirty();
        UpdateTtsPreviewButton();
    }

    private void UpdateTtsVoiceControls()
    {
        // A voice removed or made unselectable since the profile was saved shows as random, which is what the server
        //      will treat it as.
        var index = Profile?.TtsVoice is { } voice ? _ttsVoiceIds.IndexOf(voice) : -1;
        TtsVoiceButton.SelectId(index < 0 ? TtsVoiceRandomId : index + 1);

        UpdateTtsPreviewButton();
    }

    private void UpdateTtsPreviewButton()
    {
        var availability = _ttsPreviewManager.GetAvailability(Profile?.TtsVoice);

        TtsVoicePreviewButton.Disabled = availability != KsTtsPreviewAvailability.Available;
        TtsVoicePreviewButton.ToolTip = Loc.GetString(availability switch
        {
            KsTtsPreviewAvailability.Available => "humanoid-profile-editor-tts-voice-preview-available",
            KsTtsPreviewAvailability.TtsDisabled => "humanoid-profile-editor-tts-voice-preview-tts-disabled",
            KsTtsPreviewAvailability.NoVoice => "humanoid-profile-editor-tts-voice-preview-no-voice",
            KsTtsPreviewAvailability.Baking => "humanoid-profile-editor-tts-voice-preview-baking",
            KsTtsPreviewAvailability.Failed => "humanoid-profile-editor-tts-voice-preview-failed",
            KsTtsPreviewAvailability.Decoding => "humanoid-profile-editor-tts-voice-preview-decoding",
            KsTtsPreviewAvailability.Broken => "humanoid-profile-editor-tts-voice-preview-broken",
            _ => "humanoid-profile-editor-tts-voice-preview-missing",
        });
    }

    // The preview manager lives for the whole process, so the editor is only subscribed while it is on screen.
    private void TtsVoiceEnteredTree()
    {
        _ttsPreviewManager.AvailabilityChanged += UpdateTtsPreviewButton;
        UpdateTtsPreviewButton();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _ttsPreviewManager.AvailabilityChanged -= UpdateTtsPreviewButton;
    }
}
