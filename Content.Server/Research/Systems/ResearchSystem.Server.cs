// SPDX-FileCopyrightText: 2022 Paul Ritter
// SPDX-FileCopyrightText: 2022 Rane
// SPDX-FileCopyrightText: 2022 metalgearsloth
// SPDX-FileCopyrightText: 2024 Tayrtahn
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 Nemanja
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared.Research.Components;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private void InitializeServer()
    {
        SubscribeLocalEvent<ResearchServerComponent, ComponentStartup>(OnServerStartup);
        SubscribeLocalEvent<ResearchServerComponent, ComponentShutdown>(OnServerShutdown);
        SubscribeLocalEvent<ResearchServerComponent, TechnologyDatabaseModifiedEvent>(OnServerDatabaseModified);
    }

    private void OnServerStartup(EntityUid uid, ResearchServerComponent component, ComponentStartup args)
    {
        var unusedId = EntityQuery<ResearchServerComponent>(true)
            .Max(s => s.Id) + 1;
        component.Id = unusedId;
        Dirty(uid, component);
    }

    private void OnServerShutdown(EntityUid uid, ResearchServerComponent component, ComponentShutdown args)
    {
        foreach (var client in new List<EntityUid>(component.Clients))
        {
            UnregisterClient(client, uid, serverComponent: component, dirtyServer: false);
        }
    }

    private void OnServerDatabaseModified(EntityUid uid, ResearchServerComponent component, ref TechnologyDatabaseModifiedEvent args)
    {
        foreach (var client in component.Clients)
        {
            RaiseLocalEvent(client, ref args);
        }
    }

    private bool CanRun(EntityUid uid)
    {
        return this.IsPowered(uid, EntityManager);
    }

    private void UpdateServer(EntityUid uid, int time, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!CanRun(uid))
            return;
        ModifyServerPoints(uid, GetPointsPerSecond(uid, component) * time, component);
    }

    /// <summary>
    /// Registers a client to the specified server.
    /// </summary>
    /// <param name="client">The client being registered</param>
    /// <param name="server">The server the client is being registered to</param>
    /// <param name="clientComponent"></param>
    /// <param name="serverComponent"></param>
    /// <param name="dirtyServer">Whether or not to dirty the server component after registration</param>
    public void RegisterClient(EntityUid client, EntityUid server, ResearchClientComponent? clientComponent = null,
        ResearchServerComponent? serverComponent = null, bool dirtyServer = true)
    {
        if (!Resolve(client, ref clientComponent, false) || !Resolve(server, ref serverComponent, false))
            return;

        if (serverComponent.Clients.Contains(client))
            return;

        serverComponent.Clients.Add(client);
        clientComponent.Server = server;

        // --- START DATABASE SYNC FIX ---
        // 1. Copy all current unlocked technologies and recipes from the Server (master database)
        //    to the new Client's local database (Lathe/TechFab).
        if (TryComp<TechnologyDatabaseComponent>(server, out var serverDb) &&
            TryComp<TechnologyDatabaseComponent>(client, out var clientDb))
        {
            // Sync Unlocked Technologies (Tech IDs)
            // We use a simple loop/Contains check to avoid the UnionWith compiler error on List<T>.
            foreach (var techId in serverDb.UnlockedTechnologies)
            {
                if (!clientDb.UnlockedTechnologies.Contains(techId))
                    clientDb.UnlockedTechnologies.Add(techId);
            }

            // Sync Unlocked Recipes (Goobstation/dynamic recipes)
            foreach (var recipeId in serverDb.UnlockedRecipes)
            {
                if (!clientDb.UnlockedRecipes.Contains(recipeId))
                    clientDb.UnlockedRecipes.Add(recipeId);
            }

            // Mark the client's local database as dirty to sync the component state to the UI.
            Dirty(client, clientDb);
        }
        // --- END DATABASE SYNC FIX ---

        UpdateClientInterface(client, component: clientComponent); // This now sends the populated database state.

        if (dirtyServer && !TerminatingOrDeleted(server))
            Dirty(server, serverComponent);

        var ev = new ResearchRegistrationChangedEvent(server);
        RaiseLocalEvent(client, ref ev);
    }

    /// <summary>
    /// Unregisterse a client from its server
    /// </summary>
    /// <param name="client"></param>
    /// <param name="clientComponent"></param>
    /// <param name="dirtyServer"></param>
    public void UnregisterClient(EntityUid client, ResearchClientComponent? clientComponent = null, bool dirtyServer = true)
    {
        if (!Resolve(client, ref clientComponent))
            return;

        if (clientComponent.Server is not { } server)
            return;

        UnregisterClient(client, server, clientComponent, dirtyServer: dirtyServer);
    }

    /// <summary>
    /// Unregisters a client from its server
    /// </summary>
    /// <param name="client"></param>
    /// <param name="server"></param>
    /// <param name="clientComponent"></param>
    /// <param name="serverComponent"></param>
    /// <param name="dirtyServer"></param>
    public void UnregisterClient(EntityUid client, EntityUid server, ResearchClientComponent? clientComponent = null,
        ResearchServerComponent? serverComponent = null, bool dirtyServer = true)
    {
        if (!Resolve(client, ref clientComponent, false) || !Resolve(server, ref serverComponent, false))
            return;

        serverComponent.Clients.Remove(client);
        clientComponent.Server = null;
        UpdateClientInterface(client, component: clientComponent);

        if (dirtyServer && !TerminatingOrDeleted(server))
        {
            Dirty(server, serverComponent);
        }

        var ev = new ResearchRegistrationChangedEvent(null);
        RaiseLocalEvent(client, ref ev);
    }

    /// <summary>
    /// Gets the amount of points generated by all the server's sources in a second.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <returns></returns>
    public int GetPointsPerSecond(EntityUid uid, ResearchServerComponent? component = null)
    {
        var points = 0;

        if (!Resolve(uid, ref component))
            return points;

        if (!CanRun(uid))
            return points;

        var ev = new ResearchServerGetPointsPerSecondEvent(uid, points);
        foreach (var client in component.Clients)
        {
            RaiseLocalEvent(client, ref ev);
        }
        return ev.Points;
    }

    /// <summary>
    /// Adds a specified number of points to a server.
    /// </summary>
    /// <param name="uid">The server</param>
    /// <param name="points">The amount of points being added</param>
    /// <param name="component"></param>
    public void ModifyServerPoints(EntityUid uid, int points, ResearchServerComponent? component = null)
    {
        if (points == 0)
            return;

        if (!Resolve(uid, ref component))
            return;
        component.Points += points;
        var ev = new ResearchServerPointsChangedEvent(uid, component.Points, points);
        foreach (var client in component.Clients)
        {
            RaiseLocalEvent(client, ref ev);
        }
        Dirty(uid, component);
    }
}
