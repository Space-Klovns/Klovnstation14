namespace Content.Client._KS14.Sensors;

/// <summary>
///     How a sensor's coverage fan is drawn on the nav radar, cycled per sensor
///         type from its toggle button so the display can be decluttered.
/// </summary>
public enum KsCoverageDisplayMode : byte
{
    Off = 0,

    /// <summary>Just the boundary outline of the field of view.</summary>
    Outline = 1,

    /// <summary>The outline plus a translucent fill.</summary>
    Filled = 2,
}
