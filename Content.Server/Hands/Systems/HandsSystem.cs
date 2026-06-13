using Content.Shared.Explosion;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameStates;

namespace Content.Server.Hands.Systems
{
    public sealed partial class HandsSystem : SharedHandsSystem
    {
<<<<<<< HEAD

        // Trauma - moved query and DropHeldItemsSpread to PredictedHandsSystem
=======
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IRobustRandom _random = default!;
        [Dependency] private StackSystem _stackSystem = default!;
        [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
        [Dependency] private SharedTransformSystem _transformSystem = default!;
        [Dependency] private PullingSystem _pullingSystem = default!;
        [Dependency] private ThrowingSystem _throwingSystem = default!;
        [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

        /// <summary>
        /// Items dropped when the holder falls down will be launched in
        /// a direction offset by up to this many degrees from the holder's
        /// movement direction.
        /// </summary>
        private const float DropHeldItemsSpread = 45;
>>>>>>> upstream/master

        public override void Initialize()
        {
            base.Initialize();

            // Trauma - moved OnDisarmed to PredictedHandsSystem

            SubscribeLocalEvent<HandsComponent, ComponentGetState>(GetComponentState);

            SubscribeLocalEvent<HandsComponent, BeforeExplodeEvent>(OnExploded);

<<<<<<< HEAD
            // Trauma - moved OnDropHandItems and HandleThrowItem to PredictedHandsSystem
=======
            SubscribeLocalEvent<HandsComponent, DropHandItemsEvent>(OnDropHandItems);

            CommandBinds.Builder
                .Bind(ContentKeyFunctions.ThrowItemInHand, new PointerInputCmdHandler(HandleThrowItem))
                .Register<HandsSystem>();
>>>>>>> upstream/master
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
