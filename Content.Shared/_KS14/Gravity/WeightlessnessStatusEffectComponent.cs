using Content.Shared.Mobs.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Gravity;

[RegisterComponent, NetworkedComponent]
[Access(typeof(MobThresholdSystem))]
public sealed partial class WeightlessnessStatusEffectComponent : Component;
