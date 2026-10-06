astro-nav-program-name = АстроНав

crew-manifest-cartridge-loading = Загрузка...

crew-manifest-cartridge-loading-failed = Ошибка загрузки манифеста экипажа!

crew-manifest-program-name = Манифест экипажа

default-program-name = Программа

device-pda-slot-component-slot-name-cartridge = Картридж

log-probe-label-accessor = Использовано:

log-probe-label-number = #

log-probe-label-time = Время

log-probe-print-button = Распечатать логи

log-probe-printout-device = Сканированное устройство: { $name }

log-probe-printout-entry = #{ $number } / { $time } / { $accessor }

log-probe-printout-header = Последние логи:

log-probe-program-name = Зонд логов

log-probe-scan = Загружены логи устройства { $device }!

med-tek-program-name = МедТек

nano-task-printed-description = [bold]Описание:[/bold] { $description }

nano-task-printed-high-priority = [bold]Приоритет[/bold]: [color=red]Высокий[/color]

nano-task-printed-low-priority = [bold]Приоритет[/bold]: Низкий

nano-task-printed-medium-priority = [bold]Приоритет[/bold]: Средний

nano-task-printed-requester = [bold]Заявитель:[/bold] { $requester }

nano-task-program-name = НаноДела

nano-task-ui-cancel = Отмена

nano-task-ui-delete = Удалить

nano-task-ui-description-label = Описание:

nano-task-ui-description-placeholder = Взять что-то важное

nano-task-ui-done = Готово

nano-task-ui-heading-high-priority-tasks =
    { $amount ->
        [zero] Нет задач высокого приоритета
        [one] 1 задача высокого приоритета
        [few] { $amount } задачи высокого приоритета
        *[other] { $amount } задач высокого приоритета
    }

nano-task-ui-heading-low-priority-tasks =
    { $amount ->
        [zero] Нет задач низкого приоритета
        [one] 1 задача низкого приоритета
        [few] { $amount } задачи низкого приоритета
        *[other] { $amount } задач низкого приоритета
    }

nano-task-ui-heading-medium-priority-tasks =
    { $amount ->
        [zero] Нет задач среднего приоритета
        [one] 1 задача среднего приоритета
        [few] { $amount } задачи среднего приоритета
        *[other] { $amount } задач среднего приоритета
    }

nano-task-ui-item-title = Редактировать задачу

nano-task-ui-new-task = Новая задача

nano-task-ui-print = Распечатать

nano-task-ui-priority-high = Высокий

nano-task-ui-priority-low = Низкий

nano-task-ui-priority-medium = Средний

nano-task-ui-requester-label = Заявитель:

nano-task-ui-requester-placeholder = Джон Нанотрейзен

nano-task-ui-revert-done = Отмена

nano-task-ui-save = Сохранить

net-probe-label-address = Адрес

net-probe-label-frequency = Частота

net-probe-label-name = Название

net-probe-label-network = Сеть

net-probe-program-name = Зонд сетей

net-probe-scan = Просканирован { $device }!

news-read-program-name = Новости станции

notekeeper-program-name = Заметки

wanted-list-age-label = [color=darkgray]Возраст:[/color] [color=white]{ $age }[/color]

wanted-list-gender-label = [color=darkgray]Гендер:[/color] [color=white]{ $gender }[/color]

wanted-list-history-table-initiator-col = Инициатор

wanted-list-history-table-reason-col = Преступление

wanted-list-history-table-time-col = Время

wanted-list-initiator-label = [color=darkgray]Инициатор:[/color] [color=white]{ $initiator }[/color]

wanted-list-job-label = [color=darkgray]Должность:[/color] [color=white]{ $job }[/color]

wanted-list-label-no-records = Всё спокойно, ковбой.

# Wanted list cartridge
wanted-list-program-name = Список разыскиваемых

wanted-list-reason-label = [color=darkgray]Причина:[/color] [color=white]{ $reason }[/color]

wanted-list-search-placeholder = Поиск по имени и статусу

wanted-list-species-label = [color=darkgray]Вид:[/color] [color=white]{ $species }[/color]

wanted-list-status-label = [color=darkgray]статус:[/color] { $status ->
    [suspected] [color=yellow]подозревается[/color]
    [wanted] [color=red]разыскивается[/color]
    [detained] [color=#b18644]под арестом[/color]
    [paroled] [color=green]освобождён по УДО[/color]
    [discharged] [color=green]освобождён[/color]
    [hostile] [color=darkred]враждебен[/color]
    [eliminated] [color=gray]ликвидирован[/color]
    *[other] нет
}

wanted-list-unknown-initiator-label = неизвестный инициатор

wanted-list-unknown-reason-label = неизвестная причина
