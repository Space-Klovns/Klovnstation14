using Content.Shared.Explosion;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameStates;

namespace Content.Server.Hands.Systems
{
    public sealed partial class HandsSystem : SharedHandsSystem
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
            // If we only switch hands don't send a full state.
            if (args.FromTick > hands.CreationTick) // KS14: was also `&& hands.LastUnclassifiedDirty >= args.FromTick`, which only let through states where nothing had changed
            {
                var aspects = EntityManager.GetModifiedAspects(hands, args.FromTick);

                if (aspects == 1UL << ActiveHandIdIndex /* KS14: was `ActiveHandIdIndex`; aspects is a bitmask, not an index */)
                {
                    args.State = new HandsComponentActiveHandDeltaState(hands.ActiveHandId);
                    return;
                }
            }

            // Get full state.
            args.State = new HandsComponentState(
                hands.ActiveHandId,
                hands.Hands,
                hands.SortedHands,
                hands.ShowInHands,
                hands.HandDisplacement,
                hands.LeftHandDisplacement,
                hands.RightHandDisplacement,
                hands.CanBeStripped,
                hands.NextThrowTime /* Trauma */);
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
