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
ks-fieldcommander-asset-drone = Слышите жужжание? Это за вами.
ks-fieldcommander-intel-drone = Полевое управление. Приближаются дроны-камикадзе. Перед взрывом они пищат и вспыхивают красным: отходите или сбивайте их.
ks-fieldcommander-drone-armed = { CAPITALIZE(THE($device)) } пищит всё чаще и чаще!
ks-fieldcommander-asset-sniper = Не высовывайтесь. Или высовывайтесь, и я сниму вам головы.
ks-fieldcommander-intel-sniper = Полевое управление. Противник выставил снайпера. Следите за красным лазером: как только он перестанет мигать, последует выстрел. Снайпер бьёт издалека и отходит, если на него бросаются.
ks-fieldcommander-asset-juggernaut = Слышите шаги? Бегите. Не поможет.
ks-fieldcommander-intel-juggernaut = Полевое управление. Приближается джаггернаут: пулемёт, туша, которая выдержит магазин за магазином, и его не оглушить и не сбить с ног. Он медленный, так что держите дистанцию и заливайте его огнём.
ks-fieldcommander-asset-breacher = Стены? Как мило.
ks-fieldcommander-intel-breacher = Полевое управление. Приближаются подрывники с зарядами для стен. Где бы вы ни засели, они проделают проход рядом с вами, а не пойдут через дверь. Следите за стенами и слушайте, не ставят ли заряд.
ks-fieldcommander-asset-tank = Финал заслуживает тяжёлой брони. Поздоровайтесь.
ks-fieldcommander-intel-tank = Полевое управление. Приближается вражеский танк. Его пушка медленно стреляет фугасными снарядами: наводясь, он пищит, так что, услышав писк, уходите из виду. Башня приварена к корпусу, и уничтожение любой из частей добивает его. Ракеты.
ks-fieldcommander-asset-tank-flamer = Холодно? Сейчас согрею.
ks-fieldcommander-intel-tank-flamer = Полевое управление. Приближается огнемётный танк. Ему нужно подобраться близко, а там, куда он попадёт, земля продолжает гореть: держите дистанцию и не стойте в огне.
ks-fieldcommander-asset-mortar = Соберитесь в кучку. Пожалуйста. Мины сэкономлю.
ks-fieldcommander-intel-mortar = Полевое управление. Вражеский миномётный расчёт разворачивается примерно в сорока тайлах. Мина прилетает каждые секунд двенадцать — туда, где вы стоите кучей, или по турели: как увидите метку на земле или услышите свист, уходите с неё. Найдите расчёт — и миномёт ваш, со всеми оставшимися минами.

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

## Doctrines: how the commander spends a wave's budget

ks-fieldcommander-doctrine-standard = Обычная
ks-fieldcommander-doctrine-swarm = Рой
ks-fieldcommander-doctrine-spearhead = Остриё
ks-fieldcommander-doctrine-airborne = Десант
ks-fieldcommander-doctrine-hoard = Накопление
ks-fieldcommander-intel-doctrine-swarm = Полевое управление. Много лёгкой пехоты. Не прекращайте огонь и не дайте себя обойти.
ks-fieldcommander-intel-doctrine-spearhead = Полевое управление. Эту волну ведут их лучшие. Держитесь и сосредоточьте огонь.
ks-fieldcommander-intel-doctrine-airborne = Полевое управление. Вижу десантные капсулы. Следите за небом над вашими позициями.
ks-fieldcommander-intel-doctrine-hoard = Полевое управление. Волна слабая. Они что-то копят — используйте время.

ks-fieldcommander-wave-status-paused = { $wave ->
    [0] Волны приостановлены
   *[other] Волна { $wave } · пауза
}

ks-fieldcommander-points-revive = Реанимация: { $name }
ks-fieldcommander-points-heal = Лечение: { $name }

## Концовка: проваливший задачу командир мобилизован и отправлен в бой сам.

ks-fieldcommander-mobik-sender = Верховное командование «Синдиката»
ks-fieldcommander-mobik-announcement = Верховное командование «Синдиката» на всех частотах. Полевой командир не сумел взять опорный пункт Nanotrasen. Он отстранён от командования и мобилизован с этой минуты: одна винтовка, без брони, без звания. Поздравляем с повышением до мобика, боец. Фронт там.
ks-fieldcommander-intel-mobik = Полевое управление. Через { $seconds } с прибудет одна капсула с единственным призывником: их бывшим командиром. Избавьте его от мучений.
ks-fieldcommander-intel-mobik-killed = Полевое управление. Мобик готов. Это был последний.
ks-fieldcommander-mobik-name = бывший полевой командир
ks-fieldcommander-points-mobik = Мобик убит
ks-fieldcommander-roundend-mobik-killed = Отстранённый от командования и отправленный в бой мобиком, полевой командир тоже погиб.
