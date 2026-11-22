ks-fieldcommander-title = Полевой командир
ks-fieldcommander-description = Полевой командир Синдиката осаждает ваш аванпост. Укрепляйтесь, удерживайте цели во время штурма и уничтожьте его аплинк, пока он не навёл орбитальный удар.

ks-fieldcommander-unit-name = неизвестный боец

## Phases

ks-fieldcommander-phase-landfall = Высадка
ks-fieldcommander-phase-probing = Разведка боем
ks-fieldcommander-phase-pressure = Давление
ks-fieldcommander-phase-siege = Осада
ks-fieldcommander-phase-final = Последний штурм

# Командир лишь язвит. Всё, на что могут отреагировать защитники, приходит от Полевого управления в виде разведданных,
#     кроме предупреждения командира об ударе (ks-fieldcommander-strike-warning).
ks-fieldcommander-phase-landfall-announcement = Аванпост Nanotrasen, говорит ваш новый сосед. Мы высадились, распаковываемся и скоро зайдём, так что никуда не уходите.
ks-fieldcommander-phase-probing-announcement = Отправляю к вам пару человек посмотреть, так что будьте вежливы.
ks-fieldcommander-phase-pressure-announcement = Всё ещё там, я вижу.
ks-fieldcommander-phase-siege-announcement = Я был терпелив.
ks-fieldcommander-phase-final-announcement = Довольно.

ks-fieldcommander-intel-phase-landfall = Командный сигнал Синдиката в вашем секторе подтверждён, движения пока нет, так что укрепляйтесь, пока можете.
ks-fieldcommander-intel-phase-probing = К вам идут небольшие, легко вооружённые группы, и преследовать их в тумане не стоит.
ks-fieldcommander-intel-phase-pressure = Противник вводит элитные отряды и боргов-проходчиков, часть — десантными капсулами, и боргам нужны серверы.
ks-fieldcommander-intel-phase-siege = Артиллерия Синдиката бьёт по укреплениям, так что рассредоточьтесь.
ks-fieldcommander-intel-phase-final = Это последний штурм: высаживается аплинк наведения для орбитального удара, и мы сообщим координаты, как только их получим.

## Waves

ks-fieldcommander-wave-1 = Они уже идут.
ks-fieldcommander-wave-2 = Ещё.
ks-fieldcommander-wave-3 = У меня таких много.
ks-fieldcommander-wave-4 = Снова.
ks-fieldcommander-wave-5 = Я могу так дольше, чем вы.
ks-fieldcommander-wave-6 = Вы, должно быть, устали.

ks-fieldcommander-intel-wave = Волна { $number }: { $count } { $count ->
    [one] противник
    [few] противника
   *[other] противников
}, идут с { $direction ->
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

ks-fieldcommander-strike-1 = Не двигайтесь.
ks-fieldcommander-strike-2 = Славная крепость.
ks-fieldcommander-strike-3 = Спасибо, что построили всё в одном месте.
# Единственное предупреждение, которое командир даёт сам: ракеты его, и весть о них подписана им.
ks-fieldcommander-strike-warning = Я бы встал в другом месте.

ks-fieldcommander-asset-driller = Говорят, вам дороги эти серверы.
ks-fieldcommander-intel-driller = Приближается борг-проходчик, идёт к серверу.
ks-fieldcommander-asset-drone = Слышите?
ks-fieldcommander-intel-drone = Приближаются дроны-камикадзе, и перед взрывом они пищат.
ks-fieldcommander-drone-armed = { CAPITALIZE(THE($device)) } пищит всё чаще и чаще!
ks-fieldcommander-asset-sniper = Не высовывайтесь.
ks-fieldcommander-intel-sniper = За вашими позициями снайпер, и его красный лазер перестаёт мигать прямо перед выстрелом.
ks-fieldcommander-asset-juggernaut = Сначала вы его услышите.
ks-fieldcommander-intel-juggernaut = Приближается джаггернаут: медленный, в тяжёлой броне, и его не оглушить и не сбить с ног.
ks-fieldcommander-asset-breacher = Двери — для гостей.
ks-fieldcommander-intel-breacher = Приближаются подрывники с зарядами для стен, так что следите за стенами.
ks-fieldcommander-asset-tank = Кое-что я приберёг напоследок.
ks-fieldcommander-intel-tank = Приближается танк с фугасными снарядами, который пищит, пока наводится, и гибнет без башни или без корпуса, лучше всего от ракеты.
ks-fieldcommander-asset-tank-flamer = Холодно там?
ks-fieldcommander-intel-tank-flamer = Приближается огнемётный танк, так что не подпускайте его и не стойте в огне, который он оставляет.
ks-fieldcommander-asset-mortar = Стойте вместе, не расходитесь.
ks-fieldcommander-intel-mortar = За вашими позициями разворачивается миномётный расчёт, так что смотрите под ноги и помните, что захваченный миномёт — ваш.

## Objectives and the uplink

ks-fieldcommander-objective-lost = Благодарю.
ks-fieldcommander-intel-objective-lost = Мы потеряли сервер, { $remaining ->
    [one] и остался только один.
   *[other] и теперь их осталось { $remaining }.
}
ks-fieldcommander-uplink-deployed = Время пошло.
ks-fieldcommander-intel-uplink = Мы нашли аплинк в { $distance } м { $direction ->
    [north] к северу
    [northeast] к северо-востоку
    [east] к востоку
    [southeast] к юго-востоку
    [south] к югу
    [southwest] к юго-западу
    [west] к западу
    [northwest] к северо-западу
   *[other] за периметром
} от аванпоста, под охраной, и у вас примерно { $minutes } мин., чтобы взломать или уничтожить его до удара.
ks-fieldcommander-intel-uplink-warning = Аплинк зафиксировал координаты, и до удара { $minutes } мин.
ks-fieldcommander-intel-reinforcements = К вашей позиции летит капсула снабжения.

## Round end

ks-fieldcommander-roundend-no-phase = нет
ks-fieldcommander-roundend-summary = Полевой командир дошёл до фазы [color=red]{ $phase }[/color] и отправил { $waves } волн, всего { $sent } бойцов. Убито из них: { $killed }. Потеряно целей: { $lost }.
ks-fieldcommander-roundend-uplink-destroyed = Аплинк уничтожен через { $minutes } мин. { $seconds } с после высадки.
ks-fieldcommander-roundend-uplink-bombarded = Аплинк продержался достаточно долго, чтобы навести орбитальный удар.

## The defenders' briefing

ks-fieldcommander-intel-sender = Полевое управление Nanotrasen
ks-fieldcommander-briefing = Аванпост, говорит Полевое управление. Вокруг вашей позиции высаживается полевая командная группа Синдиката, так что укрепляйтесь, удерживайте серверы и не выходите за периметр, потому что туман смертелен. За подтверждённые уничтожения начисляется кредит снабжения, который тратится в терминале снабжения. Конец связи.

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
ks-fieldcommander-intel-endless-vote = Аплинк уничтожен, и у вас { $seconds } с, чтобы проголосовать, отступить или остаться.
ks-fieldcommander-endless = Я не ухожу, и вы тоже.
ks-fieldcommander-intel-endless = Значит, вы остаётесь, и каждая волна будет больше и раньше предыдущей.

## Doctrines: how the commander spends a wave's budget

ks-fieldcommander-doctrine-standard = Обычная
ks-fieldcommander-doctrine-swarm = Рой
ks-fieldcommander-doctrine-spearhead = Остриё
ks-fieldcommander-doctrine-airborne = Десант
ks-fieldcommander-doctrine-hoard = Накопление
ks-fieldcommander-intel-doctrine-swarm = Это много лёгкой пехоты, так что следите за флангами.
ks-fieldcommander-intel-doctrine-spearhead = Эту волну ведут их лучшие.
ks-fieldcommander-intel-doctrine-airborne = Приближаются десантные капсулы, и садятся они обычно внутри ваших позиций.
ks-fieldcommander-intel-doctrine-hoard = Волна была небольшой, а значит, они копят силы.

ks-fieldcommander-wave-status-paused = { $wave ->
    [0] Волны приостановлены
   *[other] Волна { $wave } · пауза
}

ks-fieldcommander-points-revive = Реанимация: { $name }
ks-fieldcommander-points-heal = Лечение: { $name }

## Концовка: проваливший задачу командир мобилизован и отправлен в бой сам.

ks-fieldcommander-mobik-sender = Верховное командование «Синдиката»
ks-fieldcommander-mobik-announcement = Полевой командир не сумел взять аванпост Nanotrasen и настоящим отстранён от командования и мобилизован с одной винтовкой, без брони и без звания. Поздравляем с повышением до мобика, боец. Фронт там.
ks-fieldcommander-intel-mobik = Приближается одна капсула, примерно через { $seconds } с, и в ней единственный призывник, который раньше был их командиром.
ks-fieldcommander-intel-mobik-killed = Мобик мёртв, и он был последним.
ks-fieldcommander-mobik-name = бывший полевой командир
ks-fieldcommander-points-mobik = Мобик убит
ks-fieldcommander-roundend-mobik-killed = Отстранённый от командования и отправленный в бой мобиком, полевой командир тоже погиб.

ks-fieldcommander-rcd-blocked = РСУ заблокировано, пока с волной не покончено.
