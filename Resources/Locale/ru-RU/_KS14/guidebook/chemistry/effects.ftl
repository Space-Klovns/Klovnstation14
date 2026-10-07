# KS14: automatically translated; see _KS14/Localization/translation-review.tsv.

entity-effect-guidebook-gib =
    { $chance ->
        [1] Gibs
       *[other] gib
    } толпа
entity-effect-guidebook-regenerateorgans-nomax = Мгновенно восстанавливает все утраченные органы в организме
entity-effect-guidebook-regenerateorgans-withmax =
    Восстанавливается до { $count } отсутствует { $count ->
        [one] орган
       *[other] органы
    } в тексте сразу
entity-effect-guidebook-stain-clean = Удаляет пятна с очищаемых объектов.
reagent-effect-guidebook-add-to-chemicals =
    { $chance ->
        [1]
            { $deltasign ->
                [1] Добавлено
               *[-1] Удаляет
            }
       *[other]
            { $deltasign ->
                [1] добавить
               *[-1] удалить
            }
    } { NATURALFIXED($amount, 2) }u из { $reagent } { $deltasign ->
        [1] к
       *[-1] из
    } решение
