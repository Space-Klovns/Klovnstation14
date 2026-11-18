# Perks

A perk is a `ksPerk` prototype: a name, a description, an icon, an optional alert, and lists of things given to a body
while it holds the perk. Field Commander defenders pick one per round; the framework itself knows nothing of the mode.

| Piece | Where |
| --- | --- |
| `KsPerkPrototype` (`ksPerk`) | `Content.Shared/_KS14/Perks/KsPerkPrototype.cs` |
| `KsPerkHolderComponent`, grant record, `KsPerkAddedEvent` / `KsPerkRemovedEvent` | `Content.Shared/_KS14/Perks/KsPerkHolderComponent.cs` |
| `SharedKsPerkSystem` (give, take, ask whether held) | `Content.Shared/_KS14/Perks/SharedKsPerkSystem.cs` |
| Picker: `KsPerkChoiceComponent`, `SharedKsPerkChoiceSystem`, `KsPerkChosenEvent` | `Content.Shared/_KS14/Perks/` |
| Picker window and BUI | `Content.Client/_KS14/Perks/UI/` |
| Field Commander storage, `ks_fcperk` | `Content.Server/_KS14/FieldCommander/*Perks.cs`, `KsFieldCommanderPerkCommand.cs` |
| Perks, alerts, icons | `Resources/Prototypes/_KS14/Perks/perks.yml`, `Resources/Prototypes/_KS14/Alerts/perks.yml`, `Resources/Textures/_KS14/Interface/Perks/perks.rsi` (placeholders) |
| Text | `Resources/Locale/{en-US,ru-RU}/_KS14/perks/perks.ftl` |
| Tests | `Content.IntegrationTests/Tests/_KS14/Perks/KsPerkTest.cs` |

## What a perk grants

```yaml
- type: ksPerk
  id: KsPerkTank
  name: ks-perk-tank-name
  description: ks-perk-tank-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: tank }
  alert: KsPerkTank          # one alert per perk; its name and description are the perk's
  components:                # added unless the body already has one
  - type: KsLivingShieldAbility
    capacity: 150
  statusEffects:             # made permanent (null duration)
  - StatusEffectSomething
  actions:                   # one instance each, through SharedActionsSystem.AddAction
  - ActionKsLivingShield
  items:                     # spawned into the hands, or at the feet
  - KsSomeItem
```

`SharedKsPerkSystem.TryAddPerk(body, perk)` records exactly what it added in `KsPerkHolderComponent.Grants`, and
`RemovePerk` undoes only that:

- **Components** the body already had are skipped and never removed.
- **Status effects**: one the perk started is removed; one that was already running on a timer is made permanent and
  later goes back to its own end time; one already permanent is left alone.
- **Actions** are removed and deleted; **items** are deleted wherever they are.
- Something two held perks both list stays until the last of them goes.

Giving and taking are server-only (they return false on the client). `HasPerk` works on both sides: the held list is
networked.

## Adding an ability

1. A component with the ability's data, in `Content.Shared/_KS14/Perks/Abilities/` (or the feature's own folder), and a
   system acting on it. Set it up on `ComponentStartup`/`MapInit` and undo it on `ComponentShutdown`, because removing
   the perk removes the component (CONTRIBUTING §6).
2. List the component in the perk's `components`, with its tuning there rather than in C#.
3. Shared and predicted where it can be (fire rate, stamping projectiles, toggles); server-only for spawning, injecting,
   explosions.
4. A mechanism test with its own `[TestPrototypes]` perk: add it with `TryAddPerk`, check the effect, `RemovePerk`,
   check it is gone.

If an ability needs to know about the perk rather than its component, subscribe to `KsPerkAddedEvent` /
`KsPerkRemovedEvent` on the body.

## The picker

`SharedKsPerkChoiceSystem.RequestChoice(body, options, source)` opens a radial picker on the body for its player. It has
no close button or click-away, `Close()` is refused while it waits, and the server opens it again if the interface
closes anyway. The client's pick is checked against the options on the server. A valid one raises `KsPerkChosenEvent`
(on the body and broadcast) with the `source`; whoever asked handles it, and if nobody does the perk is just given.
`CancelChoice` stops asking.

## Field Commander

- `KsFieldCommanderRuleComponent.PerkOptions` lists the perks offered; `ChosenPerks` keeps each player's pick (by
  `NetUserId`) for the round.
- `PrepareDefender` (every ticker spawn, roundstart or latejoin, and defenders already there when the rule starts) calls
  `ApplyStoredPerk` if the player has picked, otherwise `RequestPerk`. They are separate so a pick made another way
  (before joining, later) only needs `ChosenPerks` set before the spawn.
- `SetChosenPerk` (behind `ks_fcperk <player> <perk>`) replaces a pick and swaps it onto the current body.
