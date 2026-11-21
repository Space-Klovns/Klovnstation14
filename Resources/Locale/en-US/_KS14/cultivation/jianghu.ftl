## The jianghu: face and honor duels

jianghu-face-gained = You gain face! ({$reason}) Face: {$face}
jianghu-face-lost = You lose face! ({$reason}) Face: {$face}

jianghu-reason-won = won an honor duel
jianghu-reason-lost = lost an honor duel
jianghu-reason-fled = fled from a duel
jianghu-reason-interfered = interfered in an honor duel

jianghu-title-disgraced = Disgraced
jianghu-title-shamed = Shamed
jianghu-title-rising = Rising Talent
jianghu-title-hero = Renowned Hero
jianghu-title-legend = Martial Legend

jianghu-examine-title = In the jianghu, {SUBJECT($target)} {CONJUGATE-BE($target)} known as a [bold]{$title}[/bold].
jianghu-examine-dishonored = [color=#d06a6a]{CAPITALIZE(SUBJECT($target))} recently interfered in an honor duel. Shameful.[/color]
jianghu-examine-dueling = [color=#e03030]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-BE($target)} locked in an honor duel with {THE($opponent)}.[/color]

jianghu-duel-cannot-answer = They can't answer a challenge.
jianghu-duel-busy = One of you is already in a duel or a challenge.
jianghu-duel-wager-ask = Wager {THE($stake)} in your hand? The winner takes every stake.
jianghu-duel-option-wager = Wager what I'm holding
jianghu-duel-option-no-wager = No wager
jianghu-duel-option-accept = Accept the duel
jianghu-duel-option-accept-wager = Accept, wagering what I'm holding
jianghu-duel-option-refuse = Refuse
jianghu-duel-challenge = {CAPITALIZE(THE($user))} bows to {THE($target)} and challenges {OBJECT($target)} to an honor duel!
jianghu-duel-refused = {CAPITALIZE(THE($user))} refuses {POSS-ADJ($target)} challenge.
jianghu-duel-unanswered = Your challenge goes unanswered.
jianghu-duel-stake-laid = {CAPITALIZE(THE($user))} lays down {THE($stake)} as a wager.
jianghu-duel-begins = {CAPITALIZE(THE($one))} and {THE($two)} bow to each other. An honor duel begins!
jianghu-duel-draw = The duel between {THE($one)} and {THE($two)} ends in a draw. Both bow with respect.
jianghu-duel-won = {CAPITALIZE(THE($winner))} wins the honor duel! {CAPITALIZE(THE($loser))} {$how}.
jianghu-duel-how-Defeated = is beaten
jianghu-duel-how-Fled = has fled
jianghu-duel-how-Vanished = has vanished
jianghu-duel-how-Draw = has drawn
jianghu-duel-interference = {CAPITALIZE(THE($user))} interferes in an honor duel! Shameful!
jianghu-duel-beneath-notice = {CAPITALIZE(THE($target))} is First Under Heaven and won't accept challenges from anyone outside the top five of the Martial World Ranking.

## Sects

jianghu-sect-wrap = [bold]【{$sect}】[/bold] {$message}
jianghu-sect-name-title = Found Sect
jianghu-sect-name-prompt = Name your sect (Azure Cloud, Iron Toolbox, Seven Mops...)
jianghu-sect-already-master = You already lead a sect.
jianghu-sect-founded = {CAPITALIZE(THE($user))} hangs a gilded plaque and proclaims the founding of the {$sect}!
jianghu-sect-joined = {$name} has joined the sect as {$rank}!
jianghu-rank-master = Sect Master
jianghu-rank-elder = an Elder
jianghu-rank-inner = an Inner Disciple
jianghu-rank-outer = an Outer Disciple

jianghu-transmission-title = Sect Transmission
jianghu-transmission-prompt = Message to your sect
jianghu-transmission = [italic]{$name} ({$rank}):[/italic] {$message}

jianghu-rivalry-not-master = Only a Sect Master can declare rivalries.
jianghu-rivalry-no-target = That's no member or plaque of another sect worth your contempt.
jianghu-rivalry-declared = Our Sect Master has declared the {$rival} our sworn rival!
jianghu-rivalry-received = The {$rival} has declared us their sworn rival! Defeat them in honor duels for more face.
jianghu-rival-defeated = {$winner} has defeated {$loser} of our rival, the {$rival}, in an honor duel!
jianghu-rival-lost = {$loser} was defeated by {$winner} of the {$rival}. Our sect's face suffers.

jianghu-plaque-name = plaque of the {$sect}
jianghu-plaque-option-tournament = Proclaim a Martial Tournament
jianghu-plaque-option-bow = Bow
jianghu-plaque-bow = You bow to your sect's plaque. Current mission: {$mission}.
jianghu-plaque-no-master = none
jianghu-plaque-examine = Sect Master: {$master}. Members: {$members}.
jianghu-plaque-rivals = [color=#d06a6a]Sworn rivals: {$rivals}.[/color]
jianghu-plaque-mission = Sect mission: {$mission}. Missions completed: {$completed}.
jianghu-plaque-help = Members meditating near the plaque cultivate faster. Others can touch it to ask to join. The Sect Master can proclaim a Martial Tournament here.
jianghu-plaque-destroyed = Our sect's plaque has been destroyed! Our face is lost!
jianghu-reason-plaque = your sect's plaque was destroyed

jianghu-petition-no-master = The Sect Master is not available to accept you.
jianghu-petition-sent = You ask to join the {$sect}...
jianghu-petition-received = {CAPITALIZE(THE($user))} asks to join your sect.
jianghu-petition-option-accept = Accept them into the sect
jianghu-petition-option-refuse = Refuse them
jianghu-petition-refused = The Sect Master refuses you.

jianghu-examine-sect = {CAPITALIZE(SUBJECT($target))} {CONJUGATE-BE($target)} {$rank} of the [bold]{$sect}[/bold].{$relation ->
    [fellow] {" "}A fellow member of your sect.
    [rival] {" "}[color=#d06a6a]A member of your RIVAL sect![/color]
   *[none] {""}
}

## Sect missions

jianghu-mission-none = none
jianghu-mission-Pills = Refine {$goal} pills in an alchemy cauldron ({$progress}/{$goal})
jianghu-mission-Duel = Win {$goal} honor duels ({$progress}/{$goal})
jianghu-mission-Meditate = Meditate {$goal} cycles beside the sect plaque ({$progress}/{$goal})
jianghu-mission-Recruit = Accept {$goal} new members into the sect ({$progress}/{$goal})
jianghu-mission-HeartDemon = Defeat {$goal} heart demons ({$progress}/{$goal})
jianghu-mission-new = [bold]A new sect mission:[/bold] {$mission}. You have twenty minutes.
jianghu-mission-progress = Mission progress: {$progress}/{$goal}.
jianghu-mission-complete = [bold]Mission complete![/bold] The sect's fortune rises. Every member gains insight and face.
jianghu-mission-expired = The sect mission ({$mission}) has expired, unfinished.
jianghu-reason-mission = completed a sect mission

## The Martial World Ranking and tournaments

jianghu-ranking-sender = Martial World Ranking
jianghu-ranking-header = [bold]Martial World Ranking:[/bold]
jianghu-ranking-empty = [italic]Nobody has made a name in the jianghu yet. Win an honor duel to claim a place.[/italic]
jianghu-ranking-line = #{$rank} {$name}{$rank ->
    [1] {" "}[italic](First Under Heaven)[/italic]
   *[other] {""}
}
jianghu-ranking-unknown = someone forgotten
jianghu-ranking-weighed = The jianghu has already weighed you two against each other recently. The ranking doesn't change.
jianghu-ranking-climbed = You are now ranked #{$rank} in the Martial World Ranking!
jianghu-ranking-fell = You have fallen {$rank ->
    [0] outside the top ten
   *[other] to #{$rank}
} in the Martial World Ranking.
jianghu-first-under-heaven = {$name} has defeated all comers and is now known as First Under Heaven!
jianghu-examine-first = [bold]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-BE($target)} First Under Heaven, the strongest in the jianghu![/bold]
jianghu-examine-ranked = {CAPITALIZE(SUBJECT($target))} {CONJUGATE-BE($target)} ranked #{$rank} in the Martial World Ranking.
jianghu-examine-champion = [bold]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-BE($target)} the reigning Martial Champion![/bold]

jianghu-tournament-sender = Martial Tournament
jianghu-tournament-running = A tournament is already underway.
jianghu-tournament-cooldown = The jianghu is still recovering from the last tournament. ({$minutes} min)
jianghu-tournament-proclaimed = The {$sect} proclaims a Martial Tournament! For the next ten minutes, honor duels win double face. Whoever wins the most duels will be crowned Martial Champion.
jianghu-tournament-wins = Tournament: {THE($user)} now has {$wins ->
    [one] one win
   *[other] {$wins} wins
}.
jianghu-tournament-empty = The Martial Tournament ends without a single duel. How disappointing.
jianghu-tournament-champion = The Martial Tournament is over! {$name} is crowned Martial Champion with {$wins ->
    [one] one victory
   *[other] {$wins} victories
}!
jianghu-reason-champion = crowned Martial Champion

## Sworn brotherhood

jianghu-incense-unlit = Use it in hand to light it.
jianghu-incense-lit = It's lit. Use it on someone beside you to offer an oath of brotherhood.
jianghu-incense-lights = {CAPITALIZE(THE($user))} lights the incense. Fragrant smoke curls upward.
jianghu-incense-light-first = Light the incense first.
jianghu-incense-burns-down = The incense burns down to ash.
jianghu-oath-no-mind = {CAPITALIZE(THE($target))} has no mind to swear with.
jianghu-oath-already-sworn = {CAPITALIZE(THE($target))} is already sworn to others.
jianghu-oath-once = {CAPITALIZE(THE($target))} has already sworn an oath this round.
jianghu-oath-full = An oath can bind no more than three.
jianghu-oath-offered = {CAPITALIZE(THE($user))} offers lit incense to {THE($target)} and asks {OBJECT($target)} to swear brotherhood.
jianghu-oath-option-swear = Swear the oath of brotherhood
jianghu-oath-option-refuse = Refuse
jianghu-oath-declined = {CAPITALIZE(THE($target))} declines your oath.
jianghu-oath-kneel = {CAPITALIZE(THE($user))} and {THE($target)} kneel together before the burning incense.
jianghu-oath-words-one = We were not born on the same day...
jianghu-oath-words-two = ...but we will die on the same day!
jianghu-oath-sworn = You are sworn siblings! Near each other you tire less and strike harder, and you will feel it when they are hurt.
jianghu-sworn-direction = to the {$direction}
jianghu-sworn-far-away = somewhere far away
jianghu-sworn-hurt = Your heart lurches. Your sworn sibling {$name} is badly hurt {$where}!
jianghu-sworn-struck-sibling = You strike your own sworn sibling. Keep at it, and the oath is broken.
jianghu-sworn-died = Your sworn sibling {$name} has died! Grief turns to fury!
jianghu-vengeance-cools = Your fury cools into a cold, quiet grief.
jianghu-sworn-betrayal = {CAPITALIZE(THE($user))} has betrayed {POSS-ADJ($user)} sworn sibling, {THE($target)}!
jianghu-sworn-broken = {$name} has betrayed the oath! Your bond is broken.
jianghu-reason-betrayal = betrayed a sworn sibling
jianghu-examine-sworn = {CAPITALIZE(SUBJECT($target))} {CONJUGATE-BE($target)} sworn sibling to {$siblings}.
jianghu-examine-oathbreaker = [color=#d06a6a]{CAPITALIZE(SUBJECT($target))} betrayed {POSS-ADJ($target)} own sworn sibling. Oathbreaker.[/color]
jianghu-direction-North = north
jianghu-direction-NorthEast = northeast
jianghu-direction-East = east
jianghu-direction-SouthEast = southeast
jianghu-direction-South = south
jianghu-direction-SouthWest = southwest
jianghu-direction-West = west
jianghu-direction-NorthWest = northwest
jianghu-direction-Invalid = nearby

## Sect swords and wuxia weapons

jianghu-plaque-option-jian = Issue a sect jian
jianghu-jian-all-issued = Every member already has a sword. Recruit more disciples first.
jianghu-jian-name = jian of the {$sect}
jianghu-jian-issued = {CAPITALIZE(THE($user))} takes a sword from the rack beneath the plaque.
wuxia-sect-jian-examine = In a member's hand it strikes truer and tires the one it cuts.
wuxia-meteor-hammer-yank = The rope of {POSS-ADJ($user)} meteor hammer wraps around {THE($target)} and yanks {OBJECT($target)} closer!
wuxia-rope-dart-coiling = You're still coiling the cord!
wuxia-rope-dart-stings = {CAPITALIZE(POSS-ADJ($user))} rope dart darts out and stings {THE($target)}!
wuxia-rope-dart-tears = The cord snaps back and tears {THE($item)} out of {POSS-ADJ($target)} hand!
wuxia-bamboo-sweep = {CAPITALIZE(THE($user))} sweeps {POSS-ADJ($target)} legs out with the bamboo staff!
wuxia-bamboo-splits = The bamboo staff splits down its length and falls apart!
wuxia-needle-acupoint = A needle strikes an acupoint and your limbs grow heavy!
wuxia-smoke-pellet-crushed = {CAPITALIZE(THE($user))} crushes something and vanishes into a cloud of smoke!
construction-step-condition-wuxia-round = Only in a wuxia round.
jianghu-ranking-no-title = no title yet
jianghu-ranking-self = Your face: {$face} ({$title}). {$rank ->
    [0] You are not on the ranking; win an honor duel to claim a place.
   *[other] You are ranked #{$rank}.
}
