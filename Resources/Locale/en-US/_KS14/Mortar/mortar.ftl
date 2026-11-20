ks-mortar-examine-rounds = { $rounds ->
    [0] It has no shells left.
    [one] It has [color=yellow]1[/color] shell left.
   *[other] It has [color=yellow]{ $rounds }[/color] shells left.
}
ks-mortar-window-title = Mortar
ks-mortar-window-rounds = { $rounds ->
    [one] 1 shell left
   *[other] { $rounds } shells left
}
ks-mortar-window-range = Range { $min } to { $max } tiles
ks-mortar-window-bearing = Bearing (°)
ks-mortar-window-bearing-placeholder = 0 is north
ks-mortar-window-distance = Distance (tiles)
ks-mortar-window-fire = Fire
ks-mortar-window-ready = Ready
ks-mortar-window-reloading = Reloading: { $seconds } s
ks-mortar-window-empty = Out of shells
ks-mortar-window-invalid = Enter a number for both
