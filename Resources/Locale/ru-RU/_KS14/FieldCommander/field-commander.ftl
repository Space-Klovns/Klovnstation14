ks-fieldcommander-title = Полевой командир
ks-fieldcommander-description = Полевой командир Синдиката осаждает ваш аванпост. Укрепляйтесь, удерживайте цели во время штурма и уничтожьте его аплинк, пока он не навёл орбитальный удар.

ks-fieldcommander-unit-name = неизвестный боец

## Phases

ks-fieldcommander-phase-landfall = Высадка
ks-fieldcommander-phase-probing = Разведка боем
ks-fieldcommander-phase-pressure = Давление
ks-fieldcommander-phase-siege = Осада
ks-fieldcommander-phase-final = Последний штурм

# Командир лишь язвит. Всё, на что могут отреагировать защитники, приходит от Полевого управления в виде разведданных.
ks-fieldcommander-phase-landfall-announcement = Nanotrasen. Я вижу вас отсюда: мечетесь за своими стенками, как насекомые. Окапывайтесь. Тем проще будет вас выкапывать.
ks-fieldcommander-phase-probing-announcement = Мои псы спущены с поводка. Послушаем, как громко вы умеете кричать.
ks-fieldcommander-phase-pressure-announcement = Ещё дышите? Это канцелярская ошибка, и я её сейчас исправлю.
ks-fieldcommander-phase-siege-announcement = Хватит игр. Каждая стена, что вы возвели, — гроб, и я закрываю крышки.
ks-fieldcommander-phase-final-announcement = Пора заканчивать. Я сожгу ваш аванпост в стекло и спишу ваши имена в допустимые потери.

ks-fieldcommander-intel-phase-landfall = Говорит Полевое управление. Командный сигнал Синдиката в вашем секторе подтверждён, движения противника пока нет. Пользуйтесь затишьем: укрепляйтесь, прикройте серверы, ставьте турели.
ks-fieldcommander-intel-phase-probing = Полевое управление. Ожидайте небольшие разведгруппы, в основном с пистолетами. Они прощупывают вашу оборону. Не преследуйте их в тумане.
ks-fieldcommander-intel-phase-pressure = Полевое управление. Элитным отрядам и борам-проходчикам дан допуск, часть прибудет десантными капсулами. Боры идут прямо к серверам: уничтожайте их в первую очередь.
ks-fieldcommander-intel-phase-siege = Полевое управление. Артиллерии Синдиката разрешён огонь по вашим укреплениям: ждите ударов по всему, что вы построили. Рассредоточьтесь и не давайте РЦД простаивать.
ks-fieldcommander-intel-phase-final = Полевое управление. Последний штурм. Синдикат высаживает аплинк наведения для орбитального удара по вашей позиции. Как только мы его засечём — уничтожьте.

## Waves

ks-fieldcommander-wave-1 = Вот и они. Постарайтесь не умирать слишком быстро, моим людям станет скучно.
ks-fieldcommander-wave-2 = Ещё отряд, Nanotrasen. Людей у меня больше, чем у вас патронов.
ks-fieldcommander-wave-3 = Свежее мясо в мясорубку. Ваше, не моё.
ks-fieldcommander-wave-4 = Пригните головы. Мои люди в них и целятся.
ks-fieldcommander-wave-5 = Каждого убитого вами я заменю. Каждого убитого мной — вы нет.
ks-fieldcommander-wave-6 = Держите строй, солдатики. Это последнее, что вы удержите.

ks-fieldcommander-intel-wave = Полевое управление. Волна { $number }: сенсоры фиксируют { $count } { $count ->
    [one] противника
    [few] противников
   *[other] противников
}, приближение с { $direction ->
    [north] севера
    [northeast] северо-востока
    [east] востока
    [southeast] юго-востока
    [south] юга
    [southwest] юго-запада
    [west] запада
    [northwest] северо-запада
   *[other] периметра
}.

ks-fieldcommander-strike-1 = Улыбнитесь артиллерии.
ks-fieldcommander-strike-2 = Я вижу вашу крепостишку. Мой огневой налёт тоже.
ks-fieldcommander-strike-3 = Из ваших стен выйдет отличная шрапнель.
ks-fieldcommander-intel-strike = Полевое управление. Огневой налёт, опасно близко! Отойдите от укреплений!

ks-fieldcommander-asset-driller = Мой бор идёт за вашими серверами, и вам нечем его остановить.
ks-fieldcommander-intel-driller = Полевое управление. Приближается бор-проходчик. Он идёт прямо к серверу — сосредоточьте огонь на нём.

## Objectives and the uplink

ks-fieldcommander-objective-lost = Ещё один сервер мой. Чувствуете, как всё ускользает?
ks-fieldcommander-intel-objective-lost = Полевое управление. Мы потеряли сервер. { $remaining ->
    [one] Остался один. Удержите его любой ценой.
   *[other] Осталось: { $remaining }.
}
ks-fieldcommander-uplink-deployed = Мой аплинк на земле. Молитесь, если Nanotrasen позволяет.
ks-fieldcommander-intel-uplink = Полевое управление. Аплинк Синдиката засечён: { $distance } м { $direction ->
    [north] к северу
    [northeast] к северо-востоку
    [east] к востоку
    [southeast] к юго-востоку
    [south] к югу
    [southwest] к юго-западу
    [west] к западу
    [northwest] к северо-западу
   *[other] за периметром
} от аванпоста, под охраной. Удар примерно через { $minutes } мин. Взломайте или взорвите его до этого.
ks-fieldcommander-intel-uplink-warning = Полевое управление. Аплинк зафиксировал координаты. Удар через { $minutes } мин. Уничтожьте его немедленно!
ks-fieldcommander-intel-reinforcements = Полевое управление. К вашей позиции летит капсула снабжения. Заберите её раньше Синдиката.

## Round end

ks-fieldcommander-roundend-no-phase = нет
ks-fieldcommander-roundend-summary = Полевой командир дошёл до фазы [color=red]{ $phase }[/color] и отправил { $waves } волн, всего { $sent } бойцов. Убито из них: { $killed }. Потеряно целей: { $lost }.
ks-fieldcommander-roundend-uplink-destroyed = Аплинк уничтожен через { $minutes } мин. { $seconds } с после высадки.
ks-fieldcommander-roundend-uplink-bombarded = Аплинк продержался достаточно долго, чтобы навести орбитальный удар.

## The defenders' briefing

ks-fieldcommander-intel-sender = Полевое управление Nanotrasen
ks-fieldcommander-briefing = Аванпост, говорит Полевое управление, приоритетная связь. Дальние сенсоры фиксируют высадку полевой командной группы Синдиката вокруг вашей позиции. В ближайшие минуты ожидайте разведку боем, за ней — полноценный штурм. Укрепляйтесь, удерживайте серверы, ни шагу назад. Не выходите за периметр: туман за ним смертелен. За каждое подтверждённое уничтожение вам начисляется кредит снабжения: тратьте его в терминале снабжения на боеприпасы и огневую поддержку. Конец связи.

## Points

ks-fieldcommander-points-total = Снабжение: { $points }
ks-fieldcommander-points-gain = +{ $amount }  { $reason }
ks-fieldcommander-points-loss = −{ $amount }  { $reason }
ks-fieldcommander-points-kill = { $name }
ks-fieldcommander-points-assist = Помощь: { $name }
ks-fieldcommander-points-teamkill = Убийство союзника: { $name }
ks-fieldcommander-points-teamkill-assist = Помощь в убийстве союзника: { $name }
ks-fieldcommander-points-uplink = Аплинк уничтожен
ks-fieldcommander-points-admin = Корректировка Полевого управления

## Requisitions

ks-fieldcommander-shop-maptext = СНАБЖЕНИЕ
ks-supplypod-passenger-name = боец Nanotrasen
ks-fieldcommander-wave-status = Волна { $wave } · следующая через { $time }
ks-fieldcommander-wave-status-first = Первая волна через { $time }
ks-fieldcommander-wave-status-none = Волна { $wave } · новых не ожидается
ks-fieldcommander-wave-status-idle = Волны ещё не начались

ks-fieldcommander-roundend-waves = Оборона продержалась до волны [color=yellow]{ $wave }[/color].
ks-fieldcommander-roundend-waves-endless = Оборона продержалась до волны [color=yellow]{ $wave }[/color], из них { $endless } в бесконечном режиме.

ks-fieldcommander-endless-vote-title = Аплинк уничтожен. Держать оборону в бесконечном режиме?
ks-fieldcommander-endless-vote-initiator = Полевое управление
ks-fieldcommander-endless-vote-yes = Держать оборону
ks-fieldcommander-endless-vote-no = Отступить
ks-fieldcommander-intel-endless-vote = Полевое управление. Аплинк уничтожен, поле за вами. Голосуйте в течение { $seconds } с: отступить или окопаться и узнать, сколько вы продержитесь.
ks-fieldcommander-endless = Думали, это всё? Всё, что у меня осталось, идёт за вами, и будет идти дальше.
ks-fieldcommander-intel-endless = Полевое управление. Вы держите оборону. Каждая волна будет больше и раньше предыдущей. Держитесь сколько сможете.
