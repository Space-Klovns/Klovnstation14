# CBURN
ent-RandomHumanoidSpawnerCBURNUnit = Агент РХБЗЗ
    .desc = { "" }
    .suffix = Роль ОБР
    .desc = { "" }

# misc
ent-RandomHumanoidSpawnerCentcomOfficial = Представитель ЦК
    .desc = { "" }

ent-RandomHumanoidSpawnerCluwne = Клувень
    .desc = { "" }
    .suffix = Спавнит клувеня

ent-RandomHumanoidSpawnerDeathSquad = Коммандос Эскадрона смерти
    .desc = { "" }
    .suffix = Роль ОБР, Эскадрон смерти

# ERT Chaplain
ent-RandomHumanoidSpawnerERTChaplain = ОБР священник
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ОБР, Базовый

ent-RandomHumanoidSpawnerERTChaplainEVA = ОБР священник
    .suffix = Роль ОБР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTChaplain.desc }

# ERT Engineer
ent-RandomHumanoidSpawnerERTEngineer = ОБР инженер
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ОБР, Базовый
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }

ent-RandomHumanoidSpawnerERTEngineerArmed = { ent-RandomHumanoidSpawnerERTEngineer }
    .suffix = Роль ОБР, Вооружен, ВКД
    .desc = Вооружен Силовиком, имеет детонационный шнур и коробку детонаторов.

ent-RandomHumanoidSpawnerERTEngineerEVA = { ent-RandomHumanoidSpawnerERTEngineer }
    .suffix = Роль ОБР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTEngineer.desc }

# ERT Janitor
ent-RandomHumanoidSpawnerERTJanitor = ОБР уборщик
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ОБР, Базовый
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }

ent-RandomHumanoidSpawnerERTJanitorEVA = ОБР уборщик
    .suffix = Роль ОБР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTJanitor.desc }

# ERT Leader
ent-RandomHumanoidSpawnerERTLeader = ОБР лидер
    .suffix = Роль ОБР, Базовый
    .desc = { "" }

ent-RandomHumanoidSpawnerERTLeaderArmed = { ent-RandomHumanoidSpawnerERTLeaderEVA }
    .suffix = Роль ОБР, Вооружен, ВКД
    .desc = Вооружен XL8, 4 запасных магазина разного типа.

ent-RandomHumanoidSpawnerERTLeaderEVA = ОБР лидер
    .suffix = Роль ОБР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }

# ERT Medic
ent-RandomHumanoidSpawnerERTMedical = ОБР медик
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ОБР, Базовый
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }

ent-RandomHumanoidSpawnerERTMedicalArmed = ОБР медик
    .suffix = Роль ОБР, Вооружен, ВКД
    .desc = Вооружен Лектером, 4 запасных магазина разного типа.

ent-RandomHumanoidSpawnerERTMedicalEVA = ОБР медик
    .suffix = Роль ОБР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTMedical.desc }

# ERT Security
ent-RandomHumanoidSpawnerERTSecurity = ОБР офицер безопасности
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ОБР, Базовый

ent-RandomHumanoidSpawnerERTSecurityArmedGrenade = { ent-RandomHumanoidSpawnerERTSecurityEVA }, Гренадер
    .suffix = Роль ОБР, Вооружен, ВКД
    .desc = Вооружен Гидрой с осколочными снарядами, имеет в запасе 6 фугасных, 3 ЭМИ и светошумовых снаряда.

ent-RandomHumanoidSpawnerERTSecurityArmedRifle = { ent-RandomHumanoidSpawnerERTSecurityEVA }, Стрелок
    .suffix = Роль ОБР, Вооружен, ВКД
    .desc = Вооружен Лектером, 4 запасных магазина различного типа, Лазерная пушка и переносной зарядник.

ent-RandomHumanoidSpawnerERTSecurityArmedShotgun = { ent-RandomHumanoidSpawnerERTSecurityEVA }, Сапёр
    .suffix = Роль ОБР, Вооружен, ВКД
    .desc = Вооружен Силовиком, 3 коробки различной дроби, осколочной гранатой, детонационным шнуром и коробкой детонаторов.

ent-RandomHumanoidSpawnerERTSecurityArmedVanguard = { ent-RandomHumanoidSpawnerERTSecurityEVA }, Авангард
    .suffix = Роль ОБР, Вооружен, ВКД
    .desc = Вооружен WT550, 4 запасных магазина, 3 телескопических щита.

ent-RandomHumanoidSpawnerERTSecurityEVA = ОБР офицер безопасности
    .suffix = Роль ОБР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTSecurity.desc }

ent-RandomHumanoidSpawnerNukeOp = Ядерный оперативник
    .desc = { "" }

ent-RandomHumanoidSpawnerSyndicateAgent = Агент Синдиката
    .desc = { "" }
