# KS14: ported locale; references to unavailable source content pruned.

ent-BaseMobAnimal = { "" }
    .desc = { "" }
ent-OrganAnimal =
    .suffix = Животное
    .desc = { "" }
ent-OrganAnimalHeart = { ent-OrganBaseHeart }
    .desc = { ent-OrganBaseHeart.desc }
    .suffix = { ent-OrganAnimalInternal.suffix }
ent-OrganAnimalInternal =
    .desc = { ent-OrganAnimal.desc }
    .suffix = { ent-OrganAnimal.suffix }
ent-OrganAnimalKidneys = { ent-OrganBaseKidneys }
    .desc = { ent-OrganBaseKidneys.desc }
    .suffix = { ent-OrganAnimalInternal.suffix }
ent-OrganAnimalLiver = { ent-OrganBaseLiver }
    .desc = { ent-OrganBaseLiver.desc }
    .suffix = { ent-OrganAnimalInternal.suffix }
ent-OrganAnimalLungs = { ent-OrganBaseLungs }
    .desc = { ent-OrganBaseLungs.desc }
    .suffix = { ent-OrganAnimalInternal.suffix }
ent-OrganAnimalMetabolizer = { "" }
    .desc = { "" }
ent-OrganAnimalStomach = { ent-OrganBaseStomach }
    .desc = { ent-OrganBaseStomach.desc }
    .suffix = { ent-OrganAnimalInternal.suffix }
