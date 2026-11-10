-markings-selection = { $selectable ->
    [0] Вы больше не можете выбрать черту.
    [one] Вы можете выбрать еще одну черту.
    *[other] Вы можете выбрать ещё { $selectable } черты.
}

humanoid-marking-modifier-base-layers = Базовый слой

humanoid-marking-modifier-enable = Включить

humanoid-marking-modifier-prototype-id = ID прототипа:

humanoid-marking-modifier-respect-group-sex = Учитывать ограничение расы и пола

humanoid-marking-modifier-respect-limits = Учитывать ограничения

markings-layer-Chest = Туловище

markings-layer-Eyes = Глаза

markings-layer-FacialHair = Лицевая растительность

markings-layer-Hair = Волосы

markings-layer-Head = Голова

markings-layer-HeadSide = Голова (Бок)

markings-layer-HeadTop = Голова (Верх)

markings-layer-LArm = Левая рука

markings-layer-LFoot = Левая стопа

markings-layer-LHand = Левая кисть

markings-layer-LLeg = Левая нога

markings-layer-Overlay = Наложение

markings-layer-RArm = Правая рука

markings-layer-RFoot = Правая стопа

markings-layer-RHand = Правая кисть

markings-layer-RLeg = Правая нога

markings-layer-Snout = Нос

markings-layer-SnoutCover = Нос (Покрытие)

markings-layer-Special = Особое

markings-layer-Tail = Хвост

markings-layer-Tail-Moth = Крылья

markings-layer-TailOverlay = Наложение

markings-layer-UndergarmentBottom = Трусы

markings-layer-UndergarmentTop = Нижняя рубашка

markings-limits = { $required ->
    [true] { $count ->
            [-1] Выберите хотя бы одну черту.
            [0] Вы не можете выбрать ещё черту, но как-то, должны? Это баг.
            [one] Выберите одну черту.
            *[other] Выберите хотя бы одну черту и до { $count }. { -markings-selection(selectable: $selectable) }
        }
    *[false] { $count ->
            [-1] Выберите любое количество черт.
            [0] Вы больше не можете выбрать черту.
            [one] Выберите до одной черты.
            *[other] Выберите до { $count } черт. { -markings-selection(selectable: $selectable) }
        }
}

markings-organ-ArmLeft = Левая рука

markings-organ-ArmRight = Правая рука

markings-organ-Eyes = Глаза

markings-organ-FootLeft = Левая стопа

markings-organ-FootRight = Правая стопа

markings-organ-HandLeft = Левая кисть

markings-organ-HandRight = Правая кисть

markings-organ-Head = Голова

markings-organ-LegLeft = Левая нога

markings-organ-LegRight = Правая нога

markings-organ-Torso = Туловище

markings-reorder = Выбранные черты

markings-search = Поиск
