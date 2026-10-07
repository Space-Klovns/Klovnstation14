# KS14: automatically translated; see _KS14/Localization/translation-review.tsv.

signal-port-description-ks-elevator-moving = Этот порт вызывается непосредственно перед тем, как лифт покидает этаж, чтобы двери, подключенные к нему, успели полностью закрыться.
signal-port-description-ks-elevator-stopped = Этот порт вызывается, когда лифт останавливается на этаже.
signal-port-name-ks-elevator-moving = Движение лифта
signal-port-name-ks-elevator-stopped = Лифт остановился
zlevel-elevator-call-already-here = Лифт уже здесь.
zlevel-elevator-call-called = Лифт вызван.
zlevel-elevator-call-no-shaft = Ничего не отвечает.
# One string per floor button, for the same reason: the markers a floor carries are not mutually
#     exclusive, and chaining a wrapper string per marker in C# hard-codes their order and spacing.
zlevel-elevator-ui-floor =
    Этаж { $number }{ $current ->
        [true] { " " }(здесь)
       *[false] { "" }
    }{ $called ->
        [true] { " " }{ "*" }
       *[false] { "" }
    }{ $obstructed ->
        [true] { " " }{ "[" }obstructed{ "]" }
       *[false] { "" }
    }
zlevel-elevator-ui-no-shaft = В этом шахте нет лифта.
# The whole status line is assembled here rather than in C#, so that a translation is free to word a
#     moving lift however its language wants instead of being handed a pre-chosen phrase.
zlevel-elevator-ui-status =
    { $moving ->
        [true]
            { $direction ->
                [Up] Восхождение
                [Down] Спуск
               *[Idle] Перемещение
            }
       *[false] Остановлено
    }
zlevel-elevator-ui-status-label = Статус:
zlevel-elevator-ui-title = контроллер лифта
