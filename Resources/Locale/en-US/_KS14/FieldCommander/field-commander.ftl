ks-fieldcommander-title = Field Commander
ks-fieldcommander-description = A Syndicate field commander is besieging your outpost. Fortify, hold the objectives through its assault, and destroy its uplink before it calls down a bombardment.

ks-fieldcommander-unit-name = unknown combatant

## Phases

ks-fieldcommander-phase-landfall = Landfall
ks-fieldcommander-phase-probing = Probing
ks-fieldcommander-phase-pressure = Pressure
ks-fieldcommander-phase-siege = Siege
ks-fieldcommander-phase-final = Final Assault

ks-fieldcommander-phase-landfall-announcement = Attention, Nanotrasen outpost. This is the Syndicate field commander. Your position is surrounded and your servers are forfeit. Use the minutes I am giving you well. They will be your last quiet ones.
ks-fieldcommander-phase-probing-announcement = Scouting parties are moving in. Let us see what your defences are made of.
ks-fieldcommander-phase-pressure-announcement = You are still standing. Commendable. Elite squads and breaching borgs are being cleared for deployment.
ks-fieldcommander-phase-siege-announcement = Enough. Artillery is cleared to fire on your fortifications. Every wall you build is a target.
ks-fieldcommander-phase-final-announcement = All units, final assault. My uplink is coming down to paint your outpost for orbital bombardment. No one leaves.

## Waves

ks-fieldcommander-wave-1 = { $count ->
    [one] One operative inbound.
   *[other] { $count } operatives inbound.
} Make it quick.
ks-fieldcommander-wave-2 = Fresh squad deploying. Hunt them down.
ks-fieldcommander-wave-3 = Reinforcements away. Their positions are marked on your feed.
ks-fieldcommander-wave-4 = Next wave moving out. Take no prisoners.
ks-fieldcommander-wave-5 = Another squad is on approach. Keep up the pressure.
ks-fieldcommander-wave-6 = { $count } more for your graveyard, Nanotrasen.

ks-fieldcommander-strike-1 = Fire mission confirmed. Danger close.
ks-fieldcommander-strike-2 = Ordnance away. I'd step back from your walls if I were you.
ks-fieldcommander-strike-3 = Target painted. Missile inbound.

ks-fieldcommander-asset-driller = A breaching borg is inbound. It will not stop until your servers are mine.

## Objectives and the uplink

ks-fieldcommander-objective-lost = One of your servers is mine. { $remaining ->
    [one] Only one left.
   *[other] { $remaining } left.
}
ks-fieldcommander-uplink-deployed = My uplink is down, { $distance } metres { $direction ->
    [north] north
    [northeast] north-east
    [east] east
    [southeast] south-east
    [south] south
    [southwest] south-west
    [west] west
    [northwest] north-west
   *[other] out
} of your outpost, and it is guarded. In { $minutes } minutes it will call the bombardment down on your heads. Come and stop it, if you can.
ks-fieldcommander-uplink-warning = Bombardment coordinates are locked. { $minutes } minutes.

## Round end

ks-fieldcommander-roundend-no-phase = none
ks-fieldcommander-roundend-summary = The field commander reached the [color=red]{ $phase }[/color] phase, sending { $waves } waves of { $sent } units in all. { $killed } of them were killed, and { $lost } { $lost ->
    [one] objective was
   *[other] objectives were
} lost.
ks-fieldcommander-roundend-uplink-destroyed = The uplink was taken down { $minutes }m { $seconds }s after it landed.
ks-fieldcommander-roundend-uplink-bombarded = The uplink lasted long enough to call down the bombardment.

## The defenders' briefing

ks-fieldcommander-intro-sender = Nanotrasen Field Operations
ks-fieldcommander-intro = Outpost, this is Field Operations, priority traffic. Long-range sensors have a Syndicate field command group making landfall around your position. Expect probing attacks within minutes and a full assault to follow. Fortify, hold the servers, and give no ground. Every confirmed kill earns you requisition credit: spend it at the requisitions terminal for ordnance and fire support. Field Operations out.

## Points

ks-fieldcommander-points-total = Requisition: { $points }
ks-fieldcommander-points-gain = +{ $amount }  { $reason }
ks-fieldcommander-points-loss = −{ $amount }  { $reason }
ks-fieldcommander-points-kill = { $name }
ks-fieldcommander-points-teamkill = Friendly kill: { $name }
ks-fieldcommander-points-uplink = Uplink taken out
ks-fieldcommander-points-admin = Adjusted by Field Operations
