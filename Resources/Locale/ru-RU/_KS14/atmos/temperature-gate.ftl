ks-temperature-gate-examined-status = Он [color={ $enabled ->
    [true] green]включён
   *[false] red]выключен
}[/color].
ks-temperature-gate-examined-tolerance = Он пропускает газ, пока вход { $inverted ->
    [true] дальше чем на
   *[false] не дальше чем на
} [color=lightblue]{ $tolerance } К[/color] от [color=lightblue]{ $temperature } К[/color].
ks-temperature-gate-examined-range = Он пропускает газ, пока температура входа { $inverted ->
    [true] вне диапазона
   *[false] в диапазоне
} от [color=lightblue]{ $minimum } К[/color] до [color=lightblue]{ $maximum } К[/color].

ks-temperature-gate-verb-on = Включить
ks-temperature-gate-verb-off = Выключить

ks-temperature-gate-inverted = Теперь шлюз открыт вне своих пределов.
ks-temperature-gate-restored = Теперь шлюз открыт в своих пределах.
ks-temperature-gate-unanchor-on = Сначала выключите его!

ks-temperature-gate-ui-title = Температурный шлюз
ks-temperature-gate-ui-set = Задать
ks-temperature-gate-ui-max = Макс.
ks-temperature-gate-ui-target = Цель (К):
ks-temperature-gate-ui-tolerance = Допуск (К):
ks-temperature-gate-ui-minimum = Минимум (К):
ks-temperature-gate-ui-maximum = Максимум (К):
ks-temperature-gate-ui-mode-label = Режим:
ks-temperature-gate-ui-mode-tolerance = Допуск
ks-temperature-gate-ui-mode-range = Мин-макс
ks-temperature-gate-ui-open-when = Открыт, пока вход { $inverted ->
    [true] вне
   *[false] в
} этих пределах.
ks-temperature-gate-ui-invert-hint = Примените к шлюзу мультитул, чтобы поменять это условие.
