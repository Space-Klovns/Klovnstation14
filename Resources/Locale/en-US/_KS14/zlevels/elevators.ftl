zlevel-elevator-ui-title = elevator controller
zlevel-elevator-ui-status-label = Status:

# The whole status line is assembled here rather than in C#, so that a translation is free to word a
#     moving lift however its language wants instead of being handed a pre-chosen phrase.
zlevel-elevator-ui-status = { $moving ->
    [true] { $direction ->
        [Up] Ascending
        [Down] Descending
       *[Idle] Moving
    }
   *[false] Stopped
}

# One string per floor button, for the same reason: the markers a floor carries are not mutually
#     exclusive, and chaining a wrapper string per marker in C# hard-codes their order and spacing.
zlevel-elevator-ui-floor = Floor { $number }{ $current ->
    [true] { " " }(here)
   *[false] { "" }
}{ $called ->
    [true] { " " }*
   *[false] { "" }
}{ $obstructed ->
    [true] { " " }[obstructed]
   *[false] { "" }
}

zlevel-elevator-ui-no-shaft = No elevator on this shaft.

zlevel-elevator-call-called = The elevator has been called.
zlevel-elevator-call-already-here = The elevator is already here.
zlevel-elevator-call-no-shaft = Nothing answers.

signal-port-name-ks-elevator-stopped = Elevator Stopped
signal-port-description-ks-elevator-stopped = This port is invoked when the elevator comes to a stop at a floor.
signal-port-name-ks-elevator-moving = Elevator Moving
signal-port-description-ks-elevator-moving = This port is invoked just before the elevator leaves a floor, so that doors wired to it have the journey to finish closing.
