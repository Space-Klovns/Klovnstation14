## The jianghu: face and honor duels

jianghu-face-gained = You gain face! ({$reason}) Face: {$face}
jianghu-face-lost = You lose face! ({$reason}) Face: {$face}

jianghu-reason-draw = an honorable draw
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
jianghu-duel-how-Yielded = has yielded
jianghu-duel-how-Fled = has fled
jianghu-duel-how-Vanished = has vanished
jianghu-duel-how-Draw = has drawn
jianghu-duel-yields = {CAPITALIZE(THE($user))} raises a hand and yields.
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
