using System.Collections.Generic;

namespace Content.Client._KS14.Actions;

/// <summary>
/// Stable, client-local representation of an action bar and its one-level folders.
/// </summary>
public sealed class KsActionBarConfiguration
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<KsActionBarConfigurationEntry> Entries { get; set; } = [];
    public List<KsSavedActionIdentity> KnownActions { get; set; } = [];
}

public sealed class KsActionBarConfigurationEntry
{
    public KsSavedActionIdentity? Action { get; set; }
    public List<KsSavedActionIdentity>? Folder { get; set; }
}

public sealed class KsSavedActionIdentity
{
    public string ActionPrototype { get; set; } = string.Empty;
    public string? ProviderPrototype { get; set; }
    public int Occurrence { get; set; }
}
