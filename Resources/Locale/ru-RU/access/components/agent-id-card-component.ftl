agent-id-new = { CAPITALIZE($card) } { $number ->
    [0] не дала новых доступов
    [one] дала { $number } новый доступ
    [few] дала { $number } новых доступа
    *[other] дала { $number } новых доступов
}.

agent-id-open-ui-verb = Настроить
