cmd-copygrid-desc = Copies a grid (e.g. a ship) and pastes a duplicate onto the same map, offset next to the original. Made for iterating on grid designs on a dev map.
cmd-copygrid-help =
    Usage: copygrid [gridUid | here] [abs] [X Y] [rotationDeg]
      copygrid                              - copy the grid you're standing on, placed just east of it.
      copygrid <gridUid | here>             - copy that grid (or the one you're standing on, with 'here').
      copygrid <gridUid | here> X Y         - copy at a custom offset (tiles, added to the original's position).
      copygrid <gridUid | here> X Y DEG     - also rotate the copy DEG degrees about its own origin.
      copygrid <gridUid | here> abs X Y     - paste with the copy's origin at ABSOLUTE map coordinates X, Y.
      copygrid <gridUid | here> abs X Y DEG - absolute coordinates plus rotation.
    Note: minded mobs/players aboard are NOT copied. Cross-grid links (e.g. docking) can't survive a single-grid
    copy and will make the copy fail. Serializing also runs the map-wide pre-save hooks on the source map.

cmd-copygrid-invalid-args = Expected 0, 1, 3, or 4 arguments ('abs' plus X Y [DEG] in absolute mode; a position needs both X and Y).
cmd-copygrid-no-player = No grid given and you have no entity to infer one from. Pass a grid uid (or stand on one and use 'here').
cmd-copygrid-not-on-grid = You are not standing on a grid. Move onto the grid you want to copy, or pass its uid.
cmd-copygrid-bad-uid = '{$value}' is not a valid entity id.
cmd-copygrid-not-a-grid = {$uid} does not exist or is not a grid.
cmd-copygrid-is-map = That entity is a map, not a plain grid. Use savemap/loadmap for whole maps.
cmd-copygrid-no-map = The grid is not on a valid map.
cmd-copygrid-bad-float = '{$value}' is not a valid number.
cmd-copygrid-save-failed = Failed to serialize the grid. Check the server log for details.
cmd-copygrid-load-failed = Failed to load the copied grid. Check the server log for details.
cmd-copygrid-load-threw = Loading the copy failed (the source grid may have cross-grid links such as docking): {$reason}
cmd-copygrid-success = Copied {$from} -> {$to} on map {$map} at world position {$x}, {$y}.

cmd-copygrid-grid-completion = [gridUid | here] (blank = grid under you)
cmd-copygrid-here-hint = the grid you're standing on
cmd-copygrid-abs-hint = next X Y are absolute map coordinates, not an offset
cmd-copygrid-offsetx-completion = [offsetX | abs] (offsetY also required)
cmd-copygrid-offsety-completion = [offsetY]
cmd-copygrid-absx-completion = [absoluteX] (absoluteY also required)
cmd-copygrid-absy-completion = [absoluteY]
cmd-copygrid-rot-completion = [rotationDeg]
