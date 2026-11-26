## Antagonist

roles-antag-ks-cultivator-name = Cultivator
roles-antag-ks-cultivator-objective = Walk the path of cultivation and rise through the realms. The jianghu does not forgive the weak.
ks-cultivator-round-end-agent-name = cultivator
ks-cultivator-role-greeting =
    You have found the path. Use the Fork in the Road you carry to walk the way of qi or the way of the body.
    On the path of qi, find a manual to take up an element's law, meditate to gather insight, and break through when you are ready.
    Heavy blows bruise your meridians: rest them, or risk a qi deviation.

## Realms

cultivation-realm-QiCondensation = Qi Condensation
cultivation-realm-FoundationEstablishment = Foundation Establishment
cultivation-realm-GoldenCore = Golden Core
cultivation-realm-NascentSoul = Nascent Soul

cultivation-examine-aura = [color=#5cba96]The air around {OBJECT($target)} feels heavy: the pressure of the {$realm} realm.[/color]

## Alerts

alerts-ks-qi-name = Qi
alerts-ks-qi-desc = The qi gathered in your dantian. Techniques spend it; it slowly returns, faster in meditation or near a place where heaven and earth's qi pools.  Examine yourself to see your realm and how much insight you need to break through.
alerts-ks-internal-injury-name = Internal Injury
alerts-ks-internal-injury-desc = Heavy blows have bruised your meridians. Qi returns slower, spending it may twist them further, and breakthroughs are far riskier. Meditate to heal.

## Core

cultivation-not-enough-qi = Your dantian is too empty for that.
cultivation-not-enough-qi-for = Not enough qi: you have {$qi}, it takes {$cost}.
cultivation-backlash = Your bruised meridians twist as the qi forces its way through!
cultivation-internal-injury = The blow shakes your meridians. You taste blood.
cultivation-injury-eased = The knot in your meridians loosens.
cultivation-meditate-start = {CAPITALIZE(THE($user))} closes {POSS-ADJ($user)} eyes and begins to breathe slowly.
cultivation-at-peak = There is no realm above yours left to reach.
cultivation-at-peak-ascension = You stand at Nascent Soul, the peak. There is no insight left to gain; meditation only restores your qi.
cultivation-not-ready = Your insight is not yet enough to break through ({$insight}/{$required}).
cultivation-breakthrough-start = Qi howls into {THE($user)} as {SUBJECT($user)} {CONJUGATE-BE($user)} forcing open the next realm!
cultivation-breakthrough-success = Heaven and earth answer: {THE($user)} {CONJUGATE-HAVE($user)} broken through to {$realm}!
cultivation-second-law-slot = Your foundation can now hold a second, Complementary Law.
cultivation-deviation = {CAPITALIZE(THE($user))} convulses as {POSS-ADJ($user)} qi turns on {OBJECT($user)}. A qi deviation!

## Manuals

cultivation-manual-gibberish = The pages are a maze of charts and riddles. You can't make heads or tails of it.
cultivation-manual-known = You already know this law by heart.
cultivation-manual-no-room-yet = Your meridians can only hold one law until you reach Foundation Establishment.
cultivation-manual-no-room = Your meridians already hold two laws.
cultivation-manual-start = {CAPITALIZE(THE($user))} starts poring over {THE($manual)}.
cultivation-learned-root = {$law} takes root in you. It is your Root Law.
cultivation-learned-complementary = You learn {$law} as your Complementary Law.

## Laws

cultivation-law-metal-name = the Golden Edge Law
cultivation-law-metal-desc = The way of the sword: qi tempered into an edge that can be thrown.
cultivation-law-metal-root = Metal Root: your Flying Sword Qi fans out into three blades.
cultivation-law-wood-name = the Evergreen Spring Law
cultivation-law-wood-desc = The way of growth: qi that mends flesh as spring mends the land.
cultivation-law-wood-root = Wood Root: Spring Mending also eases internal injuries, and some of its warmth flows back to you.
cultivation-law-water-name = the Still Lake Law
cultivation-law-water-desc = The way of water: yield to every blow and give it back.
cultivation-law-water-root = Water Root: when your Still Water Ward ends, the force it soaked up crashes out as a wave.
cultivation-law-fire-name = the Scorching Sun Law
cultivation-law-fire-desc = The way of flame: qi burned as fuel.
cultivation-law-fire-root = Fire Root: the flames of your Burning Palm catch twice as hard.
cultivation-law-earth-name = the Immovable Mountain Law
cultivation-law-earth-desc = The way of the mountain: stand, and let the world break against you.
cultivation-law-earth-root = Earth Root: Rooted Mountain Stance turns aside two fifths of every blow instead of a quarter.

## Techniques

cultivation-sword-rain = Blades of sword qi rain from {THE($user)}!
cultivation-burning-palm = {CAPITALIZE(THE($user))} strikes {THE($target)} with a burning palm!
cultivation-sea-of-flames = A sea of flames bursts out around {THE($user)}!
cultivation-still-water = Still water closes around {THE($user)}.
cultivation-ward-wave = The still water around {THE($user)} heaves and crashes outward!
cultivation-tidal-surge = A tidal surge bursts out from {THE($user)}!
cultivation-bubble-trapped = {CAPITALIZE(THE($target))} is caught in a bubble of water, unable to move or breathe!
cultivation-spring-mending = Warm green qi flows from {THE($user)} into {THE($target)}.
cultivation-myriad-spring = Springs well up around {THE($target)} as {THE($user)} pours out spring qi.
cultivation-rooted-stance = {CAPITALIZE(THE($user))} sinks into a rooted stance, immovable as a mountain.
cultivation-mountain-collapse = {CAPITALIZE(THE($user))} brings the mountain down!

## Breakthrough and heart demons

cultivation-readiness-stable = Readiness {$readiness}: certain. Endure heaven's lightning and you will break through.
cultivation-readiness-risky = Readiness {$readiness}: a {$chance}% chance. Failing brings a qi deviation, and maybe a heart demon. Heaven tests each higher realm harder; raise your readiness first: fill your dantian, meditate at a qi source or a good site, temper your Dao heart, heal internal injuries.
cultivation-lightning-strikes = Tribulation lightning crashes down on {THE($user)}!
cultivation-storm-warning = Heaven's storm gathers: each cycle has a {$chance}% chance of a bolt dealing {$damage} shock. Grounding rods nearby draw bolts away.
cultivation-storm-warning-final = Heaven's full tribulation gathers: each cycle has a {$chance}% chance of a bolt dealing {$damage} shock, and the last bolt always falls. Come in healthy, and plant grounding rods nearby.
cultivation-lightning-grounded = A bolt of tribulation lightning is drawn into {THE($rod)}!
heart-demon-name = heart demon of {$host}
heart-demon-summoned = A twisted copy of {THE($host)} tears itself free!
heart-demon-shatters = The heart demon shatters like a broken mirror!
heart-demon-defeated = You faced your own doubts and won. Your Dao heart is tempered: your next breakthrough will be steadier.
heart-demon-not-yours = The demon is gone, but you weren't there to face it. Your doubts remain.
heart-demon-returns = {CAPITALIZE(THE($host))}'s heart demon climbs back inside {OBJECT($host)}!
cultivation-self = [color=#9fd4b8]Cultivation:[/color] {$realm}. Qi {$qi}/{$maxQi}. Insight {$insight}/{$required} to break through.
cultivation-self-peak = [color=#9fd4b8]Cultivation:[/color] {$realm}, the peak. Qi {$qi}/{$maxQi}.
cultivation-ready-to-break-through = You hold all the insight this realm can give. Your Meditate button has turned gold: press it to break through.
cultivation-formation-dialogue-title = Arrays
cultivation-formation-dialogue-prompt = Which array will you inscribe at your feet?
cultivation-formation-option-gathering = Gathering Array: meditate far faster inside (2 min)
cultivation-formation-option-barrier = Barrier Array: qi walls only friends can pass (1 min)
cultivation-formation-option-alarm = Alarm Array: warns you when strangers step in (5 min)
cultivation-sacred-site-appears = Qi wells up at {$place} for two minutes: meditate or forge there at double speed.
cultivation-sacred-site-ebbs = The qi at {$place} ebbs away. It will well up somewhere else before long.
cultivation-readiness-breakdown = Readiness starts at {$base}: {$factors}.
cultivation-readiness-factor-none = nothing helping or hurting
cultivation-readiness-factor-injury = internal injuries
cultivation-readiness-factor-qi-source = at a qi source
cultivation-readiness-factor-full-qi = full dantian
cultivation-readiness-factor-low-qi = dantian under half
cultivation-readiness-factor-dao-heart = tempered Dao heart
cultivation-readiness-factor-site = the place you sit
cultivation-readiness-factor-hurt = wounds
cultivation-readiness-factor-hungry = hungry
cultivation-readiness-factor-thirsty = thirsty
cultivation-readiness-factor-unsettled = qi unsettled (an art used in the last minute)
cultivation-readiness-factor-unsteady = foundation unsteady (last step under five minutes ago)
cultivation-readiness-factor-disturbed = strangers close by
cultivation-readiness-factor-other = everything else (pills, inheritances, the Mandate, taint)
cultivation-readiness-factor-realm = heaven tests this realm harder
cultivation-readiness-factor-stage = heaven tests this stage harder
cultivation-readiness-factor-fed = well fed
cultivation-readiness-factor-mountain = Mountain stance
cultivation-readiness-factor-rested = rested
cultivation-readiness-factor-exhausted = exhausted
cultivation-guide-qi = [bold]The path of qi, in brief:[/bold]
    • [bold]Meditate[/bold] (your first button) to gather insight and refill your qi. Your qi and insight show at the side of your screen.
    • When your insight is full the Meditate button turns gold: press it to [bold]break through[/bold]. Be fed, unhurt, calm and alone first; heaven tests the careless.
    • Your techniques are in the [bold]Offensive, Defensive, Movement and Utility Arts[/bold] menus on your bar. They open at your mouse; pick one to use it.
    • Put favourites on your bar from Utility → Favourites, then bind them to keys.
    • A dimmed art can't be used right now: not enough qi, or still cooling down.
qi-path-examine-verb = Path of qi
qi-path-examine-verb-message = See your realm and the techniques each realm brings.
qi-path-window-title = Your Path of Qi
qi-path-window-header = [bold]{$realm}[/bold]   [color=#8ad0ff]{$qi} qi[/color]   {$peak ->
        [true] [color=#e8c060]at the peak[/color]
       *[false] [color=#d8b46a]{$insight} / {$required} insight[/color]
    }
qi-path-window-hint = Each realm opens new techniques. Greyed ones are still ahead. Hover a technique to see what it does.
qi-path-window-current = ◀ you are here
