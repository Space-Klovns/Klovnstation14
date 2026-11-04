ks-temperature-gate-examined-status = It is [color={ $enabled ->
    [true] green]on
   *[false] red]off
}[/color].
ks-temperature-gate-examined-tolerance = It lets gas through while its input is { $inverted ->
    [true] more than
   *[false] within
} [color=lightblue]{ $tolerance } K[/color] { $inverted ->
    [true] away from
   *[false] of
} [color=lightblue]{ $temperature } K[/color].
ks-temperature-gate-examined-range = It lets gas through while its input is { $inverted ->
    [true] outside
   *[false] between
} [color=lightblue]{ $minimum } K[/color] and [color=lightblue]{ $maximum } K[/color].

ks-temperature-gate-verb-maximize = Set target to maximum
ks-temperature-gate-verb-on = Turn on
ks-temperature-gate-verb-off = Turn off

ks-temperature-gate-inverted = The gate now opens outside its limits.
ks-temperature-gate-restored = The gate now opens inside its limits.
ks-temperature-gate-unanchor-on = Turn it off first!

ks-temperature-gate-ui-title = Temperature Gate
ks-temperature-gate-ui-set = Set
ks-temperature-gate-ui-max = Max
ks-temperature-gate-ui-target = Target (K):
ks-temperature-gate-ui-tolerance = Tolerance (K):
ks-temperature-gate-ui-minimum = Minimum (K):
ks-temperature-gate-ui-maximum = Maximum (K):
ks-temperature-gate-ui-mode-label = Mode:
ks-temperature-gate-ui-mode-tolerance = Tolerance
ks-temperature-gate-ui-mode-range = Min-max
ks-temperature-gate-ui-open-when = Open while the input is { $inverted ->
    [true] outside
   *[false] inside
} these limits.
