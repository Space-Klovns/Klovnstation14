// SPDX-FileCopyrightText: 2026 Gerkada
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Robust.Shared.Serialization.Manager.Attributes;
using System;
using Content.Server.Chat.Systems;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.Server._KS14.GameTicking.Rules;

// 1. Store the path here instead of relying on LoadMapRule
[RegisterComponent]
public sealed partial class LoneERTRuleComponent : Component
{
    [DataField("path")]
    public ResPath? Path;
}

public sealed class LoneERTSystem : GameRuleSystem<LoneERTRuleComponent>
{
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    protected override void Added(EntityUid uid, LoneERTRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        // 2. Read from our custom component, NOT LoadMapRule
        if (component.Path == null)
        {
            Log.Error($"LoneERTRule {uid} started but 'path' is missing in YAML!");
            return;
        }
        var path = component.Path.Value;

        try
        {
            // 3. We use TryLoadMap (which supports full Maps)
            if (_mapLoader.TryLoadMap(path, out var mapEntity, out var roots))
            {
                if (mapEntity.HasValue)
                {
                    var mapId = mapEntity.Value.Comp.MapId;

                    // Initialize (Gravity/Atmos)
                    _mapSystem.InitializeMap(mapId);

                    // Unpause (Time)
                    _mapManager.SetMapPaused(mapId, false);

                    Log.Info($"LoneERT Map {mapId} initialized and unpaused.");

                    // Announcement
                    _chat.DispatchGlobalAnnouncement(
                        Loc.GetString("station-event-lone-ert-shuttle-incoming"),
                        playSound: true,
                        colorOverride: Color.Gold
                    );
                }
            }
            else
            {
                Log.Error($"Failed to load LoneERT map: {path}");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Exception loading map: {e.Message}");
        }
    }
}
