using Content.Client.Stylesheets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Changelog
{
    public sealed partial class ChangelogButton : Button
    {
        [Dependency] private ChangelogManager _changelogManager = default!;

        // KS14: the lobby shows the label as an uppercase chip
        public bool KsUpperCase;

        public ChangelogButton()
        {
            IoCManager.InjectDependencies(this);

            // So that measuring before opening returns a correct height,
            // and the window has the correct size when opened.
            Text = " ";
        }

        protected override void EnteredTree()
        {
            base.EnteredTree();

            _changelogManager.NewChangelogEntriesChanged += UpdateStuff;
            UpdateStuff();
        }

        protected override void ExitedTree()
        {
            base.ExitedTree();

            _changelogManager.NewChangelogEntriesChanged -= UpdateStuff;
        }

        private void UpdateStuff()
        {
            if (_changelogManager.NewChangelogEntries)
            {
                Text = Loc.GetString(KsUpperCase ? "ks-lobby-chip-changelog-new" : "changelog-button-new-entries") /* KS14: uppercase variant */;
                StyleClasses.Add(StyleClass.Positive);
            }
            else
            {
                Text = Loc.GetString(KsUpperCase ? "ks-lobby-chip-changelog" : "changelog-button") /* KS14: uppercase variant */;
                StyleClasses.Remove(StyleClass.Positive);
            }
        }
    }
}
