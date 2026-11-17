entity-condition-guidebook-total-hunger =
    { $max ->
        [2147483648] Цель имеет как минимум { NATURALFIXED($min, 2) } Total Hunger
       *[other]
            { $min ->
                [0] цель имеет не более { NATURALFIXED($max, 2) } Total Hunger
               *[other] Цель имеет от { NATURALFIXED($min, 2) } и { NATURALFIXED($max, 2) } Total Hunger
            }
    }
