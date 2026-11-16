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
- **Breakthrough** needs full insight and means enduring heaven's tribulation: `3 + 2 × realm` cycles of a
  do-after, each with a chance of tribulation lightning (lightning rods nearby take most of it). Readiness
  (`CultivationSystem.BreakthroughReadiness`) is 50, minus 20 per injury, plus bonuses for a qi source, nearly full
  qi and a tempered Dao heart; 70 or more is certain. Anything else can add to it through
  `CultivationBreakthroughReadinessEvent`. A failure (a qi deviation) halves insight, injures, and may tear loose a
  heart demon (`HeartDemonSystem`): kill it close by and the Dao heart is tempered, let it go and it costs more.
- **Ascension:** at Nascent Soul with full insight, the breakthrough action begins a 20-cycle tribulation. Surviving
  it leaves the round as an immortal.
- **Internal injury:** any single blow of 15 or more damage adds a stack (up to three). Stacks slow qi recovery and
  give spending qi a chance to backlash.
- **Laws** (`cultivationLaw` prototypes) are learned from manuals. The first is the Root Law, which twists one of
  its techniques (see each law's `rootDescription`); a second, Complementary Law fits from Foundation. Each law
  lists its techniques and the realm that opens them; `CultivationSystem.GrantTechniques` hands them out.

Besides each law's techniques, every cultivator comes to the universal techniques
(`CultivationSystem.UniversalTechniques`: Spiritual Sense, Qinggong, Acupoint Sealing, talismans, formations,
spirit beasts, Body Outside the Body and more), and two laws together can resonate into a combination technique
(`cultivationCombo` prototypes). Spiritual Sense raises `CultivationSensedEvent` on everyone it sweeps, so any system
can add what it notices; meditation raises `CultivationMeditatedEvent` after each cycle.

The antagonist is `KsCultivator` (rule `KsCultivators`): one to three cultivators, each starting with a random
law manual in hand.

## The Demonic Path

`ForbiddenArtsSystem`. The Heaven-Devouring Demonic Scripture (`KsDemonicScripture`, 18 TC in the uplink) can only be
read by someone whose mind already holds an antagonist role; anyone else takes a qi deviation. Reading it awakens a
mortal and adds `DemonicPathComponent` as a master. A master's Transmit Demonic Art writes a blood slip
(`KsDemonicTransmission`) for one disciple: reading it is consent, needs no antagonist role, and makes a disciple who
can never pass the arts on.

The arts are in `ForbiddenArtsSystem.ForbiddenTechniques`, granted by realm alongside the rest:

- **Devouring Art:** drain someone dying, or devour a corpse for qi, insight and healing. A devoured corpse becomes
  `Unrevivable` (still cloneable). A dead cultivator gives up a quarter of their insight.
- **Gu worms:** a worm sips its host's health into the master's qi for five minutes. Holy water in the blood drives
  it out. Stir the Gu makes every worm bite at once, spending them.
- **Soul Search:** read a helpless (cuffed, stunned, knocked down or critical) person's job and hidden antagonist
  roles, sent privately to the searcher's chat.
- **Corpse Puppet:** a `KsJiangshi` wearing the corpse's appearance, with the real body held in a container. It
  follows its master and retaliates for them. It collapses when its time runs out, when it is cut down, or when any
  qi talisman is used on it; the body always drops out.
- **Heavenly Demon Blood Escape:** a quarter of the way to critical condition in blood, for a jump of 8 to 15 tiles
  that breaks pulls and cuffs.
- **Five-Point Exploding Heart Palm:** `ExplodingHeartComponent` counts the victim's own steps (not dragged or
  thrown) and bursts the heart when they run out, with the count shown as an alert. Chest protection that stops 40%
  of a blunt blow turns the fingers aside. Acupoint Sealing (`AcupointSealedEvent`) or two meditation cycles undo it.

Every art adds taint, which fades by two a minute and lowers breakthrough readiness by up to 30. Spiritual Sense
smells the path, gu worms, jiangshi, a planted palm and the stained hands that struck it.

## The jianghu

`JianghuSystem`, the martial world.

- **Face** is reputation, kept in `JianghuFaceComponent` on the *mind* so it survives a change of body. A body
  whose mind has been noticed gets `JianghuExamineComponent`, which shows the title (Rising Talent at 5, Renowned
  Hero at 15, Martial Legend at 30; Shamed below 0, Disgraced at -10) and any recent disgrace on examine.
- **Honor duels:** cultivators and body cultivators get Challenge to Duel. Consent goes through the McqDialogue
  prompt: a challenger holding something is first asked whether to wager it, then the one challenged can accept,
  accept wagering what they hold, or refuse (unanswered challenges lapse after 20 seconds). Stakes wait in a
  container on their owner. The duel ends when a fighter is down, stamina-critical or three quarters of the way to
  critical condition, yields, flees past 12 tiles, vanishes, or three minutes pass (a draw: both gain 1 face and keep
  their stakes). The winner gains 5 face and every stake; the loser loses 2, or 5 for fleeing. Winner and
  cultivators watching gain insight, at most every two minutes. A third party who damages a duelist is disgraced for
  ten minutes and loses 5 face. `HonorDuelResolvingEvent` lets anything raise the face a win is worth;
  `HonorDuelFinishedEvent` reports the result. The Exploding Heart Palm can't be used in a duel.
- **Sects:** Found Sect (Foundation, 50 qi) asks for a name through `QuickDialogSystem` and makes a sect: a
  `SectComponent` on an entity of its own in nullspace, so it outlives its plaque, with the members' minds
  (`SectMemberComponent`). The `KsSectPlaque` (`SectPlaqueComponent`) is a mild qi source; others touch it to petition
  the master through McqDialogue, and the master proclaims tournaments there. Members get Sect Transmission (a
  private chat line to every member) and meditate more insightfully near each other (+10% each, up to 30%, through
  `CultivationInsightMultiplierEvent`). The master gets Declare Rivalry, aimed at a member or plaque of another sect;
  beating a rival in a duel is worth 5 more face. A destroyed plaque costs every member 2 face.
- **Sect missions:** two minutes after founding, and five after each one ends, a sect is set a mission (refine pills,
  win duels, meditate by the plaque, recruit, defeat heart demons) with twenty minutes to do it. Finishing gives every
  member 15 insight and 3 face. Pill progress waits on the alchemy port.
- **Martial World Ranking:** ten places, decided only by duels: beat someone ranked above you and take their place,
  beat someone unranked and join the list. The same pair can only move each other once every five minutes. Rank one
  is First Under Heaven, announced station-wide, and refuses challenges from outside the top five; the top three
  meditate 10% more insightfully. The ranking is round state on the system, and drops minds that no longer exist.
- **Tournaments:** a Sect Master's proclamation (one every 30 minutes) makes duels worth double face for ten minutes;
  whoever wins the most is crowned Martial Champion for 10 face and 30 insight.
- **Sworn brotherhood:** a lit `KsSwornIncense` used on someone asks them (McqDialogue) to swear; after kneeling
  together they share a `SwornBondComponent` (its own entity, minds as members, at most three, one oath a round).
  Siblings within nine tiles get `SwornTogetherComponent` (less stamina damage, harder unarmed blows; melee events
  are only raised on the weapon, so armed blows don't get the bonus), feel it when a sibling is badly hurt and which
  way to run, and fly into `SwornVengeanceComponent` when one dies. Three strikes on a sibling within 20 seconds, or
  the killing blow, breaks the oath: the bond dissolves, the betrayer loses 10 face, is marked an oathbreaker for 20
  minutes, and a cultivator's guilt summons a heart demon. Sparring in an honor duel doesn't count.

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

### Fighting styles and wuxia weapons

- **Styles** (`WuxiaStyleSystem`): the Drunken Fist, Eagle Claw and Wing Chun are `Body` martial arts whose
  manuals add `WuxiaStyleComponent`. Punches (bare-handed melee hits), shoves (`DisarmedEvent`, listened for on every
  `MobStateComponent`) and grabs (`PullStartedMessage`) on the same opponent build a streak; the right streak lands a
  combo. A streak resets after five seconds or on a new opponent. The Drunken Fist hits harder and may sway out of
  the way the drunker you are; Wing Chun's Sticky Hands turns aside one blow. Each style teaches Recall Teachings
  (lists the combos privately) and Challenge to Duel.
- **Weapons** (`WuxiaWeaponSystem`, crafted from the construction menu): the meteor hammer (two-tile reach, may yank
  the target closer), rope dart (stings someone up to four tiles away and may tear their held item loose), butterfly
  swords (faster in pairs, worked out in shared `ButterflySwordSystem` because attack rate is predicted), iron fan
  (cuts deeper thrown), bamboo staff (wielded sweeps, splits after six), plum blossom needles (a cultivator's needle
  slows), willow-leaf flying daggers (more damage from a cultivator, by realm) and smoke pellets. A Sect Master issues
  sect jian at the plaque, one per member; in a member's hand the sword strikes harder and tires its target.
  In-hand sprites borrow vanilla ones; icons come from `cultivation_icons.py`.

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
  Most scale with stage. The stomp, Shattering Fist and Bull Charge smash furniture and windows at any stage and
  chip walls (`wallChip`), so a few blows bring a wall down; from stage 6 walls give way at once
  (`BodyArtSystem.WallTier`). Anyone further along the path braces against them: half damage and no knockdown.

## Messages

Popups raised over the same entity in the same tick are drawn on top of each other. Every popup over an entity in
the server cultivation code goes through `CultivationNoticeSystem` instead: `Notify` for the entity's own eyes,
`Announce` for everyone who can see it. Messages over one entity show one at a time, 1.2 seconds apart. New body
powers are also written to chat, so the details can be read again.

## Legendary armor

Armor pieces are plain clothing prototypes with `Armor` coefficients plus a marker component:

- `SoftHedgehogArmorComponent` pricks anyone who strikes the wearer up close.
- `GreatSageRegaliaComponent` marks the three pieces of the Great Sage's regalia. Wearing all three in their own
  slots adds `GreatSageComponent` (less damage, faster qi).

The sprites are hand-placed pixel grids in `Tools/_KS14/cultivation_armor.py`, fitted to the human body parts;
technique medallions and manuals come from `Tools/_KS14/cultivation_icons.py`. Both write their RSIs with license
and copyright metadata. Run them from the repository root with Pillow installed.

## Tests

`Content.IntegrationTests/Tests/_KS14/Cultivation` covers the qi core, the techniques, the martial arts and armor,
the body path, the Demonic Path, the jianghu, sects, sworn brotherhood, fighting styles and weapons, and `CultivationLocaleTest` checks every cultivation locale key resolves
(`Tools/_KS14/check_cultivation_locale.py` does the same without a build). The Dugu counter, Eternal Heart,
concealment, breakthrough and Demonic Path tests (scripture gate, taint, step counting, unsealing) and the duel tests have been checked
by breaking what they cover and watching them fail. The Eternal Heart test was vacuous at first: a gibbed body reads
as "not dead". The rest still need the same check.
