delivery-already-opened-examine = Уже вскрыто.

delivery-bomb-examine = Это [color=purple]{ $type }-бомба[/color]. О нет.

delivery-bomb-primed-examine = Это [color=purple]{ $type }-бомба[/color]. Читать это – пустая трата вашего времени.

delivery-earnings-examine = Доставка этого принесёт станции [color=yellow]{ $spesos }[/color] кредитов.

delivery-fragile-broken-examine = [color=red]{ $type } имеет хрупкое содержимое[/color]. Выглядит сильно поврежденно.

delivery-fragile-examine = [color=red]{ $type } имеет хрупкое содержимое[/color]. Доставьте невредимым для получения бонуса.

delivery-open-verb = Открыть

delivery-opened-others = { CAPITALIZE($recipient) } { GENDER($recipient) ->
    [male] открыл
    [female] открыла
    [epicene] открыли
    *[neuter] открыло
} { $delivery }.

delivery-opened-self = Вы вскрываете { $delivery }.

delivery-priority-delivered-examine = [color=orange]{ $type } с высоким приоритетом[/color]. Доставлено вовремя.

# modifiers
delivery-priority-examine = [color=orange]{ $type } с высоким приоритетом[/color]. У вас осталось [color=orange]{ $time }[/color], чтобы доставить это и получить бонус.

delivery-priority-expired-examine = [color=orange]{ $type } с высоким приоритетом[/color]. Время истекло.

delivery-recipient-examine = Адресовано: { $recipient }, { $job }.

delivery-recipient-no-job = Неизвестно

delivery-recipient-no-name = Безымянный

delivery-slice-verb = Вскрыть

delivery-teleporter-amount-examine =
    Содержит [color=yellow]{ $amount }[/color] { $amount ->
        [one] посылку.
        [few] посылки.
        *[other] посылок.
    }

delivery-teleporter-empty = { CAPITALIZE($entity) } пуст.

delivery-teleporter-empty-verb = Взять почту

delivery-unlock-verb = Разблокировать

delivery-unlocked-others = { CAPITALIZE($recipient) } { GENDER($recipient) ->
    [male] разблокировал
    [female] разблокировала
    [epicene] разблокировали
    *[neuter] разблокировало
} { $delivery } используя свой отпечаток пальца.

delivery-unlocked-self = Вы разблокировали { $delivery } отпечатком пальца.
