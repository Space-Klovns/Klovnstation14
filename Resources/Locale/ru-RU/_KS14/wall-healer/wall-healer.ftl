## Examine

ks-wall-healer-examine-supplies = В запасе: [color=lightblue]{ $count }[/color] — { $item }. [color=gray]Alt-клик[/color], чтобы взять.
ks-wall-healer-examine-patient = Сейчас в ней рука { $patient }.
ks-wall-healer-examine-allowance = Остаток лимита первой помощи: [color=lightblue]{ NATURALFIXED($remaining, 1) }[/color] из { NATURALFIXED($limit, 1) }.

## Putting hands in and taking them out

ks-wall-healer-incompatible = Несовместимо!
ks-wall-healer-too-far = Подведите { OBJECT($patient) } ближе!
ks-wall-healer-busy = { $self ->
    [true] Вы пытаетесь сунуть руку в { $healer }, но { $current } уже пользуется ей.
   *[false] Вы пытаетесь сунуть руку { $patient } в { $healer }, но { $current } уже пользуется ей.
}

ks-wall-healer-insert-self = { $powered ->
    [true] Вы суёте руку в { $healer }, и какой-то датчик тут же сканирует её.
   *[false] Вы суёте руку в { $healer }, но она не реагирует.
}
ks-wall-healer-insert-self-others = { $powered ->
    [true] { CAPITALIZE($patient) } суёт руку в { $healer }, и какой-то датчик тут же сканирует её.
   *[false] { CAPITALIZE($patient) } суёт руку в { $healer }, но та не реагирует. Похоже, не работает.
}
ks-wall-healer-insert-other = { $powered ->
    [true] Вы суёте руку { $patient } в { $healer }, и какой-то датчик тут же сканирует её.
   *[false] Вы суёте руку { $patient } в { $healer }, но та не реагирует. Похоже, не работает.
}
ks-wall-healer-insert-other-others = { $powered ->
    [true] { CAPITALIZE($user) } суёт руку { $patient } в { $healer }, и какой-то датчик тут же сканирует её.
   *[false] { CAPITALIZE($user) } суёт руку { $patient } в { $healer }, но та не реагирует. Похоже, не работает.
}

ks-wall-healer-remove-self = Вы вынимаете руку из { $healer }.
ks-wall-healer-remove-self-others = { CAPITALIZE($patient) } вынимает руку из { $healer }.
ks-wall-healer-remove-other = Вы вытаскиваете руку { $patient } из { $healer }.

## Treatment

ks-wall-healer-treated = Несколько инструментов и шприцев обрабатывают вашу руку. Вам становится немного лучше.
ks-wall-healer-emagged-treatment = Вы чувствуете острую боль: машина сбоит и колет вас инструментами и иглами!
ks-wall-healer-not-organic = Ничего не происходит. Похоже, { $healer } не распознаёт неорганических существ.
ks-wall-healer-blocked = Ничего не происходит. Похоже, { $healer } не может найти открытого участка кожи.
ks-wall-healer-no-id = Ничего не происходит. { CAPITALIZE($healer) } не лечит без ID-карты.
ks-wall-healer-allowance-spent = Ничего не происходит. Лимит первой помощи на { $card } пока исчерпан.
ks-wall-healer-needs-recharge = Ничего не происходит. Похоже, { $healer } нужно перезарядиться.
ks-wall-healer-healthy = Ничего не происходит. Похоже, вы в достаточно хорошей форме.

## Supplies

ks-wall-healer-verb-take-supply = Взять: { $item }
ks-wall-healer-supply-unpowered = Вы пытаетесь достать { $item }, но { $healer } не реагирует.
ks-wall-healer-supply-empty = Вы пытаетесь достать { $item }, но в { $healer }, похоже, ничего не осталось.
ks-wall-healer-supply-jammed = Вы пытаетесь достать { $item }, но всё застревает в лотке выдачи.
ks-wall-healer-supply-taken = Вы достаёте { $item } из { $healer }.
ks-wall-healer-supply-taken-others = { CAPITALIZE($user) } достаёт { $item } из { $healer }.
ks-wall-healer-restock = Вы пополняете { $healer }: { $item }.
ks-wall-healer-restock-others = { CAPITALIZE($user) } пополняет { $healer }: { $item }.
ks-wall-healer-restock-failed = Вы пытаетесь пополнить { $healer }, но { $item } будто прилип к руке.

## Construction

construction-graph-tag-ks-wall-healer-electronics = электроника станции первой помощи
construction-graph-tag-ks-wall-healer-emergency-electronics = электроника аварийной станции первой помощи
construction-graph-component-ks-health-analyzer = анализатор здоровья
construction-graph-tag-ks-syringe = шприц
construction-graph-tag-ks-surgery-tool = хирургический инструмент
