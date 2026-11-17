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

Context-menu verb requests and execution requests carry the client's culture.
The server generates and validates verbs in a synchronous culture scope, restores
its normal culture, and then executes the selected action. This lets the menu merge matching actions and retain
the server's authoritative enabled state. Verb categories store localization keys
so static categories also follow the current language.

Client/server builds automatically generate
`Resources/Localization/_KS14/prototype-names.bin`. Both runtimes load these frozen
name and description tables without scanning Fluent resources or walking prototype inheritance.
The input fingerprint includes translations, prototype files, and the compiler;
unchanged inputs skip regeneration. The artifact and its fingerprint are ignored
by Git. `SkipKsLocalizationCache=true` disables the build hook when needed.

Client/server packaging (including automatic ACZ client packaging on the server)
recompiles the artifact from the package's exact inputs and includes it in the
resource package. The launcher transfers the client cache with the other resources;
no additional gameplay network message is needed. Culture registrations survive
Fluent directory merging through the artifact's culture list.

Selecting another language retains the precompiled tables. Unpackaged runs without
the artifact, new runtime languages, and prototype/localization hot reloads fall
back to runtime compilation. Rebuild or repackage to refresh the persistent artifact.
Both compilers share native-message indexing and inheritance/fallback rules.
Registered cultures and ordinary engine prototype metadata are loaded at startup.
Radio mappings use cached culture dictionaries and reverse character maps;
prototype reloads rebuild those maps, including newly installed languages.
Code that calls the engine's `ReloadLocalizations()` directly must also call
`ContentLocalizationManager.InvalidatePrototypeNameCache()`; the engine exposes no
localization reload event for content to subscribe to.

Translated popups use `KsPopupMessage.Create` from
`Content.Shared._KS14.PopupLocalization` with the existing popup methods. The
server sends the Fluent key and arguments; each client formats them in its own
culture before prediction matching. Pass entity handles instead of their already
formatted names. Nested labels must also remain translation keys:

```csharp
popup.PopupEntity(KsPopupMessage.Create("gun-selected-mode",
    ("mode", KsPopupMessage.Create("gun-SemiAuto"))), entity, recipient);
```

For prototype names, pass `new KsPopupPrototypeName(prototype.ID)` as the
argument. Both prediction and received popups resolve it through the recipient's
compiled prototype name table, including inherited names. Do not translate the
prototype name before sending it.

Literal/player-authored text still uses the string overloads. A string already
formatted elsewhere cannot recover its translation key; localized messages passed
through custom string-valued events or helpers must be migrated at their source.

`KsPopupLocalizationTests.MeasurePopupFormattingAndWireCosts` measures payload
construction, serialization/deserialization, client formatting, allocations, and
packet size. A Debug run with 2,000 iterations per case measured:

| Case | Receiver formatting | Localized/legacy packet size |
| --- | ---: | ---: |
| Static Anchorless message | 0.53 µs, 384 B allocated | 54 / 62 B |
| Two entity arguments with grammar | 16.84 µs, 7,096 B allocated | 86 / 46 B |
| Nested translated label | 1.82 µs, 1,528 B allocated | 67 / 25 B |

These are component microbenchmarks, excluding UI layout/rendering and full-round
load. Fluent formatting moves from the server to the recipient. Prediction formats
native arguments directly; zero-argument payloads omit the argument dictionary.
No resource scans or culture loads happen when displaying a popup.
