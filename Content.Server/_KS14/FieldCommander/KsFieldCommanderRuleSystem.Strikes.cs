using Content.Shared._KS14.RCD;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Strikes

    /// <summary>
    ///     Drops a missile on something the defenders built, or on a defender, outside the safe zones and away from
    ///         the defenders' own objectives. False when there was nothing it could hit.
    /// </summary>
    private bool TryStrike(KsFieldCommanderRuleComponent component, Battlefield battlefield)
    {
        var targets = new List<MapCoordinates>();

        var builtQuery = EntityQueryEnumerator<KsRcdBuiltComponent, TransformComponent>();
        while (builtQuery.MoveNext(out var builtUid, out _, out var transformComponent))
        {
            targets.Add(_transformSystem.GetMapCoordinates(builtUid, transformComponent));
        }

        targets.AddRange(battlefield.Defenders);

        // Shuffled rather than picked, so that one bad candidate doesn't call the strike off.
        _robustRandom.Shuffle(targets);

        foreach (var target in targets)
        {
            // A strike is meant to land on things, so whatever stands on the tile.
            if (!TryGetTile(target, out var strikeCoordinates, out _))
                continue;

            // Judged where the missile really lands, the centre of the tile.
            var strikeMapCoordinates = _transformSystem.ToMapCoordinates(strikeCoordinates);
            if (IsInSafeZone(component, strikeMapCoordinates, component.StrikeSafeMargin) ||
                IsNearAny(strikeMapCoordinates, battlefield.Objectives, component.StrikeSafeMargin))
                continue;

            SpawnAtPosition(component.StrikePrototype, strikeCoordinates);
            Transmit(component, PickRandom(component.StrikeAnnouncements), major: false, component.StrikeIntel);
            return true;
        }

        return false;
    }

    #endregion
}
