-edible-satiated = { $satiated ->
    [true] { " " }Вам кажется вы не можете больше { $verb }.
    *[false] { "" }
}

edible-force-feed = { CAPITALIZE($user) } пытается заставить вас что-то { $verb }!

edible-force-feed-success = { CAPITALIZE($user) } заставил вас что-то { $verb }! { $flavors }{ -edible-satiated(satiated: $satiated, verb: $verb) }

edible-force-feed-success-user = Вы успешно накормили { $target }

edible-gulp = Глоть. { $flavors }

edible-gulp-other = Глоть.

edible-has-used-storage = Вы не можете { $verb } { $food }, пока внутри что-то есть.

edible-nom = Ням. { $flavors }{ -edible-satiated(satiated: $satiated, verb: "есть") }

edible-nom-other = Ням.

edible-noun-drink = напиток

edible-noun-edible = съедобное

edible-noun-food = еда

edible-noun-pill = таблетка

edible-slurp = Сёрб. { $flavors }{ -edible-satiated(satiated: $satiated, verb: "пить") }

edible-slurp-other = Сёрб.

edible-swallow = Вы проглатываете { $food }.{ -edible-satiated(satiated: $satiated, verb: "проглотить") }

edible-verb-drink = пить

edible-verb-edible = поглощать

edible-verb-food = есть

edible-verb-pill = глотать

ingestion-cant-digest = Вы не сможете переварить { $entity }!

ingestion-cant-digest-other = { CAPITALIZE(SUBJECT($target)) } не сможет переварить { $entity }!

ingestion-other-cannot-ingest-any-more = { CAPITALIZE(SUBJECT($target)) } не может больше { $verb }!

ingestion-remove-mask = Сперва снимите { $entity }.

ingestion-try-use-is-empty = { CAPITALIZE($entity) } пуст!

ingestion-try-use-wrong-utensil = Вы не можете { $verb } { $food } с помощью { $utensil }.

ingestion-verb-drink = Пить

ingestion-verb-food = Есть

ingestion-you-cannot-ingest-any-more = Вы не можете больше { $verb }!

ingestion-you-need-to-hold-utensil = Вам нужна { $utensil }, чтобы есть это!
