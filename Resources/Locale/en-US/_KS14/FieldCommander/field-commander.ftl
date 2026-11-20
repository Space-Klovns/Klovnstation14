ks-fieldcommander-title = Field Commander
ks-fieldcommander-description = A Syndicate field commander is besieging your outpost. Fortify, hold the objectives through its assault, and destroy its uplink before it calls down a bombardment.

ks-fieldcommander-unit-name = unknown combatant

## Phases

ks-fieldcommander-phase-landfall = Landfall
ks-fieldcommander-phase-probing = Probing
ks-fieldcommander-phase-pressure = Pressure
ks-fieldcommander-phase-siege = Siege
ks-fieldcommander-phase-final = Final Assault

# The commander only taunts. Everything the defenders can act on comes from Field Operations, as intel.
ks-fieldcommander-phase-landfall-announcement = Nanotrasen. I can see you from here, scrambling behind your little walls like insects. Dig in. It only makes the digging up easier.
ks-fieldcommander-phase-probing-announcement = My hounds are off the leash. Let's hear how loud you scream.
ks-fieldcommander-phase-pressure-announcement = Still breathing? That's a clerical error, and I am about to correct it.
ks-fieldcommander-phase-siege-announcement = I'm done playing with you. Every wall you raised is a coffin, and I am closing the lids.
ks-fieldcommander-phase-final-announcement = This ends now. I will burn your outpost to glass and file your names under acceptable losses.

ks-fieldcommander-intel-phase-landfall = Field Operations here. A Syndicate command signal is confirmed in your sector, with no hostile movement yet. Use the quiet: fortify, cover the servers, and get turrets up.
ks-fieldcommander-intel-phase-probing = Field Operations. Expect small probing teams, mostly pistols. They're testing your lines. Don't chase them out into the fog.
ks-fieldcommander-intel-phase-pressure = Field Operations. Elite squads and breaching cyborgs are cleared to deploy, some of them by drop pod. The borgs go straight for the servers, so kill them first.
ks-fieldcommander-intel-phase-siege = Field Operations. Syndicate artillery is now cleared to fire on your fortifications, so expect strikes on anything you've built. Spread out, and keep the RCDs busy.
ks-fieldcommander-intel-phase-final = Field Operations. This is the final assault. The Syndicate is landing a targeting uplink for an orbital strike on your position. Once we have a fix on it, take it down.

## Waves

ks-fieldcommander-wave-1 = Here they come. Try not to die too quickly, it bores my men.
ks-fieldcommander-wave-2 = Another squad, Nanotrasen. I have more of them than you have bullets.
ks-fieldcommander-wave-3 = Fresh meat for the grinder. Yours, not mine.
ks-fieldcommander-wave-4 = Keep your heads down. My men are aiming for them.
ks-fieldcommander-wave-5 = Every one you kill, I replace. Every one I kill, you don't.
ks-fieldcommander-wave-6 = Hold the line, little soldiers. It's the last thing you'll ever hold.

ks-fieldcommander-intel-wave = Field Operations. Wave { $number }: sensors read { $count } { $count ->
    [one] hostile
   *[other] hostiles
} inbound from the { $direction ->
    [north] north
    [northeast] north-east
    [east] east
    [southeast] south-east
    [south] south
    [southwest] south-west
    [west] west
    [northwest] north-west
   *[other] perimeter
}.

ks-fieldcommander-strike-1 = Smile for the artillery.
ks-fieldcommander-strike-2 = I see your little fort. So does my fire mission.
ks-fieldcommander-strike-3 = Your walls will make fine shrapnel.
ks-fieldcommander-intel-strike = Field Operations. Incoming fire mission, danger close! Get clear of your fortifications!

ks-fieldcommander-asset-driller = My borg is coming for your servers, and nothing you own can stop it.
ks-fieldcommander-intel-driller = Field Operations. A breaching borg is inbound. It goes straight for a server, so focus it down.
ks-fieldcommander-asset-drone = Listen. Hear that buzzing? It's looking for you.
ks-fieldcommander-intel-drone = Field Operations. Suicide drones inbound. They beep and flash red before they blow: get clear or shoot them down.
ks-fieldcommander-drone-armed = { CAPITALIZE(THE($device)) } beeps faster and faster!
ks-fieldcommander-asset-sniper = Keep your heads down. Or don't, and I'll take them off.
ks-fieldcommander-intel-sniper = Field Operations. Enemy sniper deployed. Watch for a red laser: once it stops blinking, it fires. It shoots from far off and backs away if you rush it.
ks-fieldcommander-asset-juggernaut = Hear those footsteps? Run. It won't help.
ks-fieldcommander-intel-juggernaut = Field Operations. A juggernaut is inbound: a machine gun, the bulk to soak up magazine after magazine, and it can't be stunned or tripped. It's slow, so keep your distance and pour it on.
ks-fieldcommander-asset-breacher = Walls? How quaint.
ks-fieldcommander-intel-breacher = Field Operations. Breachers inbound, carrying wall charges. Wherever you hole up, they'll blow a way in close to you rather than use your door. Keep watch on your walls, and listen for the charge going on.
ks-fieldcommander-asset-tank = The final act deserves heavy armour. Say hello.
ks-fieldcommander-intel-tank = Field Operations. Enemy tank inbound. Its cannon fires high-explosive shells, slowly: it beeps while it lines one up, so get out of sight when you hear it. The turret is welded to the hull, and destroying either kills it. Rockets.
ks-fieldcommander-asset-tank-flamer = Cold out there? Let me warm you up.
ks-fieldcommander-intel-tank-flamer = Field Operations. A flamer tank is inbound. It has to get close, and where it hits the ground keeps burning: keep your distance and don't stand in the fire.
ks-fieldcommander-asset-mortar = Bunch up. Please. It saves me shells.
ks-fieldcommander-intel-mortar = Field Operations. Enemy mortar team setting up about forty tiles out. A shell lands every twelve seconds or so, on whoever is bunched together or on a turret: when a marker shows on the ground or you hear one coming, get off it. Hunt the crew down, and the mortar is yours for what shells it has left.

## Objectives and the uplink

ks-fieldcommander-objective-lost = Another server is mine. Can you feel it slipping away?
ks-fieldcommander-intel-objective-lost = Field Operations. We've lost a server. { $remaining ->
    [one] One left. Hold it at all costs.
   *[other] { $remaining } remain.
}
ks-fieldcommander-uplink-deployed = My uplink is down. Pray, if Nanotrasen lets you.
ks-fieldcommander-intel-uplink = Field Operations. We have a fix on the Syndicate uplink: { $distance } metres to the { $direction ->
    [north] north
    [northeast] north-east
    [east] east
    [southeast] south-east
    [south] south
    [southwest] south-west
    [west] west
    [northwest] north-west
   *[other] perimeter
} of the outpost, under guard. The bombardment comes in about { $minutes } minutes. Hack it or blow it before then.
ks-fieldcommander-intel-uplink-warning = Field Operations. The uplink has locked its coordinates. Bombardment in { $minutes } minutes. Destroy it now!
ks-fieldcommander-intel-reinforcements = Field Operations. A resupply pod is inbound to your position. Grab it before the Syndicate does.

## Round end

ks-fieldcommander-roundend-no-phase = none
ks-fieldcommander-roundend-summary = The field commander reached the [color=red]{ $phase }[/color] phase, sending { $waves } waves of { $sent } units in all. { $killed } of them were killed, and { $lost } { $lost ->
    [one] objective was
   *[other] objectives were
} lost.
ks-fieldcommander-roundend-uplink-destroyed = The uplink was taken down { $minutes }m { $seconds }s after it landed.
ks-fieldcommander-roundend-uplink-bombarded = The uplink lasted long enough to call down the bombardment.

## The defenders' briefing

ks-fieldcommander-intel-sender = Nanotrasen Field Operations
ks-fieldcommander-briefing = Outpost, this is Field Operations, priority traffic. Long-range sensors have a Syndicate field command group making landfall around your position. Expect probing attacks within minutes and a full assault to follow. Fortify, hold the servers, and give no ground. Stay inside the perimeter: the fog beyond it will kill you. Every confirmed kill earns you requisition credit: spend it at the requisitions terminal for ordnance and fire support. Field Operations out.

## Points

ks-fieldcommander-points-total = Requisition: { $points }
ks-fieldcommander-points-gain = +{ $amount }  { $reason }
ks-fieldcommander-points-loss = −{ $amount }  { $reason }
ks-fieldcommander-points-kill = { $name }
ks-fieldcommander-points-assist = Assist: { $name }
ks-fieldcommander-points-teamkill = Friendly kill: { $name }
ks-fieldcommander-points-teamkill-assist = Friendly kill assist: { $name }
ks-fieldcommander-points-uplink = Uplink taken out
ks-fieldcommander-points-admin = Adjusted by Field Operations

## Requisitions

ks-fieldcommander-shop-maptext = REQUISITIONS
ks-supplypod-passenger-name = Nanotrasen paramilitary
ks-fieldcommander-wave-status = Wave { $wave } · next in { $time }
ks-fieldcommander-wave-status-first = First wave in { $time }
ks-fieldcommander-wave-status-none = Wave { $wave } · no more scheduled
ks-fieldcommander-wave-status-idle = Waves have not begun

ks-fieldcommander-roundend-waves = The defence held out until wave [color=yellow]{ $wave }[/color].
ks-fieldcommander-roundend-waves-endless = The defence held out until wave [color=yellow]{ $wave }[/color], { $endless } of them in endless mode.

ks-fieldcommander-endless-vote-title = The uplink is down. Hold the line in endless mode?
ks-fieldcommander-endless-vote-initiator = Field Operations
ks-fieldcommander-endless-vote-yes = Hold the line
ks-fieldcommander-endless-vote-no = Pull out
ks-fieldcommander-intel-endless-vote = Field Operations. The uplink is down, the field is yours. Vote within { $seconds } seconds: pull out, or dig in and see how long you last.
ks-fieldcommander-endless = You think that was all of it? Everything I have left is coming for you, and it will keep coming.
ks-fieldcommander-intel-endless = Field Operations. You're holding the line. Every wave will be bigger and sooner than the last. Hold as long as you can.

## Doctrines: how the commander spends a wave's budget

ks-fieldcommander-doctrine-standard = Standard
ks-fieldcommander-doctrine-swarm = Swarm
ks-fieldcommander-doctrine-spearhead = Spearhead
ks-fieldcommander-doctrine-airborne = Airborne
ks-fieldcommander-doctrine-hoard = Hoard
ks-fieldcommander-intel-doctrine-swarm = Field Operations. That's a lot of light infantry. Keep the fire up and don't let them flank.
ks-fieldcommander-intel-doctrine-spearhead = Field Operations. Their best are leading this one. Hold fast and focus fire.
ks-fieldcommander-intel-doctrine-airborne = Field Operations. Drop pods on scope. Watch the sky inside your lines.
ks-fieldcommander-intel-doctrine-hoard = Field Operations. That wave is light. They're holding something back; use the time.

ks-fieldcommander-wave-status-paused = { $wave ->
    [0] Waves paused
   *[other] Wave { $wave } · paused
}

ks-fieldcommander-points-revive = Revived: { $name }
ks-fieldcommander-points-heal = Healed: { $name }

## The ending: the failed commander, mobilised and sent in himself.

ks-fieldcommander-mobik-sender = Syndicate High Command
ks-fieldcommander-mobik-announcement = Syndicate High Command to all frequencies. The Field Commander has failed to take the Nanotrasen stronghold. He is relieved of command and mobilised, effective immediately: one rifle, no armour, no rank. Congratulations on your promotion to mobik, soldier. The front is that way.
ks-fieldcommander-intel-mobik = Field Operations. One pod inbound in about { $seconds } seconds, carrying a single conscript: their own former commander. Put him out of his misery.
ks-fieldcommander-intel-mobik-killed = Field Operations. The mobik is down. That was the last of them.
ks-fieldcommander-mobik-name = former field commander
ks-fieldcommander-points-mobik = Mobik killed
ks-fieldcommander-roundend-mobik-killed = Relieved of command and sent in as a mobik, the field commander was killed too.
