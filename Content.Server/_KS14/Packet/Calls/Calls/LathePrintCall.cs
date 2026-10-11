using Content.Server._KS14.Packet.Components;
using Content.Server.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Packet.States.Calls;

public sealed class LathePrintCall : DeviceCall
{
    public string[] Recipes = [];
    public int[] Amount = [];

    private static LatheSystem _latheSystem = _entityManager.System<LatheSystem>();

    protected override void GetCall(EntityUid entity, PacketSystem packetSystem, PacketExecutorComponent executor)
    {
        for (var i = 0; i < Recipes.Length; i++)
        {
            var recipe = _protoMan.Index<LatheRecipePrototype>(Recipes[i]);

            _latheSystem.TryAddToQueue(entity, recipe, Amount[i]);
        }

        _latheSystem.TryStartProducing(entity);
    }
}
