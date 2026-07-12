using Robust.Shared.Serialization;

namespace Content.Shared.IconSmoothing;

public abstract partial class SharedRandomIconSmoothSystem : EntitySystem
{
}
[Serializable, NetSerializable]
public enum RandomIconSmoothState : byte
{
    State
}
