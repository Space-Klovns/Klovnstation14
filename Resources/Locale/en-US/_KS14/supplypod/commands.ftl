cmd-spawnunlaunchedpod-desc = Spawns a supply pod sitting on the ground, unlocked and awaiting launch, instead of dropping it in from orbit.
cmd-spawnunlaunchedpod-help = spawnunlaunchedpod <pod prototype> [location uid]
cmd-spawnunlaunchedpod-invalid-args = Expected 1 or 2 arguments.
cmd-spawnunlaunchedpod-invalid-proto = { $proto } is not a supply pod prototype.
cmd-spawnunlaunchedpod-invalid-uid = Invalid entity { $uid }.
cmd-spawnunlaunchedpod-no-location = No location given, and you have no entity to spawn it on.
cmd-spawnunlaunchedpod-spawned = Spawned unlaunched supply pod { $uid }.
cmd-spawnunlaunchedpod-proto-completion = <pod prototype>
cmd-spawnunlaunchedpod-uid-completion = <location uid>

cmd-launchsupplypod-desc = Launches an unlaunched supply pod. It rises out of sight, then drops onto the dropoff, or back onto the tile it left from.
cmd-launchsupplypod-help = launchsupplypod <pod uid> [dropoff uid]
cmd-launchsupplypod-invalid-args = Expected 1 or 2 arguments.
cmd-launchsupplypod-invalid-uid = Invalid entity { $uid }.
cmd-launchsupplypod-not-launchable = { $uid } is not a supply pod waiting to be launched.
cmd-launchsupplypod-launched = Launched supply pod { $uid }.
cmd-launchsupplypod-pod-completion = <pod uid>
cmd-launchsupplypod-dropoff-completion = <dropoff uid>
