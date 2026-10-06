using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.PopupLocalization;

[Serializable, NetSerializable]
public enum KsPopupKind : byte { Entity, Coordinates, Cursor }

[Serializable, NetSerializable]
public sealed class KsLocalizedPopupEvent(
    KsPopupPayload message, KsPopupKind kind, PopupType type, GameTick tick,
    NetEntity entity, NetCoordinates coordinates, int predictionKey) : EntityEventArgs
{
    public KsPopupPayload Message = message;
    public KsPopupKind Kind = kind;
    public PopupType Type = type;
    public GameTick Tick = tick;
    public NetEntity Entity = entity;
    public NetCoordinates Coordinates = coordinates;
    public int PredictionKey = predictionKey;
}
