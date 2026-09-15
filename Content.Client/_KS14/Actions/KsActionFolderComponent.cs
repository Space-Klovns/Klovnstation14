using System.Collections.Generic;

namespace Content.Client._KS14.Actions;

/// <summary>Stores client-only action bar folder membership.</summary>
[RegisterComponent]
public sealed partial class KsActionFolderComponent : Component
{
    public readonly List<EntityUid> Actions = [];
    public bool IsExit;
}
