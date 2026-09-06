using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._KS14.Shuttles.UI;

/// <summary>
///     XAML-root shim for chrome-less KS windows: XamlIL insists on an
///         instantiable public root type and <see cref="BaseWindow"/> is abstract,
///         so a fully custom-chromed window (<see cref="KsInstrumentWindow"/>)
///         roots its XAML here instead. Deliberately behaviour-free.
/// </summary>
[Virtual]
public class KsBaseWindow : BaseWindow;
