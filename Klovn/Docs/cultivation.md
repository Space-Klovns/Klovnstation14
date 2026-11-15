# Cultivation and the jianghu

This document explains the wuxia/xianxia cultivation feature: the two roads of power (qi cultivation and the Body
Molding Art), the martial arts learned from manuals, and the legendary armor. It is a port of the SurfShack13
cultivation system, rebuilt on SS14's ECS rather than translated line by line.

All code lives under `_KS14/Cultivation` in each project; prototypes under `Resources/Prototypes/_KS14/Cultivation`,
locale under `Resources/Locale/en-US/_KS14/cultivation`, textures under `Resources/Textures/_KS14/Cultivation`.

## Qi cultivation

A `CultivatorComponent` holds qi, a realm, insight towards the next breakthrough, the laws known and internal
injury. `SharedCultivationSystem` is the only writer (`[Access]`); everything else goes through its API so the qi
and injury alerts stay in step.

- **Realms:** Qi Condensation, Foundation Establishment, Golden Core, Nascent Soul. Each raises the qi cap.
- **Meditation** (`CultivationSystem`) is a repeating do-after: each cycle gathers insight and qi, every second
  cycle eases one stack of internal injury. A `CultivationQiSourceComponent` nearby multiplies both.
- **Breakthrough** needs full insight. Failing it (a qi deviation) halves insight and injures. The chance of
  failure is `SharedCultivationSystem.DeviationChance`, which internal injury pushes up sharply.
- **Internal injury:** any single blow of 15 or more damage adds a stack (up to three). Stacks slow qi recovery and
  give spending qi a chance to backlash.
- **Laws** (`cultivationLaw` prototypes) are learned from manuals. The first is the Root Law, which twists one of
  its techniques (see each law's `rootDescription`); a second, Complementary Law fits from Foundation. Each law
  lists its techniques and the realm that opens them; `CultivationSystem.GrantTechniques` hands them out.

The antagonist is `KsCultivator` (rule `KsCultivators`): one to three cultivators, each starting with a random
law manual in hand.

## Martial arts

`martialArt` prototypes, learned from `MartialArtManualComponent` items through `SharedMartialArtsSystem`:

- **Body arts** cost stamina and anyone can hold two.
- **Qi arts** cost qi, need a cultivator (and sometimes a minimum realm), and a cultivator can hold one plus one
  per realm.
- **Forbidden arts** add `DarkArtsComponent`, which others can feel on examine.
- **Passive arts** add components through the prototype's `components` registry (Nine Yang, Muscle-Tendon).

The arts and the novels they come from are listed in `martial_arts.yml`. Short-lived state they leave on people
(Dugu's counter, Great Shift, the Dragon Palm combo) lives in server components written only by
`MartialArtsSystem`.

## The Body Molding Art

The other road: the flesh itself is tempered instead of gathering qi. `BodyCultivatorComponent`, written only by
`SharedBodyCultivationSystem`.

- **Tempering** is earned from training: being hit, punching people and things, and simply living. It banks up to
  a cap.
- **Forge the Body** pours tempering into the least tempered of ten body parts (head, chest, arms, legs, eyes,
  ears, heart, lungs, liver, stomach). Raising a part to level `n` costs `2 + n`. Parts can go one level past the
  current stage.
- **Tribulation of Flesh:** once every part is past the stage, a repeating do-after cracks the bones for a while
  and then rolls readiness (70 or more always succeeds). Success raises the stage, adds health and opens new arts
  (`BodyCultivationSystem.StageArts`).
- **Mortals stop at Copper Skin.** The Copper Skin Primer starts anyone on the path; the Body Molding Art commits
  them to it, which closes the way of qi. Qi cultivators can read neither.
- **Powers:** every part has nine `bodyTemperingPower` prototypes, one per level, in `Body/powers.yml`.
  `SharedBodyCultivationSystem.Recalculate` combines everything unlocked into the component's modifiers. For
  damage, stamina and hunger the best (lowest) multiplier wins; for speed the best (highest); regeneration, health
  and punch bonuses add up. Health bonuses are applied by raising the critical and dead thresholds. Components a
  power grants are added the first time it unlocks.
- **Arts** (`BodyArtSystem`) cost exhaustion, which drains back on its own, and come back 5% faster per stage.
  Most scale with stage, and from stage 6 they break walls (`BodyArtSystem.WallTier`). Anyone further along the
  path braces against them: half damage and no knockdown.

## Legendary armor

Armor pieces are plain clothing prototypes with `Armor` coefficients plus a marker component:

- `SoftHedgehogArmorComponent` pricks anyone who strikes the wearer up close.
- `GreatSageRegaliaComponent` marks the three pieces of the Great Sage's regalia. Wearing all three in their own
  slots adds `GreatSageComponent` (less damage, faster qi).

The sprites are hand-placed pixel grids in `Tools/_KS14/cultivation_armor.py`, fitted to the human body parts;
technique medallions and manuals come from `Tools/_KS14/cultivation_icons.py`. Both write their RSIs with license
and copyright metadata. Run them from the repository root with Pillow installed.

## Tests

`Content.IntegrationTests/Tests/_KS14/Cultivation` covers the qi core, the martial arts and armor, and the body
path. The Dugu counter and Eternal Heart tests have been checked by breaking what they cover and watching them
fail (the Eternal Heart test was vacuous at first: a gibbed body reads as "not dead"). The rest still need the
same check.
