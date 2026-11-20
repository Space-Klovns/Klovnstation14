cmd-ks_fieldcommander-no-rule = Полевой командир не запущен.

cmd-ks_fcphase-desc = Переводит каждого запущенного полевого командира в фазу, с её объявлением, капсулой и аплинком.
cmd-ks_fcphase-help = ks_fcphase <номер или название>
cmd-ks_fcphase-hint = <фаза>
cmd-ks_fcphase-unknown = Фазы «{ $phase }» нет.
cmd-ks_fcphase-set = Полевой командир переведён в фазу «{ $phase }» (номер { $index }).

cmd-ks_fcwave-desc = Отправляет следующую волну текущей фазы полевого командира прямо сейчас.
cmd-ks_fcwave-help = ks_fcwave
cmd-ks_fcwave-sent = Волна отправлена. Всего: { $waves }.

cmd-ks_fcuplink-desc = Высаживает аплинк полевого командира прямо сейчас.
cmd-ks_fcuplink-help = ks_fcuplink
cmd-ks_fcuplink-already = Аплинк уже высажен.
cmd-ks_fcuplink-deployed = Аплинк высажен.
cmd-ks_fcuplink-failed = Не нашлось места для высадки аплинка.

cmd-ks_fcpoints-desc = Начисляет или списывает очки защитнику в режиме полевого командира, а с «set» — устанавливает их.
cmd-ks_fcpoints-help = ks_fcpoints <игрок> <количество> [set]
cmd-ks_fcpoints-hint-player = <игрок>
cmd-ks_fcpoints-hint-amount = <количество>
cmd-ks_fcpoints-hint-set = [set]
cmd-ks_fcpoints-done = У { $player } теперь { $points } очков.

cmd-ks_fcstatus-desc = Показывает состояние каждого запущенного полевого командира: фазу, таймеры, бойцов и очки.
cmd-ks_fcstatus-help = ks_fcstatus
cmd-ks_fcstatus-summary = { $rule }: фаза { $index } ({ $phase }), прошло { $elapsed } с. Следующая фаза через { $nextPhase } с, следующая волна через { $nextWave } с. Адаптивный множитель ×{ $adaptive }. Бойцов в живых { $alive }/{ $cap }. Защитников в живых { $defenders }, погибло { $dead }. Волн отправлено: { $waves }. Аплинк: { $uplink }.
cmd-ks_fcstatus-uplink-none = не высажен
cmd-ks_fcstatus-uplink-destroyed = уничтожен
cmd-ks_fcstatus-uplink-active = удар через { $seconds } с
cmd-ks_fcstatus-points = { $player }: { $points } очков
cmd-ks_fcstatus-stuck = Застрявших бойцов сейчас: { $stuck }. Отправлено заново из-за застревания: { $relocated }, возвращено в бюджет: { $refunded }.

cmd-ks_fcstuck-desc = Показывает каждого бойца полевого командира, который давно никуда не продвинулся, начиная с самых долгих, и что делают его рулёжка и HTN.
cmd-ks_fcstuck-help = ks_fcstuck [секунд без продвижения, по умолчанию 0]
cmd-ks_fcstuck-hint = [секунды]
cmd-ks_fcstuck-none = Нет бойцов, которые так долго никуда не продвигались.
cmd-ks_fcstuck-count = Бойцов: { $count }:

ks-fieldcommander-stuck-unit = { $unit } в { $position }, идёт к { $goal } (ближе всего { $best }), без продвижения { $seconds } с, этап { $stage } ({ $reason }). Рулёжка: { $steering }. HTN: { $task }.
ks-fieldcommander-stuck-steering = { $status }, неудачных путей { $failed }, узлов осталось { $path }, поиск пути { $pathfinding }, к { $target } в пределах { $range }
ks-fieldcommander-stuck-htn-disabled = отключён
ks-fieldcommander-stuck-htn-none = нет плана
ks-fieldcommander-stuck-htn-plan = { $operator } (задача { $index } из { $count }, ветви { $branches })

cmd-ks_fcpause-desc = Останавливает или снова запускает часы всех полевых командиров. Его бойцы и защитники продолжают сражаться; пока он на паузе, не наступают ни фазы, ни волны, ни приказы, ни бомбардировка.
cmd-ks_fcpause-help = ks_fcpause [on|off]
cmd-ks_fcpause-hint = [on|off]
cmd-ks_fcpause-paused = Полевой командир приостановлен.
cmd-ks_fcpause-resumed = Полевой командир продолжает.
cmd-ks_fcstatus-paused = Пауза: его часы остановлены.
