# KS14: automatically translated; see _KS14/Localization/translation-review.tsv.

reagent-effect-condition-guidebook-blood-reagent-threshold =
    { $max ->
        [2147483648] там есть как минимум { NATURALFIXED($min, 2) }u из { $reagent }
       *[other]
            { $min ->
                [0] есть не более { NATURALFIXED($max, 2) }u из { $reagent }
               *[other] есть между { NATURALFIXED($min, 2) }u и { NATURALFIXED($max, 2) }u из { $reagent }
            }
    }
