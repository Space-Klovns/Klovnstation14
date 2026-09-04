using Content.Shared._KS14.Atmos.ChemicalFire;

namespace Content.Client._KS14.Atmos.ChemicalFire;

/// <summary>
///     Client half of the chemfire system. Atmospherics are server-only, so everything the client needs
///         already lives in <see cref="SharedChemicalFireSystem"/>; visuals are handled by
///         <see cref="ChemicalFireVisualsSystem"/> and <see cref="ChemicalFireOverlay"/>.
/// </summary>
public sealed partial class ChemicalFireSystem : SharedChemicalFireSystem;
