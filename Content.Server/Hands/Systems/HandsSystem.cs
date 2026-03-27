using Content.Shared.Explosion;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameStates;

namespace Content.Server.Hands.Systems
{
    public sealed class HandsSystem : SharedHandsSystem
    {

        // Trauma - moved query and DropHeldItemsSpread to PredictedHandsSystem

        public override void Initialize()
        {
            base.Initialize();

            // Trauma - moved OnDisarmed to PredictedHandsSystem

            SubscribeLocalEvent<HandsComponent, ComponentGetState>(GetComponentState);

            SubscribeLocalEvent<HandsComponent, BeforeExplodeEvent>(OnExploded);

            // Trauma - moved OnDropHandItems and HandleThrowItem to PredictedHandsSystem
        }

        // Trauma - moved Shutdown to PredictedHandsSystem

        private void GetComponentState(EntityUid uid, HandsComponent hands, ref ComponentGetState args)
        {
            args.State = new HandsComponentState(hands);
        }


        private void OnExploded(Entity<HandsComponent> ent, ref BeforeExplodeEvent args)
        {
            if (ent.Comp.DisableExplosionRecursion)
                return;

            foreach (var held in EnumerateHeld(ent.AsNullable()))
            {
                args.Contents.Add(held);
            }
        }

        #region interactions

        // Trauma - moved everything here to PredictedHandsSystem

        #endregion
    }
}
