using Robust.Shared.Map;

namespace Content.Shared._KS14.TileEffects.Effects;

public sealed partial class LogicAndTileEffect : KsTileEffect
{
    [DataField(required: true)] public KsTileEffect[] Conditions = [];
    [DataField(required: true)] public KsTileEffect[] Effects = [];

    public override void Initialize(IDependencyCollection dependencyCollection)
    {
        base.Initialize(dependencyCollection);

        foreach (var condition in Conditions)
            condition.Initialize(dependencyCollection);

        foreach (var effect in Effects)
            effect.Initialize(dependencyCollection);
    }

    public override bool Execute(TileRef tileRef, float scale, ref KsTileEffectReagentData reagentData)
    {
        foreach (var condition in Conditions)
        {
            if (condition.Execute(tileRef, scale, ref reagentData))
                continue;

            return false;
        }

        foreach (var effect in Effects)
            effect.Execute(tileRef, scale, ref reagentData);

        return true;
    }
}
