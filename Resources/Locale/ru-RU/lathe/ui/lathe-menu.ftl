lathe-menu-amount = Кол-во:

lathe-menu-category-all = Всё

lathe-menu-delete-fabricating-tooltip = Отменить производство текущего объекта.

lathe-menu-delete-item-tooltip = Отменить производство этой партии.

lathe-menu-description-display = [italic]{ $description }[/italic]

lathe-menu-fabricating-message = Производится...

lathe-menu-item-batch = { $index }. { $name } ({ $printed }/{ $total })

lathe-menu-item-single = { $index }. { $name }

lathe-menu-material-amount = { $amount ->
    [1] { NATURALFIXED($amount, 2) } ({ $unit })
    *[other] { NATURALFIXED($amount, 2) } ({ $unit })
}

lathe-menu-material-amount-missing = { $amount ->
    [1] { NATURALFIXED($amount, 2) } { $unit } { $material } ([color=red]{ NATURALFIXED($missingAmount, 2) } { $unit } не хватает[/color])
    *[other] { NATURALFIXED($amount, 2) } { $unit } { $material } ([color=red]{ NATURALFIXED($missingAmount, 2) } { $unit } не хватает[/color])
}

lathe-menu-material-display = { $material } { $amount }

lathe-menu-materials-title = Материалы

lathe-menu-move-down-tooltip = Перенести эту партию назад в очереди.

lathe-menu-move-up-tooltip = Перенести эту партию вперёд в очереди.

lathe-menu-no-materials-message = Материалы не загружены

lathe-menu-queue = Очередь

lathe-menu-queue-title = Очередь производства

lathe-menu-reagent-slot-examine = Сбоку имеется отверстие для мензурки.

lathe-menu-recipe-count = { $count ->
    [1] { $count } Рецепт
    [few] { $count } Рецепта
    *[other] { $count } Рецептов
}

lathe-menu-result-reagent-display = { $reagent } ({ $amount } ед.)

lathe-menu-search-designs = Поиск проектов

lathe-menu-search-filter = Фильтр

lathe-menu-server-list = Список серверов

lathe-menu-silo-linked-message = Хранилище связано

lathe-menu-sync = Синхр.

lathe-menu-title = Меню станка

lathe-menu-tooltip-display = { $amount } { $material }

lathe-reagent-dispense-no-container = Жидкость выливается из { $name } на пол!
