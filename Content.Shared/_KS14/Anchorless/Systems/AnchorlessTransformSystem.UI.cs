using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Anchorless.Systems;

[Serializable, NetSerializable]
public sealed class AnchorlessTransformIdentitySelectMessage(NetEntity targetIdentity) : BoundUserInterfaceMessage
{
    public readonly NetEntity TargetIdentity = targetIdentity;
}

[Serializable, NetSerializable]
public enum AnchorlessTransformUiKey : byte
{
    Key,
}
