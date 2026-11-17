using Content.Shared._KS14.CombatMode;
using Content.Shared.CCVar;
using Content.Shared.CombatMode;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._KS14.CombatMode;

/// <summary>
///     tgstation's combat mode toggle sounds, played to the player who toggled it only, at their interface volume, as
///         UI clicks are.
/// </summary>
public sealed partial class KsCombatModeSoundSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;

    /// <summary>
    ///     tgstation plays these at volume 25 of 100.
    /// </summary>
    private const float BaseGain = 0.25f;

    [SubscribeLocalEvent]
    private void OnToggled(Entity<CombatModeComponent> entity, ref KsCombatModeToggledEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted || entity.Owner != _playerManager.LocalEntity)
            return;

        var sound = args.Enabled ? entity.Comp.KsEnableSound : entity.Comp.KsDisableSound;
        if (sound == null)
            return;

        var gain = BaseGain * _configurationManager.GetCVar(CCVars.InterfaceVolume);
        if (gain <= 0f)
            return;

        _audioSystem.PlayGlobal(sound, Filter.Local(), false,
            sound.Params.WithVolume(SharedAudioSystem.GainToVolume(gain)));
    }
}
