entity-effect-guidebook-plant-remove-kudzu =
    { $chance ->
        [1] Удаляет
       *[other] удалить
    } Рост сорняка кудзу из растения
entity-effect-guidebook-satiate-hunger =
    { $chance ->
        [1] Удовлетворяет
       *[other] насытить
    } { $relative ->
        [1] уровень голода: средний
       *[other] голод в { NATURALFIXED($relative, 3) }x средний курс
    }
entity-effect-guidebook-satiate-thirst =
    { $chance ->
        [1] Удовлетворяет
       *[other] насытить
    } { $relative ->
        [1] жажда — средняя
       *[other] thirst at { NATURALFIXED($relative, 3) }x средний курс
    }
