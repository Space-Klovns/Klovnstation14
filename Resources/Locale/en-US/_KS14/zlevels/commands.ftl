cmd-zlevel_add-desc = Sets a z-level to be above another z-level. Will make either map a z-level if they arent already. Second param is the z-level that will be above.
cmd-zlevel_add-help = zlevel_add <target uid> <added uid>

cmd-zlevel_add-invalid-args = Expected exactly 2 arguments.
cmd-zlevel_add-invalid-uid = Invalid entity {$pretty}.

cmd-zlevel_add-completion = <Map UID>

cmd-zlevel_elevator-desc = Makes an existing grid into an elevator that can move up and down its z-level stack. Optional second param sets a shaft id, only needed when one stack holds more than one elevator.
cmd-zlevel_elevator-help = zlevel_elevator <grid uid> [shaft id]

cmd-zlevel_elevator-invalid-args = Expected 1 or 2 arguments.
cmd-zlevel_elevator-not-a-grid = {$uid} is not a grid.
cmd-zlevel_elevator-no-zlevel = Made it an elevator, but its map is not a z-level yet, so it has no floors. Link the maps with zlevel_add first.
cmd-zlevel_elevator-success = Made it an elevator. It serves {$floors} floor(s) and is currently on floor {$floor}.

cmd-zlevel_elevator-completion-grid = <Grid UID>
cmd-zlevel_elevator-completion-shaft = <shaft id>

cmd-zlevel_elevator_controller-desc = Spawns an elevator controller console where you are standing. Optional param binds it to a shaft id rather than the nearest unlabelled elevator.
cmd-zlevel_elevator_controller-help = zlevel_elevator_controller [shaft id]

cmd-zlevel_elevator_controller-invalid-args = Expected at most 1 argument.
cmd-zlevel_elevator_controller-no-body = You need a body to place a controller next to.
cmd-zlevel_elevator_controller-success = Placed a controller. It resolves to elevator {$elevator}.
cmd-zlevel_elevator_controller-unbound = Placed a controller, but it resolves to no elevator. Check the shaft id, or that the elevator is in the same z-level stack.
