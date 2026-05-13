using Content.Shared._KS14.Signal.SignalSpeaker;
using Content.Shared._KS14.Signal.SignalSpeaker.Components;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Signal.SignalSpeaker.UI
{
    /// <summary>
    /// Initializes a <see cref="SignalSpeakerWindow"/> and updates it when new server messages are received.
    /// </summary>
    public sealed class SignalSpeakerBoundUserInterface : BoundUserInterface
    {
        [Dependency] private readonly IEntityManager _entManager = default!;

        [ViewVariables]
        private SignalSpeakerWindow? _window;

        public SignalSpeakerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            IoCManager.InjectDependencies(this);
        }

        protected override void Open()
        {
            base.Open();

            _window = this.CreateWindow<SignalSpeakerWindow>();

            if (_entManager.TryGetComponent(Owner, out SignalSpeakerComponent? signalSpeaker))
            {
                _window.SetMaxTextLength(signalSpeaker!.MaxTextChars);
            }

            _window.OnTextChanged += OnTextChanged;
            _window.OnApplyPressed += OnApplyPressed;
            Reload();
            _window.SetInitialTextState(); // Must be after Reload() has set the text
        }

        private void OnTextChanged(string newText)
        {
            // Focus moment
            if (_entManager.TryGetComponent(Owner, out SignalSpeakerComponent? signalSpeaker) &&
                signalSpeaker.AssignedText.Equals(newText))
                return;

            SendPredictedMessage(new SignalSpeakerTextChangedMessage(newText));
        }

        private void OnApplyPressed()
        {
            SendPredictedMessage(new SignalSpeakerApplyMessage());
        }

        public void Reload()
        {
            if (_window == null || !_entManager.TryGetComponent(Owner, out SignalSpeakerComponent? component))
                return;

            _window.SetCurrentText(component.AssignedText);
        }
    }
}
