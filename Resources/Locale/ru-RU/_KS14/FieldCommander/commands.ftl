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
