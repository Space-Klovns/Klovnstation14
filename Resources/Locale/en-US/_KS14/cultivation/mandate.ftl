mandate-domain-station = the Station
mandate-title-son = Son of Heaven
mandate-title-lord = Mandated Lord of {$domain}
mandate-title-duke = Duke
mandate-title-marquis = Marquis
mandate-title-count = Count
mandate-title-viscount = Viscount
mandate-title-baron = Baron

mandate-granted = You hold the Mandate of Heaven over {$domain}. Rule justly: if you die, are demoted, or are kept in chains, heaven withdraws it, and a jade seal falls for others to claim. Your imperial powers are all in the Imperial Court button.
mandate-granted-son = You are the Son of Heaven, and rule {$domain}. Rule justly: if you die, are demoted, or are kept in chains, heaven withdraws its Mandate, and the Heirloom Seal of the Realm falls for others to claim. Your imperial powers are all in the Imperial Court button.
mandate-examine = [color=#d4a017][bold]{CAPITALIZE(SUBJECT($ruler))} {CONJUGATE-BASIC($ruler, "bear", "bears")} the Mandate of Heaven as {$title}.[/bold][/color]{$seized ->
    [yes] {" "}Heaven granted it when {SUBJECT($ruler)} seized the seal.
   *[no] {""}
}
mandate-noble-examine = [color=#c0c8d8]{CAPITALIZE(SUBJECT($noble))} {CONJUGATE-BE($noble)} a {$title} of the realm.[/color]
mandate-regicide-examine = [color=#8a0018]A shadow of heaven's wrath clings to {OBJECT($user)}: a regicide.[/color]

mandate-omen-sender = Omen of Heaven
mandate-withdrawn-self = Heaven withdraws its Mandate from you!
mandate-withdrawn-son = The heavens darken and thunder rolls across the station. The Mandate of Heaven has been withdrawn from {$ruler}, who {$reason}. The Heirloom Seal of the Realm lies where they fell. Whoever raises it to heaven shall rule.
mandate-withdrawn = An ill omen: heaven withdraws its Mandate over {$domain} from {$ruler}, who {$reason}. A jade seal lies where they fell.
mandate-reason-died = has died
mandate-reason-chains = has been kept in chains
mandate-reason-demoted = has been stripped of their rank
mandate-office-falls = Your lord has lost the Mandate of Heaven, and your office falls with it.
mandate-regicide = Heaven marks you a regicide! No seal will answer you for some time.
mandate-dynasty-endures = Your dynasty endures. Heaven's favour deepens (dynasty age {$tier}): stronger protection, better fortune, and the Imperial Treasury may be opened again.

mandate-seal-name = jade seal of {$domain}
mandate-seal-name-son = Heirloom Seal of the Realm
mandate-seal-examine = Use it in hand to raise it to heaven and claim the Mandate over {$domain}.
mandate-seal-lineage = The seal remembers those who held it: {$lineage}.
mandate-seal-already = You already bear a Mandate. Heaven does not grant two.
mandate-seal-regicide = The seal is cold in a regicide's hands. Heaven will not answer you.
mandate-seal-raising = {CAPITALIZE(THE($user))} raises {THE($seal)} towards the heavens!
mandate-claimed = {$ruler} has claimed the Mandate of Heaven over {$domain}.
mandate-claimed-son = A new dynasty! {$ruler} has raised the Heirloom Seal of the Realm and claimed the Mandate of Heaven.

mandate-fortune = By heaven's fortune, the blow misses {THE($ruler)} entirely!
mandate-protection = A golden light flares around {THE($ruler)} and the fatal blow is turned aside!
mandate-heaven-angry = The sky darkens over you. Heaven is angry!
mandate-dragon-qi = Imperial dragon qi recoils against you!
mandate-guard-alert = Your lord {$lord} is under attack by {$assailant}, {$where}!

mandate-decree-title = Imperial Decree
mandate-decree-prompt = What is your decree?
mandate-decree-sender-son = Imperial Decree of {$ruler}, Son of Heaven
mandate-decree-words = By imperial decree: {$text}
mandate-rallied = Heaven's chosen has spoken. You are inspired!
mandate-kneel-shout = KNEEL!
mandate-kneel-forced = An overwhelming authority forces you to your knees!
mandate-treasury-empty = The Imperial Treasury is empty. It will fill again as your dynasty endures.
mandate-treasury-opened = {CAPITALIZE(THE($ruler))} opens the Imperial Treasury!
mandate-pardon-words = By the Mandate of Heaven, {$subject} is pardoned!
mandate-jinyiwei-title = Jinyiwei
mandate-jinyiwei-ask = {$ruler} appoints you to the Jinyiwei, the imperial guard. Press OK to accept, Cancel to refuse.
mandate-jinyiwei-appointed = {$subject}, you serve the throne as Jinyiwei!
mandate-jinyiwei-dismissed = {$subject}, you are dismissed from the Jinyiwei.
mandate-jinyiwei-full = You have as many Jinyiwei as your Mandate allows.
mandate-fealty-title = Fealty
mandate-fealty-ask = {$ruler}, who holds the Mandate of Heaven, offers to accept your sect's fealty. Press OK to swear it, Cancel to refuse.
mandate-fealty-not-master = Only a sect master can swear a sect's fealty.
mandate-fealty-sworn = Our Sect Master has sworn the sect's fealty to {$ruler}, who holds the Mandate of Heaven. We cultivate under heaven's favour.
mandate-fealty-accepted = The {$sect} swears fealty to you.
mandate-fealty-ended = Our liege has lost the Mandate of Heaven. Our oath of fealty is ended.
mandate-ennoble-words = By the Mandate of Heaven, {$subject} is raised to {$title}!
mandate-ennoble-stripped = {$subject} is stripped of the title of {$title}!
mandate-ennoble-full = You have raised as many nobles as your dynasty allows.
mandate-punishment-spent = You have called down heaven's punishment as often as your reign allows.
mandate-punishment-shout = HEAVEN, STRIKE DOWN {$condemned}!
mandate-punishment-omen = The Son of Heaven has called down heaven's punishment upon {$condemned}.
jianghu-reason-pardoned = an imperial pardon
mandate-power-decree = Imperial Decree
mandate-power-kneel = Kneel!
mandate-power-treasury = Imperial Treasury
mandate-power-pardon = Imperial Pardon
mandate-power-jinyiwei = Appoint Jinyiwei
mandate-power-fealty = Accept Fealty
mandate-power-ennoble = Ennoble
mandate-power-punishment = Heaven's Punishment
mandate-power-ready-in = {$power} (ready in {$seconds}s)
mandate-power-no-one = {$power}: there is no one near enough.
mandate-power-cooling = That power is ready again in {$seconds} seconds.
mandate-power-tip = {$power}: {$description}
mandate-power-decree-desc = Proclaim a decree; your people nearby are rallied, faster and with their strength restored.
mandate-power-kneel-desc = Command the weak around you to kneel: they're knocked down for a moment.
mandate-power-treasury-desc = Open the treasury: spirit herbs, talismans, gold, a manual, and sometimes a legendary.
mandate-power-pardon-desc = Pardon someone, wiping away their crimes against the jianghu.
mandate-power-jinyiwei-desc = Offer someone a post in your Jinyiwei guard; they cultivate faster and are told when you're attacked.
mandate-power-fealty-desc = Accept a sect master's oath; their whole sect cultivates faster under you.
mandate-power-ennoble-desc = Raise someone to the nobility, from duke down to baron; nobles cultivate faster.
mandate-power-punishment-desc = Call down heaven's punishment: three lightning bolts strike the one you name.
cultivation-proclamation-wrap = [font size=14][bold]{$sender}[/bold][/font]{"\u000A"}[font size=12]{$message}[/font]
mandate-jinyiwei-ask-ascension = In this Ascension, a sworn guard serves the court: you could no longer win by reaching the peak, and your duty would be to stop those who try.
