using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Downward acceleration applied to entities transiting between z-levels, in z-levels per second squared.
    ///         Only applies where there is gravity: the grid under the entity if it has any, otherwise the map.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitGravity =
        CVarDef.Create("klovn.zlevel.transit_gravity", 2.7f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum absolute vertical speed of a transiting entity, in z-levels per second.
    ///         Without this a long fall would skip entire z-levels in a single tick.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitTerminalVelocity =
        CVarDef.Create("klovn.zlevel.transit_terminal_velocity", 10f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Impact speed, in z-levels per second, at or above which landing is damaging by default.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelTransitImpactVelocity =
        CVarDef.Create("klovn.zlevel.transit_impact_velocity", 3f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How long, in seconds, something is stunned for after being landed on.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> ZLevelTransitCrushStun =
        CVarDef.Create("klovn.zlevel.transit_crush_stun", 4f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How long, in seconds, something stays knocked down after a transit ends.
    ///     An entity is kept down for the whole transit regardless; this is only the sprawl on landing.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> ZLevelTransitLandingKnockdown =
        CVarDef.Create("klovn.zlevel.transit_landing_knockdown", 2f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Volume, in dB, added to the single footstep something plays as it lands on a z-level, over what that
    ///         same step is worth walked. For scale, an ordinary step is already given 1.5 walking and 3.5
    ///         sprinting.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> ZLevelTransitLandingFootstepVolume =
        CVarDef.Create("klovn.zlevel.transit_landing_footstep_volume", 7f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Whether lights bleed onto the z-levels below the one they are on.
    /// </summary>
    /// <remarks>
    ///     Purely a rendering matter, and the one thing here a player pays for, so it is theirs to turn off.
    ///     Everything below is the world's, not the viewer's, and is replicated so that a z-level looks the
    ///         same lit from above no matter who is looking at it.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> ZLevelLightLeakEnabled =
        CVarDef.Create("klovn.zlevel.light_leak_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How tall one z-level of <see cref="ZLevel.KsZLevelComponent.Depth"/> is, in tiles, for the purpose
    ///         of dimming light that falls through it.
    /// </summary>
    /// <remarks>
    ///     Depth is otherwise a rendering scale and a fall duration, neither of which is a distance. Light is,
    ///         so it needs one: this is the ceiling height the attenuation is measured against.
    ///     Station lights run a radius of about 4 to 7, and a light stops reaching down entirely once the drop
    ///         costs it its whole radius - so raising this much past 3 quietly stops most lights leaking.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelLightLeakHeight =
        CVarDef.Create("klovn.zlevel.light_leak_height", 1f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How many z-levels down a light is allowed to reach.
    /// </summary>
    /// <remarks>
    ///     One by default because that is as far as a client is given anything to light:
    ///         <see cref="ZLevelPvsUpdateInterval"/>'s system mirrors exactly one z-level below the viewer into
    ///         their PVS, so a stand-in any deeper than that would be lighting an empty map.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<int> ZLevelLightLeakLevels =
        CVarDef.Create("klovn.zlevel.light_leak_levels", 1, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Multiplier on the energy a leaked light arrives with, over what the attenuation says it should be.
    /// </summary>
    /// <remarks>
    ///     A knob for taste, deliberately separate from the physics: leave it at 1 and what shows up below is
    ///         exactly what the light falls off to over that distance.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelLightLeakEnergy =
        CVarDef.Create("klovn.zlevel.light_leak_energy", 1f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How many gaps in the floor a single light may shine down through.
    /// </summary>
    /// <remarks>
    ///     Each one costs a stand-in light of its own, so this is what stops a grating floor turning every
    ///         lamp above it into dozens of lights. The nearest gaps win, which are also the brightest.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<int> ZLevelLightLeakMaximumHoles =
        CVarDef.Create("klovn.zlevel.light_leak_max_holes", 4, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How often, in seconds, a light re-checks the floor beneath it for gaps.
    /// </summary>
    /// <remarks>
    ///     A light that moves or changes radius re-checks immediately; this is only what catches the floor
    ///         itself being built or cut away underneath a light that has not moved at all.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelLightLeakSearchInterval =
        CVarDef.Create("klovn.zlevel.light_leak_search_interval", 2f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether sound carries between z-levels, through the gaps in the floor between them.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> ZLevelAudioLeakEnabled =
        CVarDef.Create("klovn.zlevel.audio_leak_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How tall one z-level of <see cref="ZLevel.KsZLevelComponent.Depth"/> is, in tiles, for the purpose
    ///         of how far a sound has to travel to get through it.
    /// </summary>
    /// <remarks>
    ///     The same quantity <see cref="ZLevelLightLeakHeight"/> measures, kept separate because the two are
    ///         tuned against completely different curves - one against the light shader's falloff, one against
    ///         OpenAL's distance model - and a value that reads right for one will not for the other.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelAudioLeakHeight =
        CVarDef.Create("klovn.zlevel.audio_leak_height", 2f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How many z-levels away a sound can still be heard from.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<int> ZLevelAudioLeakLevels =
        CVarDef.Create("klovn.zlevel.audio_leak_levels", 1, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How far, in tiles, to look around a sound for a gap in the floor it could carry through.
    /// </summary>
    /// <remarks>
    ///     The search is a tile scan per leaking sound, so this is the cost knob: it grows with the square of
    ///         the radius. It is also a design one - a large value lets a sound bend a long way sideways to
    ///         find an opening, which stops sounding like it came through a hole and starts sounding misplaced.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelAudioLeakSearchRange =
        CVarDef.Create("klovn.zlevel.audio_leak_search_range", 6f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Occlusion added to a sound per z-level it carried through, on top of whatever is in its way
    ///         normally.
    /// </summary>
    /// <remarks>
    ///     Occlusion is a thickness of material, which the engine turns into a low-pass cutoff of
    ///         <c>exp(-occlusion)</c> and a gain of <c>cutoff^0.1</c>. So this is the muffling of a sound that
    ///         reached you round the lip of a hole rather than straight through the air: 0 leaves it clear,
    ///         and about 1 is already well dulled.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelAudioLeakOcclusion =
        CVarDef.Create("klovn.zlevel.audio_leak_occlusion", 0.75f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     How far, in tiles, a player can be from a sound one z-level above them and still be sent it.
    /// </summary>
    /// <remarks>
    ///     Server-side, and needed because nothing else sends it: a sound's PVS filter is map-gated, and the
    ///         z-level view subscriber only ever mirrors downwards. Without this a client is never told about
    ///         a sound above it, and there is nothing for the client half to make audible.
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelAudioLeakSendRange =
        CVarDef.Create("klovn.zlevel.audio_leak_send_range", 20f, CVar.SERVERONLY);

    /// <summary>
    ///     How often, in seconds, each player's z-level view subscriber is moved to track them.
    ///     This is what loads the z-level below into a client's PVS, and transit crossings are predicted
    ///         against it, so a slow interval shows up as the level below being briefly unpopulated.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<float> ZLevelPvsUpdateInterval =
        CVarDef.Create("klovn.zlevel.pvs_update_interval", 0.25f, CVar.SERVERONLY);
}
