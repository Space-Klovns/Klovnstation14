// SPDX-FileCopyrightText: 2020 DrSmugleaf
// SPDX-FileCopyrightText: 2020 Metal Gear Sloth
// SPDX-FileCopyrightText: 2020 Paul Ritter
// SPDX-FileCopyrightText: 2020 Víctor Aguilera Puerto
// SPDX-FileCopyrightText: 2021 GraniteSidewalk
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto
// SPDX-FileCopyrightText: 2021 Visne
// SPDX-FileCopyrightText: 2022 Acruid
// SPDX-FileCopyrightText: 2022 ScalyChimp
// SPDX-FileCopyrightText: 2022 hubismal
// SPDX-FileCopyrightText: 2023 metalgearsloth
// SPDX-FileCopyrightText: 2024 Leon Friedrich
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 Quantum-cross
// SPDX-FileCopyrightText: 2025 TemporalOroboros
// SPDX-FileCopyrightText: 2025 slarticodefast
//
// SPDX-License-Identifier: MPL-2.0

using Content.Client.Atmos.Overlays;
using Content.Client.Light;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameStates;

using Content.Client.Light;
using Robust.Client.Graphics;
using Robust.Shared.Utility;


namespace Content.Client.Atmos.EntitySystems
{
    [UsedImplicitly]
    public sealed class GasTileOverlaySystem : SharedGasTileOverlaySystem
    {
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IOverlayManager _overlayMan = default!;
        [Dependency] private readonly SpriteSystem _spriteSys = default!;
        [Dependency] private readonly SharedTransformSystem _xformSys = default!;

        public GasTileOverlay _overlay = default!;
        private CanisterOverlay _canisterOverlay = default!; // KS14

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<GasOverlayUpdateEvent>(HandleGasOverlayUpdate);
            SubscribeLocalEvent<GasTileOverlayComponent, ComponentHandleState>(OnHandleState);

            _overlay = new GasTileOverlay(this, EntityManager, _resourceCache, ProtoMan, _spriteSys, _xformSys);
            _overlayMan.AddOverlay(_overlay);

            // KS14: canisteroverlay
            _canisterOverlay = new(new SpriteSpecifier.Rsi(new ResPath("/Textures/_KS14/Structures/Storage/canister.rsi"), "window-mask"), _overlay);
            _overlayMan.AddOverlay(_canisterOverlay);
        }

        public override void Shutdown()
        {
            base.Shutdown();
            _overlayMan.RemoveOverlay<CanisterOverlay>(); // KS14
            _overlayMan.RemoveOverlay<GasTileOverlay>();
        }

        private void OnHandleState(EntityUid gridUid, GasTileOverlayComponent comp, ref ComponentHandleState args)
        {
            Dictionary<Vector2i, GasOverlayChunk> modifiedChunks;

            switch (args.Current)
            {
                // is this a delta or full state?
                case GasTileOverlayDeltaState delta:
                    {
                        modifiedChunks = delta.ModifiedChunks;
                        foreach (var index in comp.Chunks.Keys)
                        {
                            if (!delta.AllChunks.Contains(index))
                                comp.Chunks.Remove(index);
                        }

                        break;
                    }
                case GasTileOverlayState state:
                    {
                        modifiedChunks = state.Chunks;
                        foreach (var index in comp.Chunks.Keys)
                        {
                            if (!state.Chunks.ContainsKey(index))
                                comp.Chunks.Remove(index);
                        }

                        break;
                    }
                default:
                    return;
            }

            foreach (var (index, data) in modifiedChunks)
            {
                comp.Chunks[index] = data;
            }
        }

        private void HandleGasOverlayUpdate(GasOverlayUpdateEvent ev)
        {
            foreach (var (nent, removedIndicies) in ev.RemovedChunks)
            {
                var grid = GetEntity(nent);

                if (!TryComp(grid, out GasTileOverlayComponent? comp))
                    continue;

                foreach (var index in removedIndicies)
                {
                    comp.Chunks.Remove(index);
                }
            }

            foreach (var (nent, gridData) in ev.UpdatedChunks)
            {
                var grid = GetEntity(nent);

                if (!TryComp(grid, out GasTileOverlayComponent? comp))
                    continue;

                foreach (var chunkData in gridData)
                {
                    comp.Chunks[chunkData.Index] = chunkData;
                }
            }
        }
    }
}
