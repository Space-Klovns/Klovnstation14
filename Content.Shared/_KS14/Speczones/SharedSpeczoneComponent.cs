using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Speczones;

[Access(typeof(SharedSpeczoneSystem))]
[NetworkedComponent]
public abstract partial class SharedSpeczoneComponent : Component;
