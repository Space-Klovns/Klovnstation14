namespace Content.Shared._KS14.Atmos.ChemicalFire;

/// <summary>
///     Sprite layers of a chemfire. Only <see cref="Under"/> lives on the entity's sprite - the matching
///         <c>over</c> half is drawn by the client's chemfire overlay, above the effects layer.
/// </summary>
public enum ChemicalFireVisualLayers : byte
{
    Under,
}
