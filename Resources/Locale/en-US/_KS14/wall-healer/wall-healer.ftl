## Examine

ks-wall-healer-examine-supplies = It has [color=lightblue]{ $count }[/color] { $item } in stock. [color=gray]Alt-click[/color] to take one.
ks-wall-healer-examine-patient = { CAPITALIZE(THE($patient)) } currently { CONJUGATE-HAVE($patient) } { POSS-ADJ($patient) } hand in it.
ks-wall-healer-examine-allowance = Its first aid allowance has [color=lightblue]{ NATURALFIXED($remaining, 1) }[/color] of { NATURALFIXED($limit, 1) } left.

## Putting hands in and taking them out

ks-wall-healer-incompatible = Incompatible!
ks-wall-healer-too-far = Bring { OBJECT($patient) } closer!
ks-wall-healer-busy = { $self ->
    [true] You try to put your hand in { THE($healer) }, but { THE($current) } { CONJUGATE-BE($current) } already using it.
   *[false] You try to put { THE($patient) }'s hand in { THE($healer) }, but { THE($current) } { CONJUGATE-BE($current) } already using it.
}

ks-wall-healer-insert-self = { $powered ->
    [true] You put your hand in { THE($healer) }, and immediately some kind of sensor scans your arm.
   *[false] You put your hand in { THE($healer) }, but it doesn't respond.
}
ks-wall-healer-insert-self-others = { $powered ->
    [true] { CAPITALIZE(THE($patient)) } puts { POSS-ADJ($patient) } hand in { THE($healer) }, and immediately some kind of sensor scans { POSS-ADJ($patient) } arm.
   *[false] { CAPITALIZE(THE($patient)) } puts { POSS-ADJ($patient) } hand in { THE($healer) }, but it doesn't respond. Seems to be out of order.
}
ks-wall-healer-insert-other = { $powered ->
    [true] You put { THE($patient) }'s hand in { THE($healer) }, and immediately some kind of sensor scans { POSS-ADJ($patient) } arm.
   *[false] You put { THE($patient) }'s hand in { THE($healer) }, but it doesn't respond. Seems to be out of order.
}
ks-wall-healer-insert-other-others = { $powered ->
    [true] { CAPITALIZE(THE($user)) } puts { THE($patient) }'s hand in { THE($healer) }, and immediately some kind of sensor scans { POSS-ADJ($patient) } arm.
   *[false] { CAPITALIZE(THE($user)) } puts { THE($patient) }'s hand in { THE($healer) }, but it doesn't respond. Seems to be out of order.
}

ks-wall-healer-remove-self = You remove your hand from { THE($healer) }.
ks-wall-healer-remove-self-others = { CAPITALIZE(THE($patient)) } removes { POSS-ADJ($patient) } hand from { THE($healer) }.
ks-wall-healer-remove-other = You pull { THE($patient) }'s hand out of { THE($healer) }.

## Treatment

ks-wall-healer-treated = Several instruments and syringes work on your hand. You feel a bit better.
ks-wall-healer-emagged-treatment = You feel a sharp pain as the machine malfunctions, stabbing you with several instruments and needles!
ks-wall-healer-not-organic = Nothing happens. Seems { THE($healer) } doesn't recognise non-organic beings.
ks-wall-healer-blocked = Nothing happens. Seems { THE($healer) } can't find any exposed flesh to work on.
ks-wall-healer-no-id = Nothing happens. { CAPITALIZE(THE($healer)) } won't treat you without an ID card.
ks-wall-healer-allowance-spent = Nothing happens. The first aid allowance on { THE($card) } is used up for now.
ks-wall-healer-needs-recharge = Nothing happens. Seems like { THE($healer) } needs to recharge.
ks-wall-healer-healthy = Nothing happens. Seems like you're in good enough shape.

## Supplies

ks-wall-healer-verb-take-supply = Take { $item }
ks-wall-healer-supply-unpowered = You try to retrieve some { $item }, but { THE($healer) } doesn't respond.
ks-wall-healer-supply-empty = You try to retrieve some { $item }, but { THE($healer) } seems to be out of stock.
ks-wall-healer-supply-jammed = You try to retrieve some { $item }, but it gets all jammed up in the access port.
ks-wall-healer-supply-taken = You retrieve { THE($item) } from { THE($healer) }.
ks-wall-healer-supply-taken-others = { CAPITALIZE(THE($user)) } retrieves { THE($item) } from { THE($healer) }.
ks-wall-healer-restock = You restock { THE($healer) } with { THE($item) }.
ks-wall-healer-restock-others = { CAPITALIZE(THE($user)) } restocks { THE($healer) } with { THE($item) }.
ks-wall-healer-restock-failed = You try to restock { THE($healer) } with { THE($item) }, but it seems stuck to your hand.

## Construction

construction-graph-tag-ks-wall-healer-electronics = first aid station electronics
construction-graph-tag-ks-wall-healer-emergency-electronics = emergency first aid station electronics
construction-graph-component-ks-health-analyzer = health analyzer
construction-graph-tag-ks-syringe = syringe
construction-graph-tag-ks-surgery-tool = surgical tool
