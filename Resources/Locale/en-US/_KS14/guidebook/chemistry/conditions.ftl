reagent-effect-condition-guidebook-blood-reagent-threshold =
    { $max ->
        [2147483648] there's at least {NATURALFIXED($min, 2)}u of {$reagent}
        *[other] { $min ->
                    [0] there's at most {NATURALFIXED($max, 2)}u of {$reagent}
                    *[other] there's between {NATURALFIXED($min, 2)}u and {NATURALFIXED($max, 2)}u of {$reagent}
                    }
    }

reagent-effect-condition-guidebook-total-damage =
    the target has taken at least {NATURALFIXED($min, 2)} damage
