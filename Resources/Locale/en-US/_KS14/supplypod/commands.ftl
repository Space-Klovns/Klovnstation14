cmd-ks_spawnunlaunchedpod-desc = Spawns a supply pod sitting on the ground, unlocked and awaiting launch, instead of dropping it in from orbit.
cmd-ks_spawnunlaunchedpod-help = ks_spawnunlaunchedpod <pod prototype> [location uid]
cmd-ks_spawnunlaunchedpod-invalid-args = Expected 1 or 2 arguments.
cmd-ks_spawnunlaunchedpod-invalid-proto = { $proto } is not a supply pod prototype.
cmd-ks_spawnunlaunchedpod-invalid-uid = Invalid entity { $uid }.
cmd-ks_spawnunlaunchedpod-no-location = No location given, and you have no entity to spawn it on.
cmd-ks_spawnunlaunchedpod-spawned = Spawned unlaunched supply pod { $uid }.
cmd-ks_spawnunlaunchedpod-proto-completion = <pod prototype>
cmd-ks_spawnunlaunchedpod-uid-completion = <location uid>

cmd-ks_launchsupplypod-desc = Launches an unlaunched supply pod. It rises out of sight, then drops onto the dropoff, or back onto the tile it left from.
cmd-ks_launchsupplypod-help = ks_launchsupplypod <pod uid> [dropoff uid]
cmd-ks_launchsupplypod-invalid-args = Expected 1 or 2 arguments.
cmd-ks_launchsupplypod-invalid-uid = Invalid entity { $uid }.
cmd-ks_launchsupplypod-not-launchable = { $uid } is not a supply pod waiting to be launched.
cmd-ks_launchsupplypod-launched = Launched supply pod { $uid }.
cmd-ks_launchsupplypod-pod-completion = <pod uid>
cmd-ks_launchsupplypod-dropoff-completion = <dropoff uid>
