## The Demonic Path

cultivation-sense-demonic = {CAPITALIZE(THE($target))} reeks of demonic qi!
cultivation-sense-gu = Something small and hungry squirms inside {THE($target)}.
cultivation-sense-jiangshi = {CAPITALIZE(THE($target))} is a corpse strung up with demonic qi.
cultivation-sense-exploding-heart = Five points of foreign qi are lodged around {POSS-ADJ($target)} heart, counting down!
cultivation-sense-heart-stain = {CAPITALIZE(POSS-ADJ($target))} palms are stained with the qi of the Five-Point Exploding Heart!

cultivation-scripture-examine = [color=#d06a6a]Your dantian clenches just looking at it. Demonic qi pours off the pages.[/color]
cultivation-scripture-spent = The pages are blank and grey. Whatever lived in them has already moved into someone's heart.
cultivation-scripture-known = You already hold these arts in your heart.
cultivation-scripture-flesh = The arts need qi to take root, and your meridians are sealed into flesh.
cultivation-scripture-start = {CAPITALIZE(THE($user))} opens {THE($book)}. The characters start to crawl.
cultivation-scripture-deviation = {CAPITALIZE(THE($user))} recoils from the page, blood running from {POSS-ADJ($user)} nose! The characters claw at the mind of anyone not already sworn to darkness.

cultivation-demonic-master = The scripture's characters crawl off the page and burrow into your heart. You have comprehended the Demonic Path! You can transmit it to others, but they can never pass it on.
cultivation-demonic-disciple = Demonic qi takes root in your dantian. You have been taught the forbidden arts. Learning them doesn't make you an antagonist: use them only as far as your role and the rules allow.
cultivation-demonic-not-master = Only one who holds the scripture in their heart can pass it on.
cultivation-demonic-transmit = {CAPITALIZE(THE($user))} bites a thumb and writes in a language that hurts to look at.

cultivation-devour-resists = There's too much life left in them to devour.
cultivation-devour-husk = Nothing is left in it to devour.
cultivation-devour-start = {CAPITALIZE(THE($user))} clamps a hand over {POSS-ADJ($target)} face. Red mist streams out of {OBJECT($target)}!
cultivation-devour-drained = Something tears loose from {THE($target)} and floods into you.
cultivation-devour-cultivator = You swallow the remnants of {POSS-ADJ($target)} cultivation!
cultivation-devour-corpse = {CAPITALIZE(THE($target))} withers to a dry husk as {THE($user)} drinks the last of {OBJECT($target)}.
cultivation-devoured-examine = [color=#9a8a6a]{CAPITALIZE(SUBJECT($target))} {CONJUGATE-BE($target)} a withered husk, drained of every drop of essence.[/color]
cultivation-devoured-unrevivable = Nothing is left in this body to bring back. Only cloning could help now.

cultivation-gu-dead = The worm won't take to a corpse.
cultivation-gu-already = {CAPITALIZE(THE($target))} already carries a worm.
cultivation-gu-sting = You feel a sharp sting.
cultivation-gu-planted = Your gu worm burrows into {THE($target)}.
cultivation-gu-none = None of your worms are alive.
cultivation-gu-stirred = You twist your qi, and far away, your worms bite.
cultivation-gu-chewing = {CAPITALIZE(THE($target))} doubles over, clutching {POSS-ADJ($target)} stomach! Something is chewing from the inside!
cultivation-gu-expelled = {CAPITALIZE(THE($target))} retches up a tiny black worm, which shrivels on the floor.
cultivation-gu-died = You feel one of your gu worms die.

cultivation-soul-search-no-soul = There's no soul there to search.
cultivation-soul-search-not-helpless = {CAPITALIZE(THE($target))} must be helpless first: cuffed, stunned, knocked down or dying.
cultivation-soul-search-start = {CAPITALIZE(THE($user))} grips {POSS-ADJ($target)} skull. {CAPITALIZE(POSS-ADJ($target))} eyes roll back!
cultivation-soul-search-felt = Something is rummaging through your memories!
cultivation-soul-search-header = The soul of {$name}:
cultivation-soul-search-calling = Calling: {$job}
cultivation-soul-search-allegiance = Hidden allegiance: {$allegiances}
cultivation-soul-search-no-allegiance = No hidden allegiance.
cultivation-soul-search-cultivation = Cultivation: {$realm}, {$laws}{$demonic ->
    [true] , the Demonic Path
   *[false] {""}
}
cultivation-soul-search-no-laws = no laws
cultivation-soul-search-body = Body: {$stage}
cultivation-soul-search-seen = Your head throbs. Someone has seen everything.

cultivation-blood-escape-nowhere = Your blood finds nowhere to carry you!
cultivation-blood-escape = {CAPITALIZE(THE($user))} spits a torrent of blood and dissolves into a red mist!

cultivation-puppet-unfit = Only a dead body, whole and humanoid, can be strung up.
cultivation-jiangshi-name = jiangshi of {$corpse}
cultivation-puppet-rises = {CAPITALIZE(THE($user))} stabs two fingers into the corpse's neck. It jerks upright, arms held straight out: {THE($target)}!
cultivation-puppet-collapses = {CAPITALIZE(THE($target))} collapses as the qi holding it together unravels!
cultivation-talisman-seals-jiangshi = {CAPITALIZE(THE($user))} slaps a talisman on the jiangshi's brow!

cultivation-exploding-heart-dead = There's no heart left to burst.
cultivation-exploding-heart-already = Five points are already lodged there.
cultivation-exploding-heart-duel = Not in an honor duel!
cultivation-exploding-heart-armored = {CAPITALIZE(THE($user))} jabs five fingers at {POSS-ADJ($target)} chest, but the armour turns them aside!
cultivation-exploding-heart-struck = {CAPITALIZE(POSS-ADJ($user))} fingers dart five times against {POSS-ADJ($target)} chest, too fast to follow!
cultivation-exploding-heart-victim = Five points around your heart burn like coals. Something is counting your steps. Stand still and find a cultivator to unseal it!
cultivation-exploding-heart-planted = Five points of qi are lodged around {POSS-ADJ($target)} heart. {$steps} steps.
cultivation-exploding-heart-pounding = Your heart pounds against your ribs. {$steps ->
    [one] One more step...
   *[other] {$steps} more steps...
}
cultivation-exploding-heart-burst = {CAPITALIZE(THE($target))} stops mid-step, clutches {POSS-ADJ($target)} chest, and blood bursts from five points around {POSS-ADJ($target)} heart!
cultivation-exploding-heart-remember = You remember the hand that struck you: {$caster}.
cultivation-exploding-heart-unsealed = The red points on {POSS-ADJ($target)} chest fade as the acupoints close.
cultivation-exploding-heart-meditated = {CAPITALIZE(THE($target))} exhales a thin thread of black qi. The red points fade.
cultivation-exploding-heart-calming = You begin to push the foreign qi out of your heart. One more cycle...
cultivation-exploding-heart-examine = [color=#d04040]Five red points glow around {POSS-ADJ($target)} heart, arranged like a plum blossom.[/color]

alerts-ks-exploding-heart-name = Five-Point Exploding Heart
alerts-ks-exploding-heart-desc = Five points of demonic qi are lodged around your heart, and this many more steps will burst it. Standing still is safe. A cultivator's Acupoint Sealing, or two cycles of your own meditation, will undo it.
