ks-fieldcommander-title = Field Commander
ks-fieldcommander-description = A Syndicate field commander is besieging your outpost. Fortify, hold the objectives through its assault, and destroy its uplink before it calls down a bombardment.

ks-fieldcommander-unit-name = unknown combatant

## Phases

ks-fieldcommander-phase-landfall = Landfall
ks-fieldcommander-phase-probing = Probing
ks-fieldcommander-phase-pressure = Pressure
ks-fieldcommander-phase-siege = Siege
ks-fieldcommander-phase-final = Final Assault

# The commander only taunts. Everything the defenders can act on comes from Field Operations, as intel, but for the
#     commander's own warning of a strike (ks-fieldcommander-strike-warning).
ks-fieldcommander-phase-landfall-announcement = Nanotrasen outpost, this is your new neighbour. We've landed, we're unpacking, and we'll be over shortly, so don't go anywhere.
ks-fieldcommander-phase-probing-announcement = I'm sending a few men over to have a look at you, so do be polite.
ks-fieldcommander-phase-pressure-announcement = Still there, I see.
ks-fieldcommander-phase-siege-announcement = I've been patient.
ks-fieldcommander-phase-final-announcement = That's enough.

ks-fieldcommander-intel-phase-landfall = A Syndicate command signal is confirmed in your sector with no movement yet, so fortify while you can.
ks-fieldcommander-intel-phase-probing = Small, lightly armed teams are heading your way, and they are not worth following into the fog.
ks-fieldcommander-intel-phase-pressure = Elite squads and breaching cyborgs are deploying, some by drop pod, and the cyborgs want the servers.
ks-fieldcommander-intel-phase-siege = Syndicate artillery is firing on fortifications, so spread out.
ks-fieldcommander-intel-phase-final = This is the final assault: a targeting uplink is landing for an orbital strike, and we'll send its location once we have it.

## Waves

ks-fieldcommander-wave-1 = They're on their way.
ks-fieldcommander-wave-2 = More.
ks-fieldcommander-wave-3 = I have plenty of these.
ks-fieldcommander-wave-4 = Again.
ks-fieldcommander-wave-5 = I can keep this up longer than you can.
ks-fieldcommander-wave-6 = You must be getting tired.

ks-fieldcommander-intel-wave = Wave { $number }: { $count } { $count ->
    [one] hostile
   *[other] hostiles
} coming from the { $direction ->
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

ks-fieldcommander-strike-1 = Hold still.
ks-fieldcommander-strike-2 = Nice fort.
ks-fieldcommander-strike-3 = Thanks for building it all in one place.
# The one warning the commander gives itself: the missiles are its own, so the word of them comes signed by it.
ks-fieldcommander-strike-warning = I'd stand somewhere else.

ks-fieldcommander-asset-driller = I'm told you're fond of those servers.
ks-fieldcommander-intel-driller = Breaching cyborg inbound, heading for a server.
ks-fieldcommander-asset-drone = Hear that?
ks-fieldcommander-intel-drone = Suicide drones are inbound, and they beep before they blow.
ks-fieldcommander-drone-armed = { CAPITALIZE(THE($device)) } beeps faster and faster!
ks-fieldcommander-asset-sniper = Keep your heads down.
ks-fieldcommander-intel-sniper = There's a sniper outside your lines, whose red laser stops blinking just before the shot.
ks-fieldcommander-asset-juggernaut = You'll hear him first.
ks-fieldcommander-intel-juggernaut = A juggernaut is inbound: slow, heavily armoured, and not to be stunned or knocked down.
ks-fieldcommander-asset-breacher = Doors are for guests.
ks-fieldcommander-intel-breacher = Breachers are inbound with wall charges, so watch your walls.
ks-fieldcommander-asset-tank = I saved something for last.
ks-fieldcommander-intel-tank = A tank is inbound with high explosive, beeping while it aims, and it dies with either its turret or its hull, preferably to a rocket.
ks-fieldcommander-asset-tank-flamer = Cold out there?
ks-fieldcommander-intel-tank-flamer = A flamer tank is inbound, so stay out of its reach and out of the fire it leaves.
ks-fieldcommander-asset-mortar = Do keep standing together.
ks-fieldcommander-intel-mortar = A mortar team is setting up outside your lines, so watch the ground, and know that the mortar is yours if you take it.

## Objectives and the uplink

ks-fieldcommander-objective-lost = Thank you.
ks-fieldcommander-intel-objective-lost = We've lost a server, { $remaining ->
    [one] and only one is left.
   *[other] which leaves { $remaining }.
}
ks-fieldcommander-uplink-deployed = You're on a timer now.
ks-fieldcommander-intel-uplink = We've found the uplink { $distance } metres to the { $direction ->
    [north] north
    [northeast] north-east
    [east] east
    [southeast] south-east
    [south] south
    [southwest] south-west
    [west] west
    [northwest] north-west
   *[other] perimeter
} of the outpost and under guard, with about { $minutes } minutes to hack or destroy it before the bombardment.
ks-fieldcommander-intel-uplink-warning = The uplink has locked its coordinates, and the bombardment is { $minutes } minutes out.
ks-fieldcommander-intel-reinforcements = Resupply pod inbound to your position.

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
ks-fieldcommander-briefing = Outpost, this is Field Operations. A Syndicate field command group is landing around your position, so fortify, hold the servers, and stay inside the perimeter, because the fog will kill you. Confirmed kills earn requisition credit to spend at the requisitions terminal. Field Operations out.

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
ks-fieldcommander-intel-endless-vote = The uplink is destroyed, and you have { $seconds } seconds to vote on whether to pull out or stay.
ks-fieldcommander-endless = I'm not leaving, and neither are you.
ks-fieldcommander-intel-endless = You're staying, then, and each wave will be bigger and sooner than the last.

## Doctrines: how the commander spends a wave's budget

ks-fieldcommander-doctrine-standard = Standard
ks-fieldcommander-doctrine-swarm = Swarm
ks-fieldcommander-doctrine-spearhead = Spearhead
ks-fieldcommander-doctrine-airborne = Airborne
ks-fieldcommander-doctrine-hoard = Hoard
ks-fieldcommander-intel-doctrine-swarm = That's a lot of light infantry, so watch your flanks.
ks-fieldcommander-intel-doctrine-spearhead = Their best are leading this one.
ks-fieldcommander-intel-doctrine-airborne = Drop pods are inbound, and they tend to land inside your lines.
ks-fieldcommander-intel-doctrine-hoard = That was a small wave, which means they're saving up.

ks-fieldcommander-wave-status-paused = { $wave ->
    [0] Waves paused
   *[other] Wave { $wave } · paused
}

ks-fieldcommander-points-revive = Revived: { $name }
ks-fieldcommander-points-heal = Healed: { $name }

## The ending: the failed commander, mobilised and sent in himself.

ks-fieldcommander-mobik-sender = Syndicate High Command
ks-fieldcommander-mobik-announcement = The Field Commander has failed to take the Nanotrasen outpost, and is hereby relieved of command and mobilised with one rifle, no armour and no rank. Congratulations on your promotion to mobik, soldier. The front is that way.
ks-fieldcommander-intel-mobik = One pod is inbound, about { $seconds } seconds out, carrying a single conscript who used to be their commander.
ks-fieldcommander-intel-mobik-killed = The mobik is dead, and he was the last of them.
ks-fieldcommander-mobik-name = former field commander
ks-fieldcommander-points-mobik = Mobik killed
ks-fieldcommander-roundend-mobik-killed = Relieved of command and sent in as a mobik, the field commander was killed too.
