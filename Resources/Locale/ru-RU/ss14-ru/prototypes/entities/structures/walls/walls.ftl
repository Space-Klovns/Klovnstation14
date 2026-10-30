ent-BaseStructureWall = базовая стена
    .desc = Удерживает воздух внутри, а грейтайдеров снаружи.

ent-BaseWall = { ent-BaseStructureWall }
    .desc = { ent-BaseStructureWall.desc }

ent-Cardwall = картонная стена
    .desc = Сокращение бюджета наносит сильный удар.

ent-WallBrick = кирпичная стена
    .desc = { ent-BaseWall.desc }

ent-WallDebug = debug wall
    .desc = { ent-BaseWall.desc }
    .suffix = DEBUG

ent-WallDiagonalBase = { ent-BaseStructureWall }
    .desc = { ent-BaseStructureWall.desc }
    .suffix = Диагональ

ent-WallForce = силовой барьер
    .desc = { "" }

ent-WallGold = золотая стена
    .desc = { ent-BaseWall.desc }

ent-WallInvisible = невидимая стена
    .desc = { "" }

ent-WallMeat = мясная стена
    .desc = Липко.

ent-WallMining = шахтёрская стена
    .desc = { ent-BaseWall.desc }

ent-WallMiningDiagonal = шахтёрская стена
    .desc = { ent-WallDiagonalBase.desc }
    .suffix = { ent-WallDiagonalBase.suffix }

ent-WallPlasma = плазменная стена
    .desc = { ent-BaseWall.desc }

ent-WallPlastitanium = пластитановая стена
    .desc = { ent-WallPlastitaniumIndestructible.desc }

ent-WallPlastitaniumDiagonal = пластитановая стена
    .desc = { ent-WallPlastitaniumDiagonalIndestructible.desc }
    .suffix = Диагональ

ent-WallPlastitaniumDiagonalIndestructible = пластитановая стена
    .desc = { ent-WallDiagonalBase.desc }
    .suffix = Диагональ, Неразрушимое

ent-WallPlastitaniumIndestructible = пластитановая стена
    .desc = { ent-BaseWall.desc }
    .suffix = Неразрушимый

ent-WallReinforced = укреплённая стена
    .desc = { ent-BaseWall.desc }

ent-WallReinforcedChitin = укреплённый экзодермис
    .desc = { ent-BaseWall.desc }

ent-WallReinforcedDiagonal = укреплённая стена
    .desc = { ent-WallDiagonalBase.desc }
    .suffix = { ent-WallDiagonalBase.suffix }

ent-WallReinforcedRust = { ent-WallReinforced }
    .desc = { ent-WallReinforced.desc }
    .suffix = Ржавый

ent-WallShuttle = стена шаттла
    .desc = { ent-BaseWall.desc }

ent-WallShuttleDiagonal = стена шаттла
    .desc = { ent-WallDiagonalBase.desc }
    .suffix = { ent-WallDiagonalBase.suffix }

ent-WallSilver = серебряная стена
    .desc = { ent-BaseWall.desc }

ent-WallSolid = обычная стена
    .desc = { ent-BaseWall.desc }

ent-WallSolidChitin = экзодермическая стена
    .desc = { ent-BaseWall.desc }

ent-WallSolidDiagonal = обычная стена
    .desc = { ent-WallDiagonalBase.desc }
    .suffix = { ent-WallDiagonalBase.suffix }

ent-WallSolidRust = { ent-WallSolid }
    .desc = { ent-WallSolid.desc }
    .suffix = Ржавый

ent-WallUranium = урановая стена
    .desc = { ent-BaseWall.desc }

ent-WallWeb = паутинная стена
    .desc = Удерживает паучат внутри, а грейтайдеров снаружи.

ent-WallWood = деревянная стена
    .desc = Традиционная защита от грейтайдеров.

ent-WallXenoborg = мехадермическая стена
    .desc = { ent-WallPlastitanium.desc }

ent-WallXenoborgDiagonal = мехадермическая стена
    .desc = { ent-WallPlastitaniumDiagonal.desc }
    .suffix = Диагональ
