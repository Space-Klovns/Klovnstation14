ks-temperature-gate-examined = Он [color={ $enabled ->
    [true] green]включён
   *[false] red]выключен
}[/color] и пропускает газ, пока вход { $inverted ->
    [true] горячее
   *[false] холоднее
} [color=lightblue]{ $temperature } К[/color].

ks-temperature-gate-verb-maximize = Задать максимальную температуру
ks-temperature-gate-verb-on = Включить
ks-temperature-gate-verb-off = Выключить

ks-temperature-gate-inverted = Теперь шлюз открывается выше заданной температуры.
ks-temperature-gate-restored = Теперь шлюз открывается ниже заданной температуры.
ks-temperature-gate-unanchor-on = Сначала выключите его!

ks-temperature-gate-ui-title = Температурный шлюз
ks-temperature-gate-ui-set = Задать
ks-temperature-gate-ui-max = Макс.
ks-temperature-gate-ui-target = Заданная температура (К):
ks-temperature-gate-ui-mode = Открывается, когда вход { $inverted ->
    [true] горячее
   *[false] холоднее
} заданной температуры.
