ks-fieldcommander-title = Полевой командир
ks-fieldcommander-description = Полевой командир Синдиката осаждает ваш аванпост. Укрепляйтесь, удерживайте цели во время штурма и уничтожьте его аплинк, пока он не навёл орбитальный удар.

ks-fieldcommander-unit-name = неизвестный боец

## Phases

ks-fieldcommander-phase-landfall = Высадка
ks-fieldcommander-phase-probing = Разведка боем
ks-fieldcommander-phase-pressure = Давление
ks-fieldcommander-phase-siege = Осада
ks-fieldcommander-phase-final = Последний штурм

ks-fieldcommander-phase-landfall-announcement = Внимание, аванпост Nanotrasen. Говорит полевой командир Синдиката. Вы окружены, ваши серверы обречены. Распорядитесь этими минутами с умом. Других спокойных у вас не будет.
ks-fieldcommander-phase-probing-announcement = Разведгруппы выдвигаются. Посмотрим, чего стоит ваша оборона.
ks-fieldcommander-phase-pressure-announcement = Вы ещё держитесь. Похвально. Элитные отряды и боры-проходчики получили допуск к развёртыванию.
ks-fieldcommander-phase-siege-announcement = Довольно. Артиллерии разрешён огонь по вашим укреплениям. Каждая построенная вами стена — мишень.
ks-fieldcommander-phase-final-announcement = Всем подразделениям: последний штурм. Мой аплинк спускается, чтобы навести орбитальный удар на ваш аванпост. Никто не уйдёт.

## Waves

ks-fieldcommander-wave-1 = { $count ->
    [one] { $count } оперативник на подходе.
    [few] { $count } оперативника на подходе.
   *[other] { $count } оперативников на подходе.
} Не затягивайте.
ks-fieldcommander-wave-2 = Свежий отряд развёртывается. Выследить их.
ks-fieldcommander-wave-3 = Подкрепление отправлено. Их позиции отмечены у вас на экране.
ks-fieldcommander-wave-4 = Следующая волна выдвигается. Пленных не брать.
ks-fieldcommander-wave-5 = Ещё один отряд на подходе. Не ослабляйте давление.
ks-fieldcommander-wave-6 = Ещё { $count } для вашего кладбища, Nanotrasen.

ks-fieldcommander-strike-1 = Огневая задача подтверждена. Опасная близость.
ks-fieldcommander-strike-2 = Боеприпас пошёл. На вашем месте я бы отошёл от стен.
ks-fieldcommander-strike-3 = Цель подсвечена. Ракета на подлёте.

ks-fieldcommander-asset-driller = На подходе борг-проходчик. Он не остановится, пока ваши серверы не станут моими.

## Objectives and the uplink

ks-fieldcommander-objective-lost = Один из ваших серверов теперь мой. { $remaining ->
    [one] Остался последний.
    [few] Осталось { $remaining }.
   *[other] Осталось { $remaining }.
}
ks-fieldcommander-uplink-deployed = Мой аплинк развёрнут в { $distance } метрах { $direction ->
    [north] к северу от
    [northeast] к северо-востоку от
    [east] к востоку от
    [southeast] к юго-востоку от
    [south] к югу от
    [southwest] к юго-западу от
    [west] к западу от
    [northwest] к северо-западу от
   *[other] от
} вашего аванпоста, и он под охраной. Через { $minutes } мин. он наведёт удар вам на головы. Попробуйте остановить его, если сможете.
ks-fieldcommander-uplink-warning = Координаты удара зафиксированы. { $minutes } мин.

## Round end

ks-fieldcommander-roundend-no-phase = нет
ks-fieldcommander-roundend-summary = Полевой командир дошёл до фазы [color=red]{ $phase }[/color] и отправил { $waves } волн, всего { $sent } бойцов. Убито из них: { $killed }. Потеряно целей: { $lost }.
ks-fieldcommander-roundend-uplink-destroyed = Аплинк уничтожен через { $minutes } мин. { $seconds } с. после высадки.
ks-fieldcommander-roundend-uplink-bombarded = Аплинк продержался достаточно долго, чтобы навести орбитальный удар.

## The defenders' briefing

ks-fieldcommander-intro-sender = Полевое управление Nanotrasen
ks-fieldcommander-intro = Аванпост, говорит Полевое управление, приоритетная связь. Дальние сенсоры фиксируют высадку полевой командной группы Синдиката вокруг вашей позиции. В ближайшие минуты ожидайте разведку боем, за ней — полноценный штурм. Укрепляйтесь, удерживайте серверы, ни шагу назад. За каждое подтверждённое уничтожение вам начисляется кредит снабжения: тратьте его в терминале снабжения на боеприпасы и огневую поддержку. Конец связи.

## Points

ks-fieldcommander-points-total = Снабжение: { $points }
ks-fieldcommander-points-gain = +{ $amount }  { $reason }
ks-fieldcommander-points-loss = −{ $amount }  { $reason }
ks-fieldcommander-points-kill = { $name }
ks-fieldcommander-points-teamkill = Убийство союзника: { $name }
ks-fieldcommander-points-uplink = Аплинк уничтожен
ks-fieldcommander-points-admin = Корректировка Полевого управления
