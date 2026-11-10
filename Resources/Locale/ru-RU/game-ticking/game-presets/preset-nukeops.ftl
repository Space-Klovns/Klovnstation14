nukeops-briefing = Ваши задачи просты. Доставить бомбу и убраться до того, как она взорвётся. Начинайте миссию.

nukeops-cond-allnukiesalive = Все ядерные оперативники выжили.

nukeops-cond-allnukiesdead = Все ядерные оперативники погибли.

nukeops-cond-nukeactiveatcentcom = Ядерная бомба была доставлена Центральному командованию!

nukeops-cond-nukeactiveinstation = Ядерная бомба была оставлена взведённой на станции.

nukeops-cond-nukedisknotoncentcom = Экипаж оставил диск ядерной аутентификации на станции.

nukeops-cond-nukediskoncentcom = Экипаж улетел с диском ядерной аутентификации.

nukeops-cond-nukeexplodedoncorrectstation = Ядерным оперативникам удалось взорвать станцию.

nukeops-cond-nukeexplodedonincorrectlocation = Ядерная бомба взорвалась вне станции.

nukeops-cond-nukeexplodedonnukieoutpost = Аванпост ядерных оперативников был уничтожен ядерным взрывом!

nukeops-cond-nukiesabandoned = Ядерные оперативники были брошены.

nukeops-cond-somenukiesalive = Несколько ядерных оперативников погибли.

nukeops-crewmajor = [color=green]Разгромная победа экипажа![/color]

nukeops-crewminor = [color=green]Малая победа экипажа![/color]

nukeops-description = Ядерные оперативники нацелились на станцию. Постарайтесь не дать им взвести и взорвать ядерную бомбу, защищая ядерный диск!

nukeops-disk-carried-by = { " " }у [color=White]{ $name }[/color], [color=orange]{ $job }[/color], { $location } { $user ->
    [unknown] { "" }
    *[other] ([color=gray]{ $user }[/color])
}

nukeops-disk-location-title = Конечное местоположение диска:

nukeops-list-name = - [color=White]{ $name }[/color]

nukeops-list-name-user = - [color=White]{ $name }[/color] ([color=gray]{ $user }[/color])

nukeops-list-start = Оперативниками были:

nukeops-neutral = [color=yellow]Ничейный исход![/color]

nukeops-no-one-ready = Нет готовых игроков! Нельзя запустить пресет Ядерные оперативники.

nukeops-not-enough-ready-players = Недостаточно игроков готовы к игре! { $readyPlayersCount } игроков из необходимых { $minimumPlayers } готовы. Нельзя запустить пресет Ядерные оперативники.

nukeops-opsmajor = [color=crimson]Крупная победа Синдиката![/color]

nukeops-opsminor = [color=crimson]Малая победа Синдиката![/color]

nukeops-role-agent = Медик

nukeops-role-commander = Командир

nukeops-role-operator = Оператор

nukeops-title = Ядерные оперативники

nukeops-welcome =
    Вы — ядерный оперативник. Ваша задача — взорвать { $station } и убедиться, что от неё осталась лишь груда обломков. Ваше руководство, Синдикат, снабдило вас всем необходимым для выполнения этой задачи.
    Операция "{ $name }" началась! Смерть Nanotrasen!

storage-hierarchy-list = { $items-left ->
    [0] { $existing-text } { $item },
    *[other] { $existing-text } { $item }, в
}
