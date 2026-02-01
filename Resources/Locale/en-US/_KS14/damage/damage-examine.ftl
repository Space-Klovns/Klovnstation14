armor-penetration-percentile = It penetrates [color=yellow]{ $type }[/color] armor { $arg ->
    [1] [color=red]{$abs}% better[/color]
    *[other] [color=blue]{$abs}% worse[/color]
} than an average damage source.

armor-penetration-flat = It { $arg ->
    [1] ignores [color=red]{$abs}[/color] more points of [color=yellow]{ $type }[/color] armor
    *[other] deals [color=blue]{$abs}[/color] less [color=yellow]{ $type }[/color] damage to armored targets
} than an average damage source.
