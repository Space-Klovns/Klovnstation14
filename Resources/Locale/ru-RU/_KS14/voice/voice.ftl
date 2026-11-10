# KS14: automatically translated; see _KS14/Localization/translation-review.tsv.

# Admin commands
cmd-vcmute-desc = Отключает голосовой чат игрока.
cmd-vcmute-help = vcmute <игрок> [minutes, 0 = rest of round] [reason...]
cmd-vcmute-invalid-args = Ожидается как минимум имя игрока.
cmd-vcmute-invalid-minutes = Минуты должны быть числом от 0 (оставшаяся часть раунда) до 525600.
cmd-vcmute-minutes-completion = [minutes, 0 = rest of round]
cmd-vcmute-no-player = Нет имени онлайн-игрока. { $player }.
cmd-vcmute-player-completion = <игрок>
cmd-vcmute-reason-completion = [reason]
cmd-vcmute-success = Отключение звука { $player }.
cmd-vcmutes-desc = Отображает список отключений звука в голосовом чате.
cmd-vcmutes-entry = { $player }: { $length }, автор { $admin }: { $reason }
cmd-vcmutes-entry-auto = { $player }: автоматически отключается звук при оскорбительном звуке. { $seconds }слева (vcunmute поднимает его)
cmd-vcmutes-help = vcmutes
cmd-vcmutes-none = Ни у кого не отключен звук.
cmd-vcunmute-desc = Отключает звук в голосовом чате игрока.
cmd-vcunmute-help =
    vcunmute <игрок>
    Голосовой чат
cmd-vcunmute-invalid-args = Ожидается ровно одно имя игрока.
cmd-vcunmute-not-muted = { $player } не имеет отключения звука.
cmd-vcunmute-success = Голосовой звук включен { $player }.
# Client command
cmd-voicechat-desc = Открывает окно голосового чата со ссылкой на ваш личный микрофон.
cmd-voicechat-help = голосовой чат
cmd-voicelink-desc = Открывает вашу личную страницу голосового чата в браузере.
cmd-voicelink-help = голосовая связь
ks-ui-options-voice-activation = Голосовая активация (разговор без удерживания кнопки «push-to-talk»)
ks-ui-options-voice-activation-tooltip = Вас будут слышать всякий раз, когда микрофонная страница улавливает звук, превышающий порог шумоподавления. Настройте порог на этой странице так, чтобы он не улавливал фоновый шум.
# Options
ks-ui-options-voice-header = Голосовой чат
ks-ui-options-voice-hear = Слушайте голоса других игроков
ks-ui-options-voice-jitter = Буфер голоса (мс)
ks-ui-options-voice-open = Настройка микрофона...
ks-ui-options-voice-volume = Громкость звука
# Moderation
ks-voice-admin-alert-auto-muted = { $player } был автоматически отключен звук для { $seconds }за чрезмерно громкий звук.
ks-voice-link-error-disabled = Голосовой чат на этом сервере отключен.
ks-voice-link-error-no-url = На этом сервере не настроен общедоступный адрес для голосового чата.
ks-voice-link-error-reset-cooldown = Ссылка НЕ сброшена: подождите несколько секунд и попробуйте снова.
ks-voice-mute-by-server = Сервер
ks-voice-mute-length-minutes =
    { $minutes } { $minutes ->
        [one] минута
       *[other] минут
    } влево
ks-voice-mute-length-round = остаток раунда
ks-voice-mute-reason-none = Причина не указана
ks-voice-mute-reason-verb = Отключено с помощью команды администратора
ks-voice-popup-auto-muted = Ваш голосовой чат был автоматически отключен: громкость вашего микрофона была слишком высокой.
# Popups
ks-voice-popup-cooldown = Ты уже слишком долго говоришь. Сделай передышку.
ks-voice-popup-muted = Ваш голосовой чат был отключен администратором.
# Verbs
ks-voice-verb-mute-local = Отключить голос
ks-voice-verb-mute-round = Отключение голоса (раунд)
ks-voice-verb-unmute = Включить звук
ks-voice-verb-unmute-local = Включить звук
ks-voice-window-connected = Страница «Микрофон» подключена.
ks-voice-window-copied = Скопировано!
ks-voice-window-copy = Скопировать ссылку
ks-voice-window-disconnected = Страница микрофона не подключена.
ks-voice-window-explanation =
    Чтобы говорить, откройте личный голосовой канал в веб-браузере и разрешите доступ к микрофону, а затем удерживайте кнопку «Нажмите, чтобы говорить» в игре.
    Чтобы слышать других игроков, ничего делать не нужно.
ks-voice-window-keybind = Кнопка «Нажми и говори»: { $key }
ks-voice-window-loading = Устанавливаю голосовую связь...
ks-voice-window-open = Открыть в браузере
ks-voice-window-reset = Ссылка для сброса
ks-voice-window-reset-tooltip = Создаёт новую ссылку и отключает любую страницу, открытую по старой ссылке.
# Link window
ks-voice-window-title = Голосовой чат
ks-voice-window-voice-activation = Голосовое управление включено: клавиши не требуются.
ks-voice-window-warning = Эта ссылка позволяет любому, у кого она есть, говорить от вашего имени. Не делитесь ею и не показывайте в прямом эфире.
# Keybinds
ui-options-function-ks-voice-push-to-talk = Голосовой чат с функцией «нажми и говори»
