using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Plumbing.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PlumbingStorageComponent : Component
{
    [DataField, AutoNetworkedField]
    public string SolutionName = "tank";
}
