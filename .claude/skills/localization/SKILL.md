---
name: localization
description: How text follows each client's language in this repo - KsPopupMessage and deferred popup arguments (KsPopupPrototypeName, KsPopupMessageList), popup audience and the recipient filter, prediction and network flow, ContentLocalizationManager for prototype names and descriptions, server culture scopes for examine and verb responses, ent- translation files and inheritance, the packaged prototype-name cache, adding cultures, and the localisation tests. Use when writing any popup, examine text, verb label or prototype name a player sees, adding or translating .ftl files, adding ent- entries, adding a culture, or when text shows in the wrong language, freezes the server's English, mixes languages, or a popup shows twice or not at all.
---

# Localisation

The server runs in `en-US`; each client picks its own culture (`klovn.client_locale`, KS14 settings tab). **Text
formatted on the server doesn't follow the recipient's language.** Two systems handle it: `KsPopupMessage` carries
unformatted popups to clients, and `ContentLocalizationManager` resolves prototype names and descriptions in the
active culture. Use both when a popup contains a prototype name. Keep fixes in content, never in `RobustToolbox`.
Culture, guidebook and radio setup: `Klovn/Docs/client_localization.md`.

## Writing messages

### One message, with selectors, not sentences stitched together

Put the whole sentence in one message and let Fluent pick the varying part. Gluing several messages together in C#
fixes the word order to English's, and a translator can't reorder, inflect or agree the pieces.

```ftl
# do this
gas-valve-system-examined = The valve is [color={$statusColor}]{$open ->
    [true]  open
   *[false] closed
}[/color].
```

```csharp
// do this
args.PushMarkup(Loc.GetString("gas-valve-system-examined", ("statusColor", color), ("open", valveComponent.Open)));

// not this
var state = Loc.GetString(valveComponent.Open ? "gas-valve-on" : "gas-valve-off");
args.PushMarkup(Loc.GetString("gas-valve-system-examined") + " " + state);
```

- A selector needs a default variant, marked `*`. Pass booleans, enums and strings as they are; booleans select on
  `[true]`/`[false]`, and enums on their lowercase names.
- **Numbers select by plural category**, with the culture's CLDR rules: English has `one` and `other`; Russian has
  `one`, `few`, `many` and `other`. Never pick the plural form in C#.

```ftl
ks-example-units-left = { $count ->
    [one] { $count } unit left
   *[other] { $count } units left
}
```

```ftl
ks-example-units-left = { $count ->
    [one] Осталась { $count } единица
    [few] Осталось { $count } единицы
   *[many] Осталось { $count } единиц
}
```

- Don't pre-format a name, pronoun or number into a string in C#: pass the entity or the number and use the functions
  below.

### Functions

Pass the **entity** (an `EntityUid`, or an `Entity<T>`), not its name, for anything grammatical. Names are written
`{ $user }`, not "the user".

| Function | Gives | Example |
| --- | --- | --- |
| `THE($ent)` | The name, with "the" unless it's a proper noun | `You pet { THE($target) }.` → "the cat" / "Ian" |
| `CAPITALIZE(...)` | Its argument with the first letter capitalised | `{ CAPITALIZE(THE($target)) } purrs.` |
| `INDEFINITE($ent)` | "a" or "an" for the entity's name, or for a plain string | `Requires { INDEFINITE($toolName) } { $toolName }` |
| `SUBJECT($ent)` | he / she / they / it | `{ CAPITALIZE(SUBJECT($target)) } looks pale.` |
| `OBJECT($ent)` | him / her / them / it | `You hug { OBJECT($target) }.` |
| `POSS-ADJ($ent)` | his / her / their / its | `{ POSS-ADJ($target) } claws` |
| `POSS-PRONOUN($ent)` | his / hers / theirs / its | `The bag is { POSS-PRONOUN($user) }.` |
| `REFLEXIVE($ent)` | himself / herself / themselves / itself | `{ THE($user) } hurts { REFLEXIVE($user) }.` |
| `CONJUGATE-BE($ent)` | is / are (are for epicene) | `{ CAPITALIZE(SUBJECT($ent)) } { CONJUGATE-BE($ent) } asleep.` |
| `CONJUGATE-HAVE($ent)` | has / have | `{ CAPITALIZE(SUBJECT($ent)) } { CONJUGATE-HAVE($ent) } no hands.` |
| `CONJUGATE-BASIC($ent, "base", "s-form")` | The verb agreeing with the entity | `{ SUBJECT($target) } { CONJUGATE-BASIC($target, "snap", "snaps") }` |
| `GENDER($ent)` | `male` / `female` / `epicene` / `neuter`, for a selector | `{ GENDER($ent) -> [female] … *[other] … }` |
| `PROPER($ent)` | `true` / `false`, for a selector | `{ PROPER($entity) -> [true] … *[false] … }` |
| `DAT-OBJ($ent)` | The dative pronoun (ему / ей / им); not used in English | `Вы даёте { DAT-OBJ($target) } яблоко.` |
| `GENITIVE($ent)` | The genitive pronoun (него / неё / них); not used in English | `у { GENITIVE($target) }` |
| `COUNTER($ent)` | The entity's counter word; for languages that use them | |
| `ATTRIB($ent, "name")` | One of the entity's localisation attributes (below), or `other` | `{ ATTRIB($ent, "gender") -> … }` |
| `ESCAPE(...)` | Its argument with markup escaped, for player text inside markup | `[bold]{ ESCAPE($name) }[/bold]` |
| `ESCAPE-PARAM(...)` | Its argument escaped for use as a markup *parameter* | `[color={ ESCAPE-PARAM($color) }]` |
| `LOC("key")` | Another message, by id; named arguments pass through | `Use a { LOC($tool) }.` |
| `MANY("word", $count)` | The word, pluralised unless the count is 1 (English only; other cultures get it unchanged) | `{ $time } { MANY("second", $time) }` |
| `MAKEPLURAL("word")` | The word's English plural (English only) | |
| `NATURALFIXED($number, places)` | The number with up to `places` decimals, trailing zeroes dropped, in the culture's format | `below { NATURALFIXED($maxTemp, 2) }K` |
| `NATURALPERCENT($fraction, places)` | A fraction as a percentage, likewise | `{ NATURALPERCENT($ratio, 1) }` |
| `TOSTRING($value, "format")` | A .NET format string applied, in the culture | `{ TOSTRING($percent, "P2") }` |
| `PRESSURE($kpa)` | A pressure with a scaled unit (kPa, MPa…) | `{ PRESSURE($pressure) }` |
| `POWERWATTS($watts)`, `POWERJOULES($joules)` | Power, or energy, with a scaled unit | `Supply: { POWERWATTS($supply) }` |
| `ENERGYWATTHOURS($joules)` | Energy in watt-hours, scaled | |
| `UNITS("type", "format", $value)` | A value in one of the unit types in `Units.Types` | |
| `PLAYTIME($time)` | A `TimeSpan` as hours and minutes | `Overall Playtime: { PLAYTIME($time) }` |

- Grammar functions read the entity's `GrammarComponent` first (gender, proper noun), then its localisation
  attributes. **Localisation attributes are the attributes of its `ent-` entry**, other than `.desc` and `.suffix`:
  `.gender`, `.proper`, `.indefinite`, `.counter`, or anything `ATTRIB` asks for.
- In Russian a noun's gender decides the pronoun and the adjective's ending, and an entity with neither a
  `GrammarComponent` nor a `.gender` attribute counts as **neuter**. Give an `ent-` entry `.gender = male`, `female` or
  `epicene` when the Russian noun isn't neuter and a message uses its pronouns.
- The engine helpers live in `RobustToolbox/Robust.Shared/Localization/LocalizationManager.Functions.cs` (with
  `zzzz-*` messages in each culture's `_engine_lib.ftl`); the unit and number ones in
  `Content.Shared/Localizations/ContentLocalizationManager*.cs`. Grep them before inventing a new one.

## Popups

### Pass the key and its arguments, not text

```csharp
using Content.Shared._KS14.PopupLocalization;

_popupSystem.PopupEntity(KsPopupMessage.Create("anchorless-convert-begin-message"), entityUid, userUid, type: PopupType.Small);

// Entity arguments keep what Fluent's entity functions need.
_popupSystem.PopupEntity(
    KsPopupMessage.Create("gun-ballistic-transfer-invalid", ("ammoEntity", ammoUid), ("targetEntity", targetUid)),
    targetUid, userUid, type: PopupType.Small);
```

- Argument names match the Fluent variables. Add the message to `en-US` and every translated locale, in the feature's
  existing file layout.
- **Never pass `Loc.GetString(...)`** to a popup meant to follow the client: it selects the string overload, which sends
  formatted text. String overloads are for literal and player-written text only. Don't call `message.Format(...)`
  first either.
- Keep events, helpers and variables that carry a translated popup typed `KsPopupMessage`. A `string` parameter can't
  recover the key and arguments.
- **The whole argument tree stays deferred**, nested values included:

```csharp
_popupSystem.PopupEntity(KsPopupMessage.Create("gun-selected-mode", ("mode", KsPopupMessage.Create("gun-SemiAuto"))), weaponUid, userUid);
_popupSystem.PopupEntity(KsPopupMessage.Create("gun-set-fire-mode-popup", ("mode", new KsPopupPrototypeName(projectilePrototype.ID))), weaponUid, userUid);
```

| Value the message needs | Pass |
| --- | --- |
| Live entity, incl. Fluent entity grammar | `EntityUid` or `Entity<T>`; not a preformatted `Name(...)` |
| Prototype name, no live entity | `new KsPopupPrototypeName(prototype.ID)` |
| Another translated label | `KsPopupMessage.Create(labelKey, ...)` |
| Several labels joined into one argument | `new KsPopupMessageList(separator, messages)`, each a deferred `KsPopupMessage` |
| Number, bool, time, literal or player text | The original value; let Fluent format numbers |
| Colour or enum | `Color` or the enum; sent as hex or lowercase invariant text |

- `KsPopupPrototypeName` resolves at the recipient, through the prototype name table and its inheritance; an unknown
  id shows as the id. Not `prototype.Name` or an eager `GetLocalizedPrototypeName(...)` (both freeze the sender's
  language), and not `KsPopupMessage.Create("ent-" + id)` (skips inheritance and `localizationId`).
- The payload also unwraps `LocValueString` and `LocValueNumber`. Anything else is sent as `ToString()`: no components,
  prototypes or custom objects. A new argument kind needs its own representation and tests.
- Entity arguments travel as `NetEntity`, not a name snapshot, so the client must have the entity.
- `KsPopupMessage.ToLower()` lowercases after formatting, with plain `string.ToLower()` (thread casing rules, no
  inflection). Prefer Fluent messages written for the casing you need.

### Audience and prediction

**`recipient` is a filter, not a prediction hint**: whoever it names is the only one who sees the popup.

| Call | Who sees it |
| --- | --- |
| `PopupEntity(message, entityUid)` | Everyone in the entity's PVS |
| `PopupEntity(message, entityUid, recipientUid)` / `(…, recipientSession)` | That one player |
| `PopupEntity(message, entityUid, filter, recordReplay)` | The filter |
| `PopupCoordinates(message, coordinates)` | Everyone in the coordinates' PVS |
| `PopupCursor(message, recipientUid)` | That one player |

- In shared code, call these directly: the predicting client shows the popup at once, and the server's copy is matched
  against it rather than shown twice; everyone else gets the server's. Entity and coordinate methods also take filters
  and sessions.
- The two-message `PopupEntity` sends one message to the recipient and another to observers. Both must be
  `KsPopupMessage`; the mixed overload takes a literal observer message, or `null` for none.
- `PopupPredicted`, `PopupPredictedCursor`, `PopupPredictedCoordinates` and `PopupClient` are `[Obsolete]` wrappers.
  Migrating, keep the audience: `PopupPredicted(message, uid, recipient)` showed everyone, so it becomes
  `PopupEntity(message, uid)`; `PopupClient(message, uid, recipient)` showed only the recipient, so it becomes
  `PopupEntity(message, uid, recipient)`. Keep the filter, type, replay choice, coordinates and prediction key too.

**Flow.** The server (`PopupSystem.Klovn.Localization.cs`) sends one `KsPopupPayload` in a `KsLocalizedPopupEvent`,
with location, type, tick and coordinate prediction key, unformatted, not one copy per language. The client formats
it in its culture and feeds the ordinary popup handlers. Prediction formats the original arguments locally on the first
predicted run, with no round trip. The client drops a network popup whose anchor isn't available, and
`KsPopupPayload.TryFormat` rejects missing or terminating entity arguments, nested ones included. Never pass
`EntityUid.Invalid` to Fluent's entity functions.

Formatting comes before the prediction match, so predicted and confirmed text agree across languages. **Use the same
key, arguments, audience and prediction identity on both paths**: that's what the engine's duplicate suppression
matches on. It doesn't dedupe different ticks, locations, keys or arguments; don't fix a duplicate with a second send
or a per-language broadcast.

## Prototype names and descriptions

```csharp
[Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;

var name = _contentLocalizationManager.GetLocalizedPrototypeName(prototype);
var description = _contentLocalizationManager.GetLocalizedPrototypeDescription(prototype);

// A live item keeps its custom name rather than the prototype's.
var displayName = _contentLocalizationManager.GetLocalizedEntityName(itemUid, EntityManager, MetaData(itemUid).EntityName);

// Examine text is formatted now, inside the request's culture scope.
args.PushMarkup(Loc.GetString("gun-set-fire-mode-examine", ("mode", name)));
```

- These use the **active** culture, not the entity's owner or any recipient. On the client that's the player's choice.
  The server's examine and verb request handlers already open a validated, synchronous
  `BeginCultureScope(requestedCulture)`. A new authoritative response path must carry and validate the client's
  culture and scope its formatting the same way:

```csharp
using (_contentLocalizationManager.BeginCultureScope(requestedCulture))
{
    // This synchronous response only.
}
```

- The scope swaps the manager's default culture until disposed. Never hold it across `await`, background work,
  queued callbacks or unrelated actions. Verb *actions* run after their validation scope is gone; popups use deferred
  messages, not a scope.
- The engine's metadata cache is warmed in the server's culture at startup. Don't rely on `prototype.Name`,
  `prototype.Description` or `Loc.GetEntityData(...)` switching per request, and don't clear or rewrite global engine
  metadata for one response: it mixes languages and affects other players.
- The base examine description is translated only while the live description still equals the prototype's; a runtime
  change is kept. Do the same in any new description path.

## Translation files

- **Every new prototype gets an `ent-<ExactId>` entry in `en-US` and `ru-RU`**, with `.desc` for the description and
  the prototype's path as a comment above it. A `localizationId`, inherited or not, overrides the key. Ids are case
  sensitive.
- Fork translations live under `Resources/Locale/<culture>/_KS14/`, mirroring the prototype paths, not one big file per
  module. Translated upstream trees are extended in place.

```ftl
# Resources\Prototypes\_KS14\Entities\Objects\Devices\example.yml
ent-KsExampleDevice = пример устройства
    .desc = Описание устройства.
```

- **English upstream names live in YAML, not Fluent**: an en-US `{ ent-SomeUpstreamId }` reference fails. Write the
  English text out.
- Names and descriptions resolve separately, by one policy shared between runtime and packaging
  (`KsPrototypeLocalizationResolver`):
  1. the prototype's own native translation, then its parents breadth first in declared order, abstract ones included;
  2. a concrete prototype's own distinct literal stops the walk, so a child with its own English name needs its own
     translation;
  3. a native inherited translation beats an English Fluent fallback when no literal stopped the walk;
  4. otherwise the English Fluent field, the resolved literal, or empty. An explicitly empty translation
     (`.desc = { "" }`) is a valid override.
- The English API keeps the engine's `prototype.Name` behaviour. Don't write another parent walk, and don't use
  `Loc.HasString(...)` to test for a *native* translation: an English fallback hides a missing one.
  `KsFluentMessageIndex` records native presence for the resolver.
- Prototype metadata is static text: put per-use variables in a separate examine or popup message.

## The packaged cache

- `MSBuild/KsLocalizationCache.targets` compiles `Resources/Localization/_KS14/prototype-names.bin` (per-culture name
  and description tables, plus the culture list) after normal client and server builds. It and its fingerprint are
  git-ignored: don't edit or commit them. Unchanged inputs skip it.
- `-p:SkipKsLocalizationCache=true` skips it for tooling, and leaves the cache stale: rebuild without it before running
  the localisation tests, or `PackagedMetadataMatchesRuntimeResolver` fails with nothing wrong.
- Packaging recompiles from the package's exact inputs (replacements included) into client, server and ACZ packages;
  clients get it with the normal resource download.
- `KsEntryPoint.PostInit()` loads it after prototype replacement, into frozen dictionaries: lookups don't scan files,
  walk inheritance or rebuild. Switching language keeps loaded tables. Keep expensive work in compile, init or explicit
  reloads; never per lookup, popup or recipient. Popup formatting still does Fluent work and allocates, so keep
  argument trees small and audiences tight.
- Without the artifact, or for a culture missing from it, a runtime table is built on demand and kept; abstract
  prototypes outside the tables use memoised runtime translations. Runtime and packaging must share the resolver and
  presence rules, or development and packaged builds translate differently.

## Cultures and reloads

- A new culture goes under `Resources/Locale/<canonical-culture>/`, with `_KS14/Localization/options.ftl` translated to
  register it. Use `GetAvailableCultures()` / `ResolveCulture(...)`, never an English-or-Russian branch. English is the
  fallback for unknown cultures and missing messages. Client and server both need the resources; every registered
  culture loads at startup.
- A resource pack mounted at runtime: call `RefreshAvailableCultures()` and reopen settings.
- Prototype reloads and content language refreshes invalidate the tables by themselves. Code that calls the engine's
  `ReloadLocalizations()` directly must also call `InvalidatePrototypeNameCache()` (the engine raises nothing). Never
  invalidate on ordinary lookups or popups.

## Verifying

Tests in `Content.IntegrationTests/Tests/_KS14/Localization/`:

| Test | Covers |
| --- | --- |
| `KsPopupLocalizationTests` | Recipient cultures, nested prototype labels, prediction confirmation, payload formatting and size |
| `KsPrototypeNameCacheTests` | Packaged/runtime parity, resolver behaviour |
| `KsClientLocalizationTests` | Culture discovery and switching, prototype metadata, nested examine text |
| `KsVerbLocalizationTests` | Verb labels, action culture boundaries |

Plus `SandboxTest` whenever you add an argument type or cache API. For a changed message, check an English server with
clients in two cultures, a predicted action and its confirmation, nested labels, private and broadcast audiences,
custom item names, and a missing translation's fallback. When changing resolution, check packaged resources as well as
the runtime fallback. The popup microbenchmarks measure single components, not whole-round cost.
