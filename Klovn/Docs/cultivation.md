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
  do-after, each with a chance of tribulation lightning (lightning rods nearby take most of it). The storm grows with
  the realm being left (`CultivationSystem.BreakthroughLightning`): 30/45/60% a cycle for 6/14/22 shock, and the last
  cycle of the step into Nascent Soul always strikes. Readiness
  (`CultivationSystem.BreakthroughReadiness`) is 40, minus 20 per injury, plus bonuses for a qi source, nearly full
  qi and a tempered Dao heart, then the penalties every path shares (`CultivationReadinessSystem`): wounds (−1 per 2
  damage, up to −20), hungry −10, thirsty −10, qi under half −10, an art used in the last minute −10, the last step
  under five minutes ago −15, and each stranger within 3 tiles −5 (up to −15). Every realm already climbed takes 20
  off, and 70 effective is certain. The breakdown is written to chat when it starts. The odds and the
  storm are told to the cultivator when it starts. Anything else can add to it through
  `CultivationBreakthroughReadinessEvent`. A failure (a qi deviation) halves insight, injures, and may tear loose a
  heart demon (`HeartDemonSystem`): kill it close by and the Dao heart is tempered, let it go and it costs more.
  A heart demon goes down at 60 + 30 per realm (or per three body stages) and dies at 100 + 40, and hunts its host
  for as long as it lives.
- **The water law** (`WaterLawSystem` for its own arts): Still Water Ward halves incoming damage and mends 2 a second
  for 10 s plus 2 s per realm; Bubble Prison looses a glowing bubble (6 tiles a second, 15 s cooldown) that holds whoever it catches for 2.5 s
  and drowns them (4 asphyxiation a second, times realm power); Riding the Current glides 6 tiles plus 1 per realm,
  spilling water along the way; Tidal Surge hits for 18 blunt (times realm power) as it throws.
- **Returning Iron** aims only when the bound artifact is in hand (to throw it): otherwise binding and recalling go
  off at once (`KsSkipTargetingComponent`, which the client honours by using the action on the player's own spot).
- **One button:** Meditate becomes Break Through (gold, with a chime) once insight is full.
- **Ascension:** at Nascent Soul with full insight, the breakthrough action begins a 20-cycle tribulation. Surviving
  it leaves the round as an immortal.
- **Internal injury:** any single blow of 15 or more damage adds a stack (up to three). Stacks slow qi recovery and
  give spending qi a chance to backlash.
- **Laws** (`cultivationLaw` prototypes) are learned from manuals. The first is the Root Law, which twists one of
  its techniques (see each law's `rootDescription`); a second, Complementary Law fits from Foundation. Each law
  lists its techniques and the realm that opens them; `CultivationSystem.GrantTechniques` hands them out.

Besides each law's techniques, every cultivator comes to the universal techniques
(`CultivationSystem.UniversalTechniques`: Spiritual Sense, Qinggong, Acupoint Sealing, talismans, formations,
Body Outside the Body and more; Concealment Art everywhere but Ascension. Spirit Beast and Transmit Law were scrapped), and two laws together can resonate into a combination technique
(`cultivationCombo` prototypes). Techniques grow with the realm: area techniques reach one tile further for every
realm above Qi Condensation (`CultivationSystem.AreaRange`), knockbacks throw one tile further (`ThrowReach`), and
aimed techniques can be aimed two tiles further (`RefreshReach`, which sets each aimed action's range from its
prototype's). Spiritual Sense raises `CultivationSensedEvent` on everyone it sweeps, so any system
can add what it notices; meditation raises `CultivationMeditatedEvent` after each cycle.

The antagonist is `KsCultivator` (rule `KsCultivators`): one to three cultivators, each starting with a random
law manual in hand.

## Durability

Both paths get harder to kill as they climb, but neither becomes unkillable; a crew that gangs up still brings one down.

- **Qi:** +10 / +25 / +40 / +60 health by realm (Qi Condensation to Nascent Soul), and 5% / 10% / 15% less brute and burn damage from Foundation Establishment up. A Nascent Soul drops at about 1.75 times what drops a mortal.
- **Body:** much tougher, since the path is harder: +10 to +20 health per stage (125 by the ninth), more from tempering powers, and the chest's powers cut brute damage by up to 35%.

## What speeds up cultivation

All of these add together, and most count for both paths (qi insight while meditating, body tempering from training).

| Bonus | Amount | Applies to |
|---|---|---|
| Martial World Ranking, top 3 | +10% | qi and body |
| Sect members nearby | +10% each, up to +30% | qi and body |
| Sworn siblings together | +15% | qi and body |
| Noble / Jinyiwei / sect sworn to a ruler | +15% / +10% / +10% | qi and body |
| Holding the Mandate | faster cultivation | qi and body |
| Wuxia: Ascension | about double | qi and body |
| Cultivation sites, ancestral ring | varies | qi meditation |
| Gu worm sips, Devouring Art (Demonic Path) | +1 insight per sip, +30 per corpse | qi |

Face does **not** change cultivation speed. It only matters through titles, the ranking (which duels also move) and the Green Dragon Crescent Blade.

## Face, titles and the ranking

Examining yourself in a wuxia round shows your exact face, title and rank, and the Martial World Ranking.

| Event | Face |
|---|---|
| Win an honor duel | +5 (+5 more against a rival sect; doubled in a tournament) |
| Draw | nothing (so face can't be farmed) |
| Complete a sect mission | +3 to every member |
| Crowned Martial Champion | +10 |
| Pardoned by a ruler | reset to 0 if negative, then +3 |
| Lose an honor duel, or walk away from it | -2 |
| Interfere in a duel | -5 |
| Sect plaque destroyed | -2 to every member |
| Betray a sworn sibling | -10 |

Titles: 30+ Martial Legend, 15+ Renowned Hero, 5+ Rising Talent, below 0 Shamed, -10 or less Disgraced. The Green Dragon Crescent Blade's honour damage is 10 + half the wielder's face (4 to 24).

## Where and how to cultivate

- **Cultivation mat** (`KsCultivationMat`, crafted from 3 cloth). Meditating on one lets the surroundings matter
  (`CultivationSiteSystem`). Things within a couple of tiles count towards the five elements: what counts is data, the
  `fengShuiElement` prototypes in `sites.yml` (prototypes, name prefixes, components, stack materials, or anything
  burning). Each element caps at 4, and identical things count twice at most. For each of the cultivator's laws, its own
  element nourishes, the element that generates it helps, and the one that overcomes it suppresses. Being alone (no other
  player within 5 tiles) helps anywhere. The result multiplies meditation insight and adds to breakthrough readiness.
  Examining a mat shows the element readout; Spiritual Sense gives the full reading in chat.
- **Station qi**: meditating within 6 tiles of a singularity or a ball of lightning, or 2 of a fire, is far faster and
  hurts (radiation, shocks, burns).
- **Guqin** (`KsGuqin`, crafted from 8 planks and 7 cable). Every 20 seconds of playing, the player and every
  cultivator listening within 6 tiles gain insight, more the higher the player's realm; the player's torn meridians
  settle every third wave. Everyone else just feels at peace.
- **Heavenly treasures** (`HeavenlyTreasureSystem`). Every 15 to 20 minutes (the first after 12 to 18), if any
  cultivator is alive, a spirit herb blooms beside a maintenance hatch and every cultivator is told roughly where. Digging
  it up takes 4 seconds; eaten, it heals, fills a qi cultivator's dantian and gives insight, or tempers a body
  cultivator. Unclaimed, it fades after 6 minutes.
- **Round-start loot** (`CultivationLootSystem`): three qi manuals, two martial manuals, the Body Molding Art, a primer
  and an ancestral ring beside maintenance hatches, sometimes a demonic scripture, and a qi manual and a primer beside the
  library shelves.
- **Ancestral ring** (`KsAncestralRing`). A cultivator using it in hand offers a ghost role: an ancient master living in
  the stone. The master speaks only into the bearer's head (an action, not local chat), can lend them 30 qi every three
  minutes, and while awake the bearer meditates 25% faster. A bible pressed to the ring banishes the master.

## Legendary artifacts

One legendary turns up in maintenance each round (`CultivationLootSystem.SpawnLegendary`); unclaimed ones give
themselves away to cultivators at 8 and 20 minutes. `LegendarySystem` (and its partial files) runs them, prototypes in
`legendary.yml`, sprites from SS13 in `legendary.rsi`. Every blow from one is true damage (`LegendaryHit`), and each
answers the body path through `LegendaryTrait`: BreaksDefence ends Iron Shirt and the Vajra Golden Body, WearsDown
exhausts, CrushesTempered hits harder the higher the stage. Powers are in-hand uses or actions the item grants while held.

- **Ganjiang and Moye**: paired (one person holding both, or two side by side) they hit harder and spin the Twin Dragon
  Sword Storm; apart, one tugs towards the other.
- **Heaven Reliant Sword**: cuts structures like mud; the Heaven-Cleaving Stroke cuts a 14 by 3 line through
  everything, reinforced walls included.
- **Dragon Slaying Saber**: holding it makes you First Under Heaven (`JianghuSystem.ClaimFirstUnderHeaven`); woken in
  hand, its next blow is the Dragon Slaying Strike. With the Heaven Reliant Sword in hand, a verb on the saber shatters
  both and drops the Nine Yin Manual and the Wumu Yishu.
- **Ruyi Jingu Bang**: grows (reach 3) and shrinks in hand; Thirteen Thousand Jin slams a line down; Pluck Hairs sends
  two clones (`KsSageClone`); set down grown, it plants itself and only its planter can lift it.
- **Purple-Gold Gourd**: Call Their Name; a real answer (`IsAnswer`) within six seconds sucks them in to dissolve. The
  dead come out as `KsGourdElixir`.
- **Plantain Fan**: Hurricane (a cone), Typhoon (in hand, all around), Firestorm (whips fires onward).
- **Bagua Mirror**: the Eight Trigrams Seal (`CultivationSealedComponent`, checked by techniques, body arts and martial
  arts) on cultivators in sight, burns the undead and demonic, flashes everyone else; reflects shots while held.
- **Qiankun Pouch**: a huge storage; one Heaven and Earth action: at your own feet it swallows, anywhere else it releases; someone
  beside its holder can hide in its world (alt verb on the holder).
- **Xuanyuan Sword**: answers only to the Son of Heaven (`SonOfHeavenComponent`, given by the Mandate of Heaven);
  anyone else is burned picking it up.
- **Green Dragon Crescent Blade**: wielded, reaches further; its blows follow the wielder's face in the jianghu.
- **Seven-Star Sword**: each blow lights a star and every lit star adds 2 to its blows. With three or more lit, release them: they fall one after another in the shape of the Northern Dipper around the target, each cutting for 15 (once per foe); with all seven, the last is a great star (25 in a 2-tile blast, knocks down, craters the floor).
- **Linglong Pagoda**: thrown at someone, seals them inside for twenty seconds (a ward or the Golden Bell turns it).
- **Universe Ring**: thrown, ricochets to two more people and flies home.
- **Wind Fire Wheels**: shoes; much faster, no slipping, burning footprints, and the odd skid.
- **Nine Yin Manual / Wumu Yishu** (`InheritanceManualComponent`): one reader each learns the Nine Yin White Bone Claw or
  the Command of the Wumu Yishu (`WumuFormationSystem`: faster, half stamina damage).

**Swings** (`legendary_swings.yml`, `LegendarySystem.Swings`): Ganjiang and Moye cut fast with blue and crimson arcs, Moye from the other side so the pair's cuts cross; the Green Dragon Crescent Blade sweeps a slow, wide 150° crescent of jade; the Ruyi Jingu Bang drives golden thrusts and sweeps a 100° arc. Each leaves a slash of its colour on what it hits.

## Heirs

Ported from SS13 `inheritance.dm` (`InheritanceSystem`). Swearing brotherhood grants **Name Your Heir**. Taking a
disciple (Body Molding) or having your transmitted jade slip awakened does too. The heir must be a disciple or sworn
sibling; pointing the action at yourself clears the choice. The lineage is kept on the mind.

When the master dies, a golden streak flies to a living, humanoid heir, unless an Eternal Heart revival is pending.
What passes on:
- **Qi master:** half the insight. A qi heir is filled to their threshold and learns one of the master's laws if they
  have a free slot. A committed body heir gets tempering instead.
- **Body master:** half the tempering invested passes on the same way.

The master's cultivation is wiped either way. The heir also gets `InheritanceBlessingComponent`, so their next
breakthrough or Tribulation of Flesh cannot fail.

## The Mandate of Heaven

Ported from SS13 `mandate_of_heaven.dm` and `mandate_powers.dm` (`MandateSystem`). In a wuxia round each head of staff
rules their department with heaven's blessing, and the Captain is the Son of Heaven (`SonOfHeavenComponent`, which
the Xuanyuan Sword answers to). Heads already aboard get theirs when the jianghu wakes.

**What a ruler has:**
- a golden aura; the Son of Heaven also has a golden dragon coiling around them and chasing a flaming pearl (`temple.rsi`, two layers so it passes behind and in front of them);
- Dragon Qi: the weak who strike them are slowed, and later knocked down;
- Heaven's Protection: 10-20% less damage, and one fatal blow turned aside every 6-10 minutes, with lightning for
  the attacker;
- Heaven's Fortune: a 5-10% chance a blow misses;
- +10/+15 breakthrough and Tribulation readiness, and faster cultivation.

All of these grow with the dynasty's age (15, 30 and 60 minutes; twice as fast in Ascension), and each new tier adds a treasury charge.

**Powers** are all in one **Imperial Court** button, a radial menu (`ImperialCourtBoundUserInterface`). Powers used on someone open a second ring of the people within nine tiles, and each power keeps its own cooldown. The powers: Imperial Decree (rallies their people; station-wide for the Son), Kneel!,
Imperial Treasury (herbs, talismans, gold and a manual; from tier 1 a martial manual, from tier 2 a treasured armour, at tier 3 another martial manual; a legendary at 10% + 25% per tier), Imperial Pardon, Appoint Jinyiwei and
Accept Fealty (both need the other player to accept). The Son also has Ennoble and Heaven's Punishment (three bolts).

**Losing it:** the Mandate is withdrawn on death, after three minutes in cuffs, or after a minute with a demoted ID.
A jade seal falls, carrying the lineage, and an omen tells the station. Raising the seal in hand (eight seconds)
claims the Mandate. Whoever struck the ruler down is marked a regicide for five minutes, and no seal answers them.

Nobles (+15%), Jinyiwei (+10%) and members of sects sworn to a ruler (+10%) cultivate faster
(`JianghuSystem.CompanyBonus`).

## Wuxia: Ascension

A second gamemode (`KsWuxiaAscension`, vote name "Wuxia: Ascension"). Everyone gets a Fork in the Road scroll at spawn. Using it opens a radial menu (`PathChoiceBoundUserInterface`) with the path of qi, then one of the five elements, or the path of the body, each described on hover. The scroll becomes that path's book (the element's qi manual or the Body Molding Art), and reading it sets them on the path for life. Cultivation runs faster for everyone. The first to reach Nascent Soul or the ninth body stage is proclaimed Supreme Sovereign: a station-wide announcement, a quarter less damage from every blow, and the shuttle comes in 10 minutes. The round-end screen names the winner. The world events run as in the Wuxia preset, but richer: every element's manual is hidden in maintenance plus five more, seven different martial manuals, three sets of the body's arts, a demonic scripture, two shelves' worth in the library, three different legendaries, and heavenly treasures bloom a little more often (every 12-16 minutes). The last step to the peak (Golden Core to Nascent Soul, stage 8 to 9) is different for contenders (anyone not bound to the court): the first try names a random place on the station (a nav-map beacon), they can only take the step within 2.5 tiles of it, each cycle takes twice as long, and the whole station is told who is attempting to ascend and where (`WuxiaAscensionRuleSystem.CheckFinalSite`). In Ascension Nascent Soul and the ninth stage are the peak: no more insight or tempering can be gained. Swords (the legendary blades, the sect jian and butterfly swords) are Large, so they don't fit in a backpack, and are carried on the belt, the back or a vest's storage slot; the Qiankun Pouch still holds them. Spirit herbs give 25 tempering. No legendary ever turns up twice in a round, from loot, the Imperial Treasury or anywhere else.

## The Demonic Path

`ForbiddenArtsSystem`. The Heaven-Devouring Demonic Scripture (`KsDemonicScripture`, 18 TC in the uplink) can only be
read by someone whose mind already holds an antagonist role; anyone else takes a qi deviation. Reading it awakens a
mortal and adds `DemonicPathComponent` as a master. A master's Transmit Demonic Art writes a blood slip
(`KsDemonicTransmission`) for one disciple: reading it is consent, needs no antagonist role, and makes a disciple who
can never pass the arts on.

Demonic qi is vicious: every technique of someone on the path hits 30% harder (`CultivationSystem.DemonicPowerMultiplier`).

The arts are in `ForbiddenArtsSystem.ForbiddenTechniques`, granted by realm alongside the rest:

- **Devouring Art:** drain someone dying, or devour a corpse for qi, insight and healing. A devoured corpse becomes
  `Unrevivable` (still cloneable). A dead cultivator gives up a third of their insight.
- **Gu worms:** a worm sips its host's health into the master's qi for five minutes. Holy water in the blood drives
  it out. Each sip also feeds the master a little insight. Stir the Gu makes every worm bite at once (poison scaled by the master's realm, and a knockdown), spending them.
- **Soul Search:** read a helpless (cuffed, stunned, knocked down or critical) person's job and hidden antagonist
  roles, sent privately to the searcher's chat.
- **Corpse Puppet:** a `KsJiangshi` wearing the corpse's appearance, with the real body held in a container. It
  follows its master and retaliates for them. It collapses when its time runs out, when it is cut down, or when any
  qi talisman is used on it; the body always drops out.
- **Heavenly Demon Blood Escape:** a sixth of the way to critical condition in blood, for a jump of 8 to 15 tiles
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
  critical condition, walks away past 12 tiles, vanishes, or three minutes pass (a draw: nobody gains anything and both keep
  their stakes). The winner gains 5 face and every stake; the loser loses 2 (walking away concedes; there's no yield button). Winner and
  cultivators watching gain insight, at most every two minutes (a draw gives nobody anything). A third party who damages a duelist is disgraced for
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
  In-hand sprites borrow vanilla ones.

## The Body Molding Art

The other road: the flesh itself is tempered instead of gathering qi. `BodyCultivatorComponent`, written only by
`SharedBodyCultivationSystem`.

- **Tempering** is earned from training. Each source pays a solid chunk and then rests for a few seconds, so it's
  about doing things rather than clicking fast (`BodyCultivationSystem`, Training region):
  - a swing that lands on someone still standing: 8 with fists, 4 with a weapon; then 4s;
  - taking blows from someone: 1 per point of damage, up to 15; then 4s;
  - fire, cold, falls and lightning: 0.4 per point of damage, up to 6; then 15s;
  - training on walls, rock and machines: 4; then 5s;
  - using a body art: 20% of its exhaustion; then 5s;
  - living: 6 a minute;
  - beating someone into crit or death: 25 for a player, 35 for a cultivator, 5 for an animal; each victim teaches
    this once every 10 minutes.
  Every one of these is then scaled by `TrainingRate` (2/3), since forging is quick in the wuxia gamemodes.
  All of it goes through `BodyTemperingMultiplierEvent` (sect and gamemode bonuses). Tempering banks up to
  `TemperingCapFor(stage)`: enough to forge every part through the next two levels, never less than 120. The pace is
  tuned so that in Ascension a casual body cultivator reaches stage 9 in about 25-30 minutes, about five minutes after
  a casual qi cultivator reaches Nascent Soul (375/875/1625 insight, 2750 for Ascension).
- **Forge the Body** pours tempering into the least tempered of ten body parts (head, chest, arms, legs, eyes,
  ears, heart, lungs, liver, stomach). Raising a part to level `n` costs `2 + n`. Parts can go one level past the
  current stage. Each forging cycle pours 5 tempering and takes 5 seconds, or 2.5 in the wuxia gamemodes.
- **Tribulation of Flesh:** once every part is past the stage, a repeating do-after cracks the bones for a while
  and then rolls readiness. Readiness is 40, +10 fed, +10 rested (exhaustion under 10, −10 over 50), +10 in the
  Mountain stance, +10 on a meditation mat, +5 secluded, then the shared penalties above (a body art in the last minute
  counts as unsettled). Forge the Body turns gold into the Tribulation once every part is ready; from stage 4 each stage takes 4 off, and the step into stage 9 takes 25
  (`SharedBodyCultivationSystem.TribulationPenalty`); 70 effective always succeeds. From stage 4 heaven's lightning
  falls during it too (`TribulationLightning`: 20% a cycle +6% per stage, 5 + 1.75 × stage shock), and the last cycle of
  the step into stage 9 always strikes. Each cycle cracks the bones for 3 + stage/2. A failure exhausts, knocks down and, from stage 4, hurts. Success raises the
  stage, adds health and opens new arts (`BodyCultivationSystem.StageArts`).
- **Mortals stop at Copper Skin.** The Copper Skin Primer starts anyone on the path; the Body Molding Art commits
  them to it, which closes the way of qi. Qi cultivators can read neither.
- **Powers:** every part has nine `bodyTemperingPower` prototypes, one per level, in `Body/powers.yml`.
  The eyes gain night vision at 6, true night vision at 7 and Heat Sight at 8 (`KsHeatSightComponent`: living
  creatures out of sight within 8 tiles glow through walls, drawn by `KsHeatSightOverlay` above the line-of-sight
  shadow).
  Powers marked `major: true` change how you play (new senses, immunities, new kinds of blow). A body cultivator
  examining themselves gets a "Body powers" button that opens `BodyPowersWindow` (client only): each part with its
  level, progress to the next, its powers as short names (major ones in bold gold, descriptions on hover) and the next
  power greyed out.
  `SharedBodyCultivationSystem.Recalculate` combines everything unlocked into the component's modifiers. For
  damage, stamina and hunger the best (lowest) multiplier wins; for speed the best (highest); regeneration, health
  and punch bonuses add up. Health bonuses are applied by raising the critical and dead thresholds. Components a
  power grants are added the first time it unlocks.
- **Arts** (`BodyArtSystem`) cost exhaustion, which drains back on its own, and come back 5% faster per stage.
  Most scale with stage. The stomp, Shattering Fist and Bull Charge smash furniture and windows at any stage and
  chip walls (`wallChip`), so a few blows bring a wall down; from stage 6 walls give way at once
  (`BodyArtSystem.WallTier`). Anyone further along the path braces against them: half damage and no knockdown.

## Admin shortcuts

`CultivationAdminSystem` adds three submenus to an admin's right-click menu on any mob (Fun flag):

- **Cultivation: Qi** - awaken, break through one realm or straight to Nascent Soul, fill insight (ready for the
  normal breakthrough), fill qi, heal internal injuries, teach any law there is a slot for, make a Demonic master.
- **Cultivation: Body** - start the path (primer) or commit to it (Body Molding Art), advance one stage or to the
  highest the path allows (parts are forged first, then the stage is taken as a successful Tribulation), fill
  tempering, clear exhaustion.
- **Cultivation: Martial Arts** - teach any art they don't know; arts they can't take right now (no slot, too low a
  realm, not a cultivator) are greyed out with the reason.

Each step goes through the same code as play, so techniques, arts and powers are granted as usual.

## Visual and sound effects

Fire techniques are built from the game's real fire (`FireBurst`, `FireRing`); water from the extinguisher's spray, chemical puffs and mist (`WaterSplash`, `WaterRing`); wood from green and blossom puffs and drifting pollen (`WoodBloom`); metal from real sparks (`MetalSparks`, and the sword qi's impact). Water techniques slosh and splash, wood rustles, and flying swords ring as they're drawn. Earth keeps its own look and sound.

`CultivationVisualsSystem` is the one place effects come from.

- **Bursts** are short-lived entities in ten colours (`FxColour`; prototypes `KsFx<Kind><Colour>` in
  `visual_effects.yml`, sprites in `qi_effects.rsi`): a ring of qi (`Wave`),
  a hit `Spark`, rising `Motes`, a pillar of light, and the explosion shockwave shader rippling the air. Each is
  unshaded and carries its own coloured, fading point light.
- **`Cast`** runs on every technique (from `CultivationSystem.TryUseTechnique`, body arts' `TryExert`, stamina arts
  and forbidden arts) in the caster's colour: their Root Law's element, earth for the body, blood for the Demonic
  Path. It grows with `Power` (realm, or a third of body stage): a ring and a sound; then motes; then a great wave;
  and at the peak a shockwave. Each colour has its own sound cue, louder with power. `Strike` marks targets.
- **Auras** follow lasting states: `Initialize` lists which component (and condition) wears which aura, dome or
  floor sigil, and `Update` attaches and removes them. Add a line there for any new lasting state.
- **Fire** techniques spawn real flame sprites that light the room (`Flames`); they are for show, the technique does
  the burning.
- **Body arts** also call `BodyArtSystem.Impact`: a crater that lingers for half a minute (`KsFxCrater`, bigger with
  strength), crack decals around it (cleanable), flying rubble and dust, and a camera kick for everyone nearby.
- **Breaking things** goes through `BodyArtSystem.Smash` and `CultivationRubbleSystem`, as SS13's `dismantle_wall` did.
  Nothing vanishes: a blow that would overkill a structure stops just short of its last threshold, so windows leave
  shards, tables their parts, girders a sheet. The first wall tier knocks a plain wall down to its girder with half its
  plating strewn about; the second devastates any wall into half of everything that went into it.
- A successful breakthrough or Tribulation of Flesh plays `Breakthrough`: pillar, great golden wave, shockwave, motes,
  and the ascension swell of strings and temple bell.

### Effects ported from SS13

The SurfShack13 cultivation DMIs were converted to RSIs (`wuxia_32`, `wuxia_64`,
`wuxia_96`, `wuxia_128`, `buddha_palm`; prototypes in `wuxia_effects.yml`). SS13 moved these with `animate()` at
runtime; SS14 has no server-side tweening, so the motion is baked into frames. An animated state loops, so each effect
despawns as its one run ends.

- **Buddha's Palm**: the shadow grows on the floor while a golden palm fades in high above and drops onto it (1.2 s),
  squashes and springs back. Then a great bell, a crushed inner ring, a shockwave hurling back the outer ring,
  smashed structures and a crater. Under a Dharma Idol the palm is twice the size.
- **Void Step** opens a rift at both ends. Sword qi and thorn swords fly as their SS13 sprites and splash on impact.
  Binding Vines shoot up around the target. Molten Step leaves glowing footprints that cool to black. Mud Prison is
  the SS13 mud. Mountain Collapse drops a boulder on each foe, and Tidal Surge rears up a ring of water.
- **Lasting states** wear their SS13 sprites: Golden Bell (and Vajra Body) the golden bell, the water wards a bubble,
  Dharma Idol the idol, tribulations a storm cloud overhead, heart demons and spirit beasts their auras.

- **Every other art has its own animation** (`wuxia_arts.rsi`, prototypes `KsFxArt*` in the generated
  `wuxia_arts.yml`, named on `CultivationVisualsSystem` and played with `Art`/`ArtOn`): qi palm prints (Empty Palm,
  Burning Palm, Exploding Heart Palm, Sky-Splitting Palm), swords raining on targets, a wheel of orbiting blades for Sword
  Formation, the SS13 flying sword under a sword rider and a golden cloud where a qinggong leap starts, a talisman burning
  away, embers, petals, gold and void motes, fist bursts and a hundred-fist barrage, rings of flame, a devouring blood
  vortex, rings of a roar or of realm pressure, the ground splitting open, a meteor for Falling Mountain, an iron or golden
  sheen over the body, healing glows, and mists of blood, steam, dust and shadow. Formation arrays are the SS13 sigils.

Action icons are studded, bevelled medallions and martial-art manuals are bound books. Where SS13 drew an item better
(the element and body manuals, needles, flying dagger, smoke pellet, talisman, sect plaque) its sprite is used.

`Audio/_KS14/Cultivation` holds SS13's layered sounds mixed into single files: the great bell and temple bell, guqin
phrases, wind chimes, the qinggong whoosh, a mountain falling, and the ascension.

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

The sprites are hand-placed pixel art fitted to the human body parts.

## Tests

`Content.IntegrationTests/Tests/_KS14/Cultivation` keeps a core set:
- `CultivationTest`: the qi core (realms, meditation, breakthroughs, deviation);
- `BodyCultivationTest`: the body path;
- `CultivationPowerTest`: realm power, Buddha's Palm, body fists and destruction, durability, Hundred Fists, technique slots and night vision;
- `CultivationSpeedTest`: what speeds cultivation up;
- `HeartDemonTest`: heart demons fight their host;
- `LegendaryTest`: the legendaries, which never turn up twice;
- `MandateTest`: the Mandate of Heaven;
- `AscensionTest`: the Ascension gamemode's path choice and its Supreme Sovereign;
- `CultivationLocaleTest`: every cultivation locale key resolves.

## Arts menus and the bar

`CultivationLoadoutSystem` gives anyone who learns an art four category buttons (`ActionKsArts*`): Offensive,
Defensive, Movement and Utility Arts. Each opens a radial menu at the mouse (`CultivationArtsMenuSystem`, client) of
every art of that kind (`CultivationArtCategoryComponent` on the action) and picking one uses it. Learned arts stay
attached but off the bar (`ActionComponent.AutoPopulate` false, honoured by the action bar while playing); the
Favourites menu (in Utility) puts up to 5-7 on the bar. Qi arts the cultivator can't afford are disabled (greyed).
While aiming a cultivation art the cursor icon turns red out of range (`KsAimRangeOverlay`). On the action bar,
shift-click a slot then press a key to bind it, and alt-click a slot to pop it out as a floating button
(`ActionUIController.Klovn.Rebind/Floating`).

Merged buttons: Meditate/Break Through, Forge/Tribulation, one Stance button cycling Thunderclap, Mountain, none,
one Arrays button asking which array, and
Returning Iron for the bound artifact (holding an unbound item: bind; holding the artifact: hurl it where clicked,
hurting only whoever it hits for 1.5x its own blow plus 0.75x per realm, then it flies home; empty-handed: recall).
Spring Mending grows into Myriad Spring (`CultivationUpgrades`): a targeted heal on someone and every friend around them.

The Path of qi examine button opens a window of each realm and the techniques it brings; the Body powers window lists
the arts by stage. Reading a first law or the body primer writes a short guide to chat.

## The Sacred Site

In the wuxia gamemodes (`SacredSiteSystem`) heaven and earth's qi wells up at a random station nav beacon from three
minutes in, lasting two minutes, then gone for three to eight before welling up at another beacon; the station is told each time it appears and ebbs. Meditating or forging the body within
three tiles of it goes twice as fast.


### Keys and the final-step arrow

Keys belong to arts, not to bar slots (`ActionUIController.Klovn.Rebind.cs`): shift-click an art on the bar or a
floating button and press a key; it shows on that art's icon wherever it sits and is saved by prototype in the user's
data (`ks_action_keys.txt`). Pressing its own key again clears it. Mouse buttons other than left and right (the
middle and side buttons) can be bound too. Ctrl-click an aimed art to set it to quick-cast (a blue outline, saved in
`ks_action_quickcast.txt`): its key then casts at the cursor without the second click (`ActionsSystem.KsQuickCast`),
says "Too far" and spends nothing if the cursor is out of reach, and falls back to aiming if a person-targeted art has
nobody under the cursor. When a contender is told where to take the last step
to the peak, `KsPointArrowEvent` shows them a gold arrow toward the place for six seconds; trying again shows it again.
