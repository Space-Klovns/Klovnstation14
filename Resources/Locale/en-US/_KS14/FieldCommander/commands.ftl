cmd-ks_fieldcommander-no-rule = No field commander is running.

cmd-ks_fcphase-desc = Moves every running field commander to a phase, with its announcement, pod and uplink.
cmd-ks_fcphase-help = ks_fcphase <index or name>
cmd-ks_fcphase-hint = <phase>
cmd-ks_fcphase-unknown = There is no phase "{ $phase }".
cmd-ks_fcphase-set = Field commander moved to { $phase } (phase { $index }).

cmd-ks_fcwave-desc = Sends the current phase's next field commander wave now.
cmd-ks_fcwave-help = ks_fcwave
cmd-ks_fcwave-sent = Wave sent. { $waves } so far.

cmd-ks_fcuplink-desc = Lands the field commander's uplink now.
cmd-ks_fcuplink-help = ks_fcuplink
cmd-ks_fcuplink-already = The uplink has already landed.
cmd-ks_fcuplink-deployed = Uplink landed.
cmd-ks_fcuplink-failed = Found nowhere to land the uplink.

cmd-ks_fcpoints-desc = Gives a field commander defender points, takes them away, or with "set" sets them.
cmd-ks_fcpoints-help = ks_fcpoints <player> <amount> [set]
cmd-ks_fcpoints-hint-player = <player>
cmd-ks_fcpoints-hint-amount = <amount>
cmd-ks_fcpoints-hint-set = [set]
cmd-ks_fcpoints-done = { $player } now has { $points } points.

cmd-ks_fcstatus-desc = Shows where every running field commander is: phase, timers, units and points.
cmd-ks_fcstatus-help = ks_fcstatus
cmd-ks_fcstatus-summary = { $rule }: phase { $index } ({ $phase }), { $elapsed }s in. Next phase in { $nextPhase }s, next wave in { $nextWave }s. Adaptive ×{ $adaptive }. Units alive { $alive }/{ $cap }. Defenders alive { $defenders }, dead { $dead }. Waves sent { $waves }. Uplink: { $uplink }.
cmd-ks_fcstatus-uplink-none = not landed
cmd-ks_fcstatus-uplink-destroyed = taken out
cmd-ks_fcstatus-uplink-active = bombardment in { $seconds }s
cmd-ks_fcstatus-points = { $player }: { $points } points
