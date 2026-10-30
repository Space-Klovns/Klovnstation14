# Asteroids
dungeon-config-proto-BlobAsteroid = Астероидный массив

dungeon-config-proto-ClusterAsteroid = Астероидный кластер

dungeon-config-proto-SpindlyAsteroid = Астероидная спираль

dungeon-config-proto-SwissCheeseAsteroid = Фрагменты астероидов

salvage-asteroid-name = Астероид

salvage-expedition-window-progression = Прогресс

# Debris
salvage-magnet-debris-ChunkDebris = Космический обломок

salvage-magnet-resources = { $resource ->
    [OreIron] Железо
    [OreCoal] Уголь
    [OreQuartz] Кварц
    [OreSalt] Соль
    [OreGold] Золото
    [OreDiamond] Алмазы
    [OreSilver] Серебро
    [OrePlasma] Плазма
    [OreUranium] Уран
    [OreArtifactFragment] Фрагменты артефактов
    [OreBananium] Бананиум
    *[other] { $resource }
}

salvage-magnet-resources-count = { $count ->
    [1] (Мало)
    [2] (Средне)
    [3] (Средне)
    [4] (Много)
    [5] (Много)
    *[other] (Изобилие)
}

salvage-magnet-window-title = Магнит обломков

# Wrecks
salvage-map-wreck = Обломок для утилизации

salvage-map-wreck-desc-size = Размер:

salvage-map-wreck-size-large = [color=orchid]Большой[/color]

salvage-map-wreck-size-medium = [color=cornflowerblue]Средний[/color]

salvage-map-wreck-size-small = [color=lime]Малый[/color]

salvage-system-announcement-arrived = Обломок был притянут для утилизации. Расчётное время удержания: { $timeLeft } секунд.

salvage-system-announcement-losing = Магнит больше не может удерживать обломок. Оставшееся время удержания: { $timeLeft } секунд.

salvage-system-announcement-spawn-debris-disintegrated = Обломок дезинтегрировал во время орбитального перемещения.

salvage-system-announcement-spawn-no-debris-available = Нет обломков, которые можно притянуть магнитом.
