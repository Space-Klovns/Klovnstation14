using Robust.Shared.ComponentTrees;
using Robust.Shared.Physics;

namespace Content.Client._KS14.Storage.ClosetDoor;

/// <summary>
///     Swings an entity storage's door open and shut, as tgstation's closets do, rather than swapping between a
///         closed and an open sprite state. The door is drawn by <see cref="KsClosetDoorOverlay"/>, skewed by
///         <see cref="KsClosetDoorSystem.GetDoorTransform"/>, whenever it isn't shut: while it swings, and at
///         <see cref="MaxAngle"/> for as long as the storage stays open.
/// </summary>
/// <remarks>
///     Below 90 degrees the door's front is drawn, from the sprite's
///         <see cref="Content.Client.Storage.Visualizers.StorageVisualLayers.Door"/> state. Past it, its back is
///         drawn, from the base layer's state with <see cref="BackStateSuffix"/>.
///     Every value defaults to tgstation's <c>/obj/structure/closet</c>.
/// </remarks>
[RegisterComponent]
[Access(typeof(KsClosetDoorSystem), typeof(KsClosetDoorTreeSystem))]
public sealed partial class KsClosetDoorVisualsComponent : Component, IComponentTreeEntry<KsClosetDoorVisualsComponent>
{
    /// <summary>
    ///     The door's hinge, in pixels from the sprite's centre along its width.
    /// </summary>
    [DataField]
    public float HingeX = -6.5f;

    /// <summary>
    ///     How far the door's far edge drops per unit of its width as it swings, faking perspective.
    /// </summary>
    [DataField]
    public float Squish = 0.2f;

    /// <summary>
    ///     How far the door swings open, in degrees. It rests there while the storage is open.
    /// </summary>
    [DataField]
    public float MaxAngle = 140f;

    /// <summary>
    ///     How long a swing takes, either way. Zero, as tgstation's <c>door_anim_time = 0</c>, means the door
    ///         doesn't swing at all, and the storage's own open and shut states are shown instead.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(0.2);

    /// <summary>
    ///     Sprite layers mounted on the door, such as a lock or a paper label, by layer key (<c>enum.X.Y</c> for an
    ///         enum key), with the face of the door each is on. While the door is drawn, these are hidden on the
    ///         sprite and drawn swung with the door instead, on whichever face shows.
    /// </summary>
    /// <remarks>
    ///     A layer is drawn on the door only if it was shown on the shut closet: whatever its visualizer does to it
    ///         while the closet is open is ignored.
    /// </remarks>
    [DataField]
    public Dictionary<string, KsClosetDoorFace> Layers = new();

    public const string BackStateSuffix = "_back";

    /// <summary>
    ///     Whether each of <see cref="Layers"/> was shown when the closet was last shut, which is whether it is drawn on
    ///         the door.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, bool> ShutLayerVisibility = new();

    /// <summary>
    ///     Which of <see cref="Layers"/> this system has hidden, to show again (as <see cref="ShutLayerVisibility"/>
    ///         says) once the door is shut.
    /// </summary>
    [ViewVariables]
    public HashSet<string> HiddenLayers = new();

    /// <summary>
    ///     Whether this system has hidden the sprite's shut door while the door swings shut.
    /// </summary>
    [ViewVariables]
    public bool DoorLayerHidden;

    /// <summary>
    ///     Whether the storage was open when its appearance was last seen. Null until then, so the first value seen
    ///         (on spawn, or on coming into view) is snapped to rather than swung to.
    /// </summary>
    [ViewVariables]
    public bool? Open;

    /// <summary>
    ///     When the current swing started, in real time. A swing is in progress while
    ///         <see cref="KsClosetDoorAnimatingComponent"/> is present.
    /// </summary>
    [ViewVariables]
    public TimeSpan AnimationStart;

    #region Tree

    /// <summary>
    ///     Whether the storage is itself inside a container, where it isn't drawn.
    /// </summary>
    [ViewVariables]
    public bool InContainer;

    /// <summary>
    ///     Whether there is any door to draw: the storage is open, or its door is swinging shut.
    /// </summary>
    [ViewVariables]
    public bool DoorDrawn;

    public EntityUid? TreeUid { get; set; }

    public DynamicTree<ComponentTreeEntry<KsClosetDoorVisualsComponent>>? Tree { get; set; }

    public bool AddToTree => DoorDrawn && !InContainer;

    public bool TreeUpdateQueued { get; set; }

    #endregion
}

/// <summary>
///     Which face of a closet door a layer mounted on it is on.
/// </summary>
public enum KsClosetDoorFace : byte
{
    /// <summary>
    ///     Shown while the door is swung less than 90 degrees, like a lock or a label.
    /// </summary>
    Front,

    /// <summary>
    ///     Shown once the door is swung past 90 degrees, and while it rests open.
    /// </summary>
    Back,

    Both,
}
