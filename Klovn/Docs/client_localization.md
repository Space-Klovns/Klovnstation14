# Client localization

Add Fluent files under `Resources/Locale/<culture>/`, using a canonical culture
identifier such as `fr-FR`, `pt-BR`, or `ja-JP`. Register a selectable game language
by translating `en-US/_KS14/Localization/options.ftl` into the same relative path
under the new culture. Registered cultures appear in the KS14 settings tab
automatically, displayed by their native names. Engine-only translations do not
register a game language. Both the client
and server need the translations for authoritative examine responses. The client
preference is saved in `klovn.client_locale`; English (`en-US`) is the fallback for
missing strings and unavailable or invalid culture preferences/requests.

Restart after installing a culture. Resource packs mounted during runtime can call
`ContentLocalizationManager.RefreshAvailableCultures()` to refresh discovery.
Reopen the settings tab to update its options.

Common content formatting functions are registered for every culture. Numbers use
that culture's number format. Non-English plural forms belong in Fluent select
expressions; the English plural suffix functions do not inflect other languages.

Translated guidebooks mirror their source paths below
`Resources/ServerInfo/_KS14/Guidebook/<culture>/`. For example,
`/ServerInfo/Guidebook/Example.xml` becomes
`Resources/ServerInfo/_KS14/Guidebook/fr-FR/Guidebook/Example.xml`.
Guidebooks try the selected culture and its parent cultures before using the
original document, so a `fr` guide can serve a `fr-FR` client.

Radio shortcuts use `ksRadioLocale` prototypes, as in
`Resources/Prototypes/_KS14/Radio/locales.yml`. Add a culture and a mapping from
canonical channel keys to unique local characters. Radio mappings also try parent
cultures (e.g. `zh-Hant-TW`, `zh-Hant`, `zh`); without a mapping, canonical keys
remain in use.

Nested prototype names in examine handlers must use
`ContentLocalizationManager.GetLocalizedPrototypeName`. This resolves names in
the recipient's culture, including abstract parents, while the engine's metadata
cache stays in the server's normal culture. `GetLocalizedEntityName` also preserves
custom item names and identities.
