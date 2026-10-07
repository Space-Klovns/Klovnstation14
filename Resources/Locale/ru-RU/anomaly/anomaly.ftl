anomaly-behavior-balanced = Отклонения поведения не обнаружены.

anomaly-behavior-delayed-force = Частота пульсаций значительно снижена, но их сила повышена.

anomaly-behavior-fast = [color=crimson]Частота импульсов значительно повышена.[/color]

anomaly-behavior-inconstancy = [color=crimson]Обнаружено непостоянство. Со временем типы частиц могут поменяться.[/color]

anomaly-behavior-invisibility = Обнаружено искажение светового потока.

anomaly-behavior-light = [color=forestgreen]Мощность импульсов значительно снижена.[/color]

anomaly-behavior-moving = [color=crimson]Обнаружена координатная нестабильность.[/color]

anomaly-behavior-nonsensivity = Обнаружена слабая реакция на частицы.

anomaly-behavior-point = [color=gold]Аномалия генерирует { $mod }% очков[/color]

anomaly-behavior-rapid = Частота пульсаций значительно повышена, но их сила снижена.

anomaly-behavior-reflect = Обнаружено защитное покрытие.

anomaly-behavior-safe = [color=forestgreen]Аномалия чрезвычайно стабильна. Крайне редкие импульсы.[/color]

anomaly-behavior-secret = Обнаружены помехи. Некоторые данные не могут быть считаны

anomaly-behavior-sensivity = Обнаружена сильная реакция на частицы.

anomaly-behavior-slow = [color=forestgreen]Частота импульсов значительно снижена.[/color]

anomaly-behavior-strenght = [color=crimson]Мощность импульсов значительно повышена.[/color]

anomaly-behavior-title = Анализ отклонений поведения:

anomaly-behavior-unknown = [color=red]ОШИБКА. Невозможно считать.[/color]

anomaly-command-pulse = Вызывает импульс аномалии

anomaly-command-supercritical = Целевая аномалия переходит в суперкритическое состояние

anomaly-component-contact-damage = Аномалия сдирает с вас кожу!

anomaly-generator-announcement = Аномалия была создана!

anomaly-generator-charges = { $charges ->
    [one] { $charges } заряд
    [few] { $charges } заряда
    *[other] { $charges } зарядов
}

anomaly-generator-cooldown = Перезарядка: [color=gray]{ $time }[/color]

# Flavor text on the footer
anomaly-generator-flavor-left = Аномалия может возникнуть внутри оператора.

anomaly-generator-flavor-right = v1.1

anomaly-generator-fuel-display = Топливо:

anomaly-generator-generate = Создать аномалию

anomaly-generator-no-cooldown = Перезарядка: [color=gray]Завершена[/color]

anomaly-generator-no-fire = Статус: [color=crimson]Не готов[/color]

anomaly-generator-ui-title = генератор аномалий

anomaly-generator-yes-fire = Статус: [color=forestgreen]Готов[/color]

anomaly-gorilla-charge-infinite = Осталось [color=gold]бесконечное количество зарядов[/color]. [italic]Пока что...[/italic]

anomaly-gorilla-charge-limit = { $count ->
    [one] Остался
    *[other] Осталось
} [color={ $count ->
    [3] green
    [2] yellow
    [1] orange
    [0] red
    *[other] purple
}]{ $count } { $count ->
    [one] заряд
    [few] заряда
    *[other] зарядов
}[/color].

anomaly-gorilla-charge-none = Внутри нет [bold]ядра аномалии[/bold].

anomaly-gorilla-core-slot-name = Ядро аномалии

anomaly-particles-delta = Дельта-частицы

anomaly-particles-epsilon = Эпсилон-частицы

anomaly-particles-omega = Омега-частицы

anomaly-particles-sigma = Сигма-частицы

anomaly-particles-zeta = Зета-частицы

anomaly-scanner-component-scan-complete = Сканирование завершено!

anomaly-scanner-no-anomaly = Нет просканированной аномалии.

anomaly-scanner-particle-containment = - [color=goldenrod]Сдерживающий тип:[/color] { $type }

anomaly-scanner-particle-containment-unknown = - [color=goldenrod]Сдерживающий тип:[/color] [color=red]ОШИБКА[/color]

anomaly-scanner-particle-danger = - [color=crimson]Опасный тип:[/color] { $type }

anomaly-scanner-particle-danger-unknown = - [color=crimson]Опасный тип:[/color] [color=red]ОШИБКА[/color]

anomaly-scanner-particle-readout = Анализ реакции на частицы:

anomaly-scanner-particle-transformation = - [color=#6b75fa]Трансформирующий тип:[/color] { $type }

anomaly-scanner-particle-transformation-unknown = - [color=#6b75fa]Трансформирующий тип:[/color] [color=red]ОШИБКА[/color]

anomaly-scanner-particle-unstable = - [color=plum]Нестабильный тип:[/color] { $type }

anomaly-scanner-particle-unstable-unknown = - [color=plum]Нестабильный тип:[/color] [color=red]ОШИБКА[/color]

anomaly-scanner-point-output = Пассивная генерация очков: [color=gray]{ $point }[/color]

anomaly-scanner-point-output-unknown = Пассивная генерация очков: [color=red]ОШИБКА[/color]

anomaly-scanner-pulse-timer = Время до следующего импульса: [color=gray]{ $time }[/color]

anomaly-scanner-severity-percentage = Текущая опасность: [color=gray]{ $percent }[/color]

anomaly-scanner-severity-percentage-unknown = Текущая опасность: [color=red]ОШИБКА[/color]

anomaly-scanner-stability-high = Текущее состояние аномалии: [color=crimson]Рост[/color]

anomaly-scanner-stability-low = Текущее состояние аномалии: [color=gold]Распад[/color]

anomaly-scanner-stability-medium = Текущее состояние аномалии: [color=forestgreen]Стабильное[/color]

anomaly-scanner-stability-unknown = Текущее состояние аномалии: [color=red]ОШИБКА[/color]

anomaly-scanner-ui-title = сканер аномалий

anomaly-secret-admin = [color=red](ОШИБКА)[/color]

anomaly-sync-connect-verb-message = Присоединить близлежащую аномалию к { $machine }.

anomaly-sync-connect-verb-text = Присоединить аномалию

anomaly-sync-connected = Аномалия успешно привязана

anomaly-sync-disconnect-verb-message = Отсоединить подключённую аномалию от { $machine }.

anomaly-sync-disconnect-verb-text = Отсоединить аномалию

anomaly-sync-disconnected = Соединение с аномалией было потеряно!

anomaly-sync-examine-connected = Он [color=darkgreen]присоединён[/color] к аномалии.

anomaly-sync-examine-not-connected = Он [color=darkred]не присоединён[/color] к аномалии.

anomaly-sync-no-anomaly = Отсутствует аномалия в пределах диапазона.

anomaly-vessel-component-anomaly-assigned = Аномалия присвоена сосуду.

anomaly-vessel-component-assigned = Этому сосуду уже присвоена аномалия.

anomaly-vessel-component-not-assigned = Этому сосуду не присвоена ни одна аномалия. Попробуйте использовать на нём сканер.
