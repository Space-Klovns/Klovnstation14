using Content.Shared.DoAfter;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.SalvageTablet;

[Serializable, NetSerializable]
public enum KsSalvageTabletUiKey : byte
{
    Key,
}

/// <summary>
///     A task an account is working on.
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class KsSalvageActiveTask
{
    [DataField]
    public ProtoId<KsSalvageTaskPrototype> Task;

    [DataField]
    public int Amount;

    [DataField]
    public int Progress;

    /// <summary>
    ///     Whether the salvager has taken the task on. Only a tracked task counts progress and runs out of time.
    /// </summary>
    [DataField]
    public bool Tracked;

    /// <summary>
    ///     When the task fails if it is still unfinished. Unset until the task is tracked.
    /// </summary>
    [DataField]
    public TimeSpan? EndTime;
}

/// <summary>
///     Everything the tablet shows. The account lives on the ID card and only on the server.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSalvageTabletBuiState : BoundUserInterfaceState
{
    public bool LoggedIn;
    public string OwnerName = string.Empty;
    public string JobTitle = string.Empty;

    /// <summary>
    ///     Whoever logged in, for their picture.
    /// </summary>
    public NetEntity? Owner;

    public int Points;
    public int Reputation;
    public List<KsSalvageActiveTask> Tasks = [];
    public TimeSpan NextDropTime;
    public TimeSpan NextOrderTime;

    /// <summary>
    ///     What the silo of the station holds, and what the lathes have asked for.
    /// </summary>
    public List<KsSalvageTabletSiloMaterial> Materials = [];
}

/// <param name="Requested">How much more of it the lathes have asked for, or zero.</param>
[Serializable, NetSerializable]
public record struct KsSalvageTabletSiloMaterial(ProtoId<MaterialPrototype> Material, int Amount, int Requested);

/// <summary>
///     Log the tablet in with the ID the sender carries.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSalvageTabletLoginMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class KsSalvageTabletLogoutMessage : BoundUserInterfaceMessage
{
}

/// <summary>
///     Take a task on: it starts counting progress, and its time starts running.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSalvageTabletTrackTaskMessage(ProtoId<KsSalvageTaskPrototype> task) : BoundUserInterfaceMessage
{
    public readonly ProtoId<KsSalvageTaskPrototype> Task = task;
}

/// <summary>
///     Throw a task away for another one.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSalvageTabletDropTaskMessage(ProtoId<KsSalvageTaskPrototype> task) : BoundUserInterfaceMessage
{
    public readonly ProtoId<KsSalvageTaskPrototype> Task = task;
}

/// <summary>
///     Buy the cart with mining points and have it dropped by pod.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSalvageTabletOrderMessage(Dictionary<ProtoId<KsSalvageProductPrototype>, int> cart) : BoundUserInterfaceMessage
{
    public readonly Dictionary<ProtoId<KsSalvageProductPrototype>, int> Cart = cart;
}

/// <summary>
///     Sent from the window of a lathe: ask the salvagers for what a recipe is short of.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsLatheRequestMaterialsMessage(ProtoId<LatheRecipePrototype> recipe) : BoundUserInterfaceMessage
{
    public readonly ProtoId<LatheRecipePrototype> Recipe = recipe;
}

[Serializable, NetSerializable]
public sealed partial class KsSalvageTabletScanDoAfterEvent : SimpleDoAfterEvent
{
}
