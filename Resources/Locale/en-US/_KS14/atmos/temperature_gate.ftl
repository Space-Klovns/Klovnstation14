ks-temperature-gate-examined = It is [color={ $enabled ->
    [true] green]on
   *[false] red]off
}[/color], and lets gas through while its input is { $inverted ->
    [true] hotter
   *[false] colder
} than [color=lightblue]{ $temperature } K[/color].

ks-temperature-gate-verb-maximize = Set target to maximum
ks-temperature-gate-verb-on = Turn on
ks-temperature-gate-verb-off = Turn off

ks-temperature-gate-inverted = The gate now opens above its target.
ks-temperature-gate-restored = The gate now opens below its target.
ks-temperature-gate-unanchor-on = Turn it off first!

ks-temperature-gate-ui-title = Temperature Gate
ks-temperature-gate-ui-set = Set
ks-temperature-gate-ui-max = Max
ks-temperature-gate-ui-target = Target temperature (K):
ks-temperature-gate-ui-mode = Opens when the input is { $inverted ->
    [true] hotter
   *[false] colder
} than the target.
