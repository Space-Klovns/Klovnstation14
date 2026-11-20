cmd-ks_fcadmin-desc = Открывает окно администратора полевого командира: его состояние, фазы, ресурсы и особые волны.
cmd-ks_fcadmin-help = ks_fcadmin

ks-fieldcommander-admin-menu-button = Полевой командир
ks-fieldcommander-admin-title = Полевой командир
ks-fieldcommander-admin-no-rule = Полевой командир не запущен.

ks-fieldcommander-admin-status-phase = Фаза { $index }: { $phase }
ks-fieldcommander-admin-status-phase-none = Фаза: ещё не началась
ks-fieldcommander-admin-status-doctrine = Доктрина: { $doctrine }
ks-fieldcommander-admin-status-doctrine-none = Доктрина: нет
ks-fieldcommander-admin-status-next-wave = Следующая волна через { $seconds } с
ks-fieldcommander-admin-status-next-wave-none = Волна не назначена
ks-fieldcommander-admin-status-next-phase = Следующая фаза через { $seconds } с
ks-fieldcommander-admin-status-next-phase-none = Последняя фаза
ks-fieldcommander-admin-status-alive = Бойцов в живых: { $alive }/{ $cap }
ks-fieldcommander-admin-status-defenders = Защитников: { $defenders }
ks-fieldcommander-admin-status-budget = Перенесённый бюджет: { $budget }
ks-fieldcommander-admin-status-waves = Волн отправлено: { $waves }, в очереди: { $queued }
ks-fieldcommander-admin-status-paused = { $paused ->
    [true] ПАУЗА
   *[false] Идёт
}

ks-fieldcommander-admin-pause = Пауза
ks-fieldcommander-admin-unpause = Продолжить
ks-fieldcommander-admin-force-wave = Волна сейчас
ks-fieldcommander-admin-refresh = Обновить
ks-fieldcommander-admin-apply = Применить

ks-fieldcommander-admin-result-accepted = Готово.
ks-fieldcommander-admin-result-refused = Отклонено: проверьте значения.
ks-fieldcommander-admin-result-invalid = Не отправлено: поле пустое или не число.

ks-fieldcommander-admin-tab-phases = Фазы
ks-fieldcommander-admin-tab-assets = Ресурсы
ks-fieldcommander-admin-tab-custom-wave = Особая волна
ks-fieldcommander-admin-tab-queue = Очередь

ks-fieldcommander-admin-phase-header-index = №
ks-fieldcommander-admin-phase-header-name = Фаза
ks-fieldcommander-admin-phase-header-start = Начало (с)
ks-fieldcommander-admin-phase-header-interval = Между волнами (с)
ks-fieldcommander-admin-phase-header-budget = Бюджет
ks-fieldcommander-admin-phase-header-apply = { "" }
ks-fieldcommander-admin-phase-header-go = { "" }
ks-fieldcommander-admin-phase-go = Перейти
ks-fieldcommander-admin-phase-current = ▶ { $index }

ks-fieldcommander-admin-asset-header-index = №
ks-fieldcommander-admin-asset-header-entity = Сущность
ks-fieldcommander-admin-asset-header-humanoid = Гуманоид
ks-fieldcommander-admin-asset-header-count = Кол-во
ks-fieldcommander-admin-asset-header-cost = Цена
ks-fieldcommander-admin-asset-header-weight = Вес
ks-fieldcommander-admin-asset-header-min-phase = Мин. фаза
ks-fieldcommander-admin-asset-header-max-alive = Макс. живых
ks-fieldcommander-admin-asset-header-delivery = Доставка
ks-fieldcommander-admin-asset-header-living = Живых
ks-fieldcommander-admin-asset-header-actions = { "" }
ks-fieldcommander-admin-asset-header-tags = Метки
ks-fieldcommander-admin-asset-header-every = Каждые N
ks-fieldcommander-admin-asset-header-on-phase = Начало фазы
ks-fieldcommander-admin-asset-header-name = Имя
ks-fieldcommander-admin-asset-header-stand-off = Дистанция
ks-fieldcommander-admin-asset-header-kill-points = Очки
ks-fieldcommander-admin-asset-header-orders = Приказы
ks-fieldcommander-admin-asset-header-announcement = Объявление
ks-fieldcommander-admin-asset-header-intel = Разведка
ks-fieldcommander-admin-asset-kill-points-placeholder = по цене
ks-fieldcommander-admin-asset-locid-placeholder = id строки или пусто
ks-fieldcommander-admin-asset-tags-placeholder = infantry, pod
ks-fieldcommander-admin-asset-disable = Отключить
ks-fieldcommander-admin-asset-enable = Включить
ks-fieldcommander-admin-asset-add = Добавить ресурс
ks-fieldcommander-admin-asset-add-placeholder = ID прототипа сущности
ks-fieldcommander-admin-asset-add-hint = Добавляется с количеством 1, ценой 1 и весом 1; измените после добавления.
ks-fieldcommander-admin-delivery-foot = Пешком
ks-fieldcommander-admin-delivery-pod = Капсулой

ks-fieldcommander-admin-wave-asset = Ресурс
ks-fieldcommander-admin-wave-count = Покупок
ks-fieldcommander-admin-wave-option = №{ $index } { $name } (×{ $count }, цена { $cost })
ks-fieldcommander-admin-wave-add-row = Добавить строку
ks-fieldcommander-admin-wave-remove-row = Убрать
ks-fieldcommander-admin-wave-announcement = Объявление, отправляется как написано (необязательно):
ks-fieldcommander-admin-wave-announcement-placeholder = Оставьте пустым для обычной насмешки командира
ks-fieldcommander-admin-wave-queue = В очередь
ks-fieldcommander-admin-wave-send-now = Отправить сейчас

ks-fieldcommander-admin-queue-empty = Очередь пуста.
ks-fieldcommander-admin-queue-wave = { $position }. { $purchases }
ks-fieldcommander-admin-queue-purchase = { $count } × №{ $index } { $name }
ks-fieldcommander-admin-queue-announcement = «{ $text }»
ks-fieldcommander-admin-queue-remove = Убрать
ks-fieldcommander-admin-queue-dropped = Полевой командир: волна из очереди отброшена, её никогда не отправить: все её активы отключены или приводят больше юнитов, чем предел в { $cap } живых.
ks-fieldcommander-admin-list-separator = ,{" "}
