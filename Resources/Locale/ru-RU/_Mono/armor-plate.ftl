armor-plate-break = Ваша { $plateName } раскололась!
armor-plate-examine-with-plate = Установлена [color=yellow]{ $plateName }[/color]. Прочность: [color={ $durabilityColor }]{ $percent }%[/color]
armor-plate-examine-no-plate = Бронепластина не установлена.

armor-plate-examinable-verb-text = Свойства пластины
armor-plate-examinable-verb-message = Изучить защиту и прочность.

armor-plate-attributes-examine = Эта бронепластина:
armor-plate-initial-durability = Рассчитана на [color=yellow]{ $durability }[/color] единиц урона.

armor-plate-gait = { $gait ->
        [walk] скорость ходьбы
        [sprint] скорость бега
       *[both] скорость
    }

armor-plate-speed-display =
    { $deltasign ->
        [-1] Увеличивает вашу { armor-plate-gait } на [color=yellow]{ $speedPercent }%[/color].
        [1] Уменьшает вашу { armor-plate-gait } на [color=yellow]{ $speedPercent }%[/color].
       *[0] Не влияет на скорость.
    }

armor-plate-ratios-display =
    { $deltasign ->
        [-1] [color=cyan]Поглощает[/color] [color=yellow]{ $ratioPercent }%[/color] урона типа [color=yellow]{ $dmgType }[/color] и получает его как [color=yellow]x{ $multiplier }[/color] урона прочности.
        [1] [color=fuchsia]Усиливает[/color] урон типа [color=yellow]{ $dmgType }[/color] на [color=yellow]{ $ratioPercent }%[/color] и получает добавку как [color=yellow]x{ $multiplier }[/color] урона прочности.
       *[0] Не влияет на урон типа { $dmgType }.
    }
armor-plate-stamina-value = Наносит [color=yellow]{ $multiplier }%[/color] поглощённого урона как урон выносливости.
