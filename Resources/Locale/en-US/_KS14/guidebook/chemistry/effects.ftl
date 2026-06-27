entity-effect-guidebook-stain-clean = Cleans touched entities of stains.

reagent-effect-guidebook-add-to-chemicals =
    { $chance ->
        [1] { $deltasign ->
                [1] Adds
                *[-1] Removes
            }
        *[other]
            { $deltasign ->
                [1] add
                *[-1] remove
            }
    } {NATURALFIXED($amount, 2)}u of {$reagent} { $deltasign ->
        [1] to
        *[-1] from
    } the solution

entity-effect-guidebook-regenerateorgans-nomax = Regenerates all missing organs in the body at once
entity-effect-guidebook-regenerateorgans-withmax =
    Regenerates up to {$count} missing {
        $count ->
            [one] organ
           *[other] organs
    } in the body at once

entity-effect-guidebook-gib =
    { $chance ->
        [1] Gibs
        *[other] gib
    } the mob.
