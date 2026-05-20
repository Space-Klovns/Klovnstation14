using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization.Manager;
using Content.Shared.Throwing;
using Content.Shared.Tag;

namespace Content.Server._KS14.OnCollide.Comp
{
    public sealed class CompOnCollideSystem : EntitySystem
    {
        [Dependency] private readonly IComponentFactory _componentFactory = default!;
        [Dependency] private readonly TagSystem _tagSystem = default!;
        [Dependency] private readonly ISerializationManager _serializationManager = default!;

        public override void Initialize()
        {
            SubscribeLocalEvent<AddCompOnCollideComponent, StartCollideEvent>(AddCompOnCollide);
            SubscribeLocalEvent<AddCompOnCollideComponent, LandEvent>(OnAddCompLand);
            SubscribeLocalEvent<RemoveCompOnCollideComponent, StartCollideEvent>(RemCompOnCollide);
            SubscribeLocalEvent<RemoveCompOnCollideComponent, LandEvent>(OnRemCompLand);
        }

        private void OnAddCompLand(EntityUid uid, AddCompOnCollideComponent component, ref LandEvent args)
        {
            RemCompDeferred<AddCompOnCollideComponent>(uid);
        }

        private void AddCompOnCollide(EntityUid uid, AddCompOnCollideComponent component, ref StartCollideEvent args)
        {
            if (!args.OtherFixture.Hard)
                return;

            var otherEnt = args.OtherEntity;

            foreach (var (tag, components) in component.TaggedComponents)
            {
                if (!_tagSystem.HasTag(otherEnt, tag))
                    continue;

                foreach (var (name, data) in components)
                {
                    var newComp = (Component)_componentFactory.GetComponent(name);

                    if (HasComp(otherEnt, newComp.GetType()))
                        continue;

                    var temp = (object)newComp;
                    _serializationManager.CopyTo(data.Component, ref temp);
                    AddComp(otherEnt, (Component)temp!);
                }
            }
        }

        private void OnRemCompLand(EntityUid uid, RemoveCompOnCollideComponent component, ref LandEvent args)
        {
            RemCompDeferred<RemoveCompOnCollideComponent>(uid);
        }

        private void RemCompOnCollide(EntityUid uid, RemoveCompOnCollideComponent component, ref StartCollideEvent args)
        {
            if (!args.OtherFixture.Hard)
                return;

            var otherEnt = args.OtherEntity;

            foreach (var (tag, components) in component.TaggedComponents)
            {
                if (!_tagSystem.HasTag(otherEnt, tag))
                    continue;

                foreach (var (name, data) in components)
                {
                    var newComp = (Component)_componentFactory.GetComponent(name);

                    RemComp(otherEnt, newComp.GetType());
                }
            }
        }
    }
}

