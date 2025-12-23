// SPDX-FileCopyrightText: 2023 Leon Friedrich
// SPDX-FileCopyrightText: 2023 Nemanja
// SPDX-FileCopyrightText: 2024 Ed
// SPDX-FileCopyrightText: 2024 Fildrance
// SPDX-FileCopyrightText: 2024 metalgearsloth
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 ScarKy0
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 themias
//
// SPDX-License-Identifier: MIT

using Content.Server.Power.EntitySystems;
using Content.Server.Research.Components;
using Content.Shared.UserInterface;
using Content.Shared.Access.Components;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Research.Components;
using Content.Shared._KS14.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared._KS14.Research;
using System.Linq;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    [Dependency] private readonly EmagSystem _emag = default!;

    private void InitializeConsole()
    {
        SubscribeLocalEvent<ResearchConsoleComponent, ConsoleUnlockTechnologyMessage>(OnConsoleUnlock);
        SubscribeLocalEvent<ResearchConsoleComponent, BeforeActivatableUIOpenEvent>(OnConsoleBeforeUiOpened);
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchServerPointsChangedEvent>(OnPointsChanged);
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchRegistrationChangedEvent>(OnConsoleRegistrationChanged);
        SubscribeLocalEvent<ResearchConsoleComponent, TechnologyDatabaseModifiedEvent>(OnConsoleDatabaseModified);
        SubscribeLocalEvent<ResearchConsoleComponent, TechnologyDatabaseSynchronizedEvent>(OnConsoleDatabaseSynchronized);
        SubscribeLocalEvent<ResearchConsoleComponent, GotEmaggedEvent>(OnEmagged);
    }

    private void OnConsoleUnlock(EntityUid uid, ResearchConsoleComponent component, ConsoleUnlockTechnologyMessage args)
    {
        var act = args.Actor;

        if (!this.IsPowered(uid, EntityManager))
            return;

        if (!PrototypeManager.TryIndex<TechnologyPrototype>(args.Id, out var technologyPrototype))
            return;

        if (TryComp<AccessReaderComponent>(uid, out var access) && !_accessReader.IsAllowed(act, uid, access))
        {
            _popup.PopupEntity(Loc.GetString("research-console-no-access-popup"), act);
            return;
        }

        // 1. Try to unlock the technology

        if (!UnlockTechnology(uid, args.Id, act))

            return;


        // --- FIX START ---

        // 1. Get the R&D Server and its database
        if (TryGetClientServer(uid, out var serverUid, out var researchServerComp, null) &&
            TryComp<TechnologyDatabaseComponent>(serverUid, out var serverDatabase))
        {
            foreach (var recipe in technologyPrototype.RecipeUnlocks)
            {
                // 2. Unlock on the R&D SERVER (Global)
                if (!serverDatabase.UnlockedRecipes.Contains(recipe))
                {
                    serverDatabase.UnlockedRecipes.Add(recipe);
                }

                // 3. Unlock on all connected LATHES (Clients)
                // We iterate through every machine connected to this server and force-add the recipe
                foreach (var clientUid in researchServerComp.Clients)
                {
                    if (!TryComp<TechnologyDatabaseComponent>(clientUid, out var clientDatabase))
                        continue;

                    // Helper method from SharedResearchSystem that adds + Dirties + Raises Event
                    AddLatheRecipe(clientUid, recipe, clientDatabase);
                }
            }

            // Sync the server itself
            Dirty(serverUid.Value, serverDatabase);
            var ev = new TechnologyDatabaseModifiedEvent();
            RaiseLocalEvent(serverUid.Value, ref ev);
        }

        // --- FIX END ---

        if (!_emag.CheckFlag(uid, EmagType.Interaction))
        {
            var getIdentityEvent = new TryGetIdentityShortInfoEvent(uid, act);
            RaiseLocalEvent(getIdentityEvent);

            var message = Loc.GetString(
                "research-console-unlock-technology-radio-broadcast",
                ("technology", Loc.GetString(technologyPrototype.Name)),
                ("amount", technologyPrototype.Cost),
                ("approver", getIdentityEvent.Title ?? string.Empty)
            );
            _radio.SendRadioMessage(uid, message, component.AnnouncementChannel, uid, escapeMarkup: false);
        }

        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    private void OnConsoleBeforeUiOpened(EntityUid uid, ResearchConsoleComponent component, BeforeActivatableUIOpenEvent args)
    {
        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    private void UpdateConsoleInterface(EntityUid uid, ResearchConsoleComponent? component = null, ResearchClientComponent? clientComponent = null)
    {
        if (!Resolve(uid, ref component, ref clientComponent, false))
            return;

        // Goobstation: R&D Console Rework Start
        var allTechs = PrototypeManager.EnumeratePrototypes<TechnologyPrototype>();
        Dictionary<string, ResearchAvailability> techList;
        var points = 0;

        if (TryGetClientServer(uid, out var serverUid, out var server, clientComponent) &&
            TryComp<TechnologyDatabaseComponent>(serverUid, out var db))
        {
            var unlockedTechs = db.UnlockedTechnologies.Select(id => id.ToString()).ToHashSet();
            techList = allTechs.ToDictionary(
                proto => proto.ID,
                proto =>
                {
                    if (unlockedTechs.Contains(proto.ID))
                        return ResearchAvailability.Researched;

                    var prereqsMet = proto.TechnologyPrerequisites.All(p => unlockedTechs.Contains(p));
                    var canAfford = server.Points >= proto.Cost;

                    return prereqsMet ?
                        (canAfford ? ResearchAvailability.Available : ResearchAvailability.PrereqsMet)
                        : ResearchAvailability.Unavailable;
                });

            if (clientComponent != null)
                points = clientComponent.ConnectedToServer ? server.Points : 0;
        }
        else
        {
            techList = allTechs.ToDictionary(proto => proto.ID, _ => ResearchAvailability.Unavailable);
        }

        _uiSystem.SetUiState(uid, ResearchConsoleUiKey.Key,
            new ResearchConsoleBoundInterfaceState(points, techList));
        // Goobstation: R&D Console Rework End
    }

    private void OnPointsChanged(EntityUid uid, ResearchConsoleComponent component, ref ResearchServerPointsChangedEvent args)
    {
        if (!_uiSystem.IsUiOpen(uid, ResearchConsoleUiKey.Key))
            return;
        UpdateConsoleInterface(uid, component);
    }

    private void OnConsoleRegistrationChanged(EntityUid uid, ResearchConsoleComponent component, ref ResearchRegistrationChangedEvent args)
    {
        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    private void OnConsoleDatabaseModified(EntityUid uid, ResearchConsoleComponent component, ref TechnologyDatabaseModifiedEvent args)
    {
        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    private void OnConsoleDatabaseSynchronized(EntityUid uid, ResearchConsoleComponent component, ref TechnologyDatabaseSynchronizedEvent args)
    {
        UpdateConsoleInterface(uid, component);
    }

    private void OnEmagged(Entity<ResearchConsoleComponent> ent, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        if (_emag.CheckFlag(ent, EmagType.Interaction))
            return;

        args.Handled = true;
    }

    private bool UnlockTechnology(EntityUid uid, string techId, EntityUid actor)
    {
        if (!TryComp<ResearchClientComponent>(uid, out var clientComp) || clientComp.Server is not { } serverUid)
            return false;

        if (!TryComp<ResearchServerComponent>(serverUid, out var serverComp) || !TryComp<TechnologyDatabaseComponent>(serverUid, out var db))
            return false;

        if (!PrototypeManager.TryIndex<TechnologyPrototype>(techId, out var tech))
            return false;

        if (serverComp.Points < tech.Cost)
            return false;

        if (!tech.TechnologyPrerequisites.All(prereq => db.UnlockedTechnologies.Contains(prereq)))
            return false;

        db.UnlockedTechnologies.Add(techId);
        serverComp.Points -= tech.Cost;
        Dirty(db.Owner, db);
        Dirty(serverUid, serverComp);

        return true;
    }
}
