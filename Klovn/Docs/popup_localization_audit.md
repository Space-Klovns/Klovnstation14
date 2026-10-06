# Popup localisation audit

The energy shotgun exposed an incomplete migration: the outer popup carried a
Fluent key, but its selected projectile name was formatted before sending. This
audit searched the client, shared, and server C# trees, including fork code, for
direct popup calls, popup-only local variables, observer messages, nested label
formatters, prototype-name lookups, and custom popup helpers. Engine files were
read for context and remain unchanged.

## Migrated paths

- Emitter bolt selection now sends `KsPopupPrototypeName` and addresses the user
  choosing the verb, rather than treating the emitter as the player recipient.
- Suit sensors, injector modes, radio speaker states, jammers, pipe layers and
  their required tool labels, ghost-role selection, and stethoscope status retain
  their nested Fluent keys until the recipient formats them.
- Secret-stash names defer either the stash's configured Fluent label or its live
  identity entity. Verb labels retain their immediate formatting API.
- Eating messages defer edible verbs and flavour profiles. Multiple flavours use
  `KsPopupMessageList` so the individual labels and enclosing sentence are both
  translated by the recipient. Flavour collection, modification events, and
  ordering still happen once in the originating gameplay code.
- Regenerated organ names use their prototype IDs, avoiding a dependency on the
  newly spawned organ having already arrived at the client. Existing scene entities
  such as shields, devices, borgs, drones, and speczone targets use entity arguments
  where applicable, preserving live names instead of freezing a server string.
- More than 130 eager `Loc.GetString` calls in popup-only variables, nested labels,
  and observer messages were migrated, including medical interactions, blocking,
  wielding, implants, storage, changeling abilities, vending, paper, and devices.
- Cargo's `ConsolePopup` helper now accepts deferred messages. Packet configurator
  mode popups and magic-mirror hat warnings also use the deferred path.
- Interaction success/failure messages remain optional deferred messages. Anomaly
  host notifications and ghost/suicide command errors format their popup separately
  from their chat or command-shell output. Communication consoles defer the default
  shuttle error while retaining custom error strings.

These changes preserve popup filters and prediction/replay information. They do
not add resource scans, startup hooks, periodic work, or per-language server sends.
Joined messages add work proportional to the short list actually displayed.

## Remaining string paths and their context

These are separate from the selected-prototype bug; do not blindly replace every
string popup or assume these APIs carry recoverable translation keys:

| Path | Why it still contains strings |
| --- | --- |
| Client-only popups and `SharedGunSystem.Popup` | They format on the client. The server gun override is empty, so those gun-selector/revolver messages do not carry English server text to the client. |
| Custom error fields such as gun/wield/melee attempt messages, access denial reasons, item-toggle failure messages, RCD validation reasons, and cancelled injection overrides | Their producers hand over already formatted strings. Full recipient localisation requires changing the corresponding producer and event/helper contract together. Deferred default messages do not repair a custom string supplied by another handler. |
| Custom verb `Message` text | The server still sends the supplied formatted message. Verb names/categories follow the separate verb localisation request scope. |
| Viewer-sensitive `Identity.Name` arguments in cuffs and telefrag messages | These can intentionally include both true and disguised names for a ghost viewer. Replacing them with a single identity entity would alter that behaviour. |
| Chat text accompanying anomaly notifications | Only the popup was migrated; chat has its own protocol. |
| Administrative/player-entered text, damage totals, and numeric stack counters | Literal data, not Fluent message IDs. |
| `KsPopupSystem.PopupTargetAndUser` | Legacy string helper with no active callers found; its commented dismemberment example is not running code. Future translated callers need a deferred contract. |
| `PipeRestrictOverlapSystem` | The file's implementation is commented out; apparent legacy popup calls are inactive. |

For a new translated error path, retain the Fluent key and typed arguments at the
producer and through every intermediate event/helper. Do not try to reverse-map
an English sentence back to its key or switch the whole server's culture.
See [CONTRIBUTING.md §7](../../CONTRIBUTING.md#7-ks-client-localisation-popups-and-prototype-metadata)
for the API contract and cache rules.

## Manual checks

Use an English server and two clients with different selected languages:

1. Switch both energy shotgun ammo modes and an emitter's bolt type. Check the
   selected projectile name, and verify only the selecting user gets the private
   popup.
2. Change suit sensors, injector modes, radio speakers, jammer states, and pipe
   layers. Check the nested state/tool label as well as the outer sentence.
3. Listen with a stethoscope repeatedly. Check both absolute breathing status and
   improvement/worsening labels; confirm there is no duplicated prediction popup.
4. Eat food with one flavour and several flavours. Check every flavour and the
   join sentence. Exercise force feeding and eating blockers to check edible verbs.
5. Hide/retrieve an item in a named secret stash, use shields and paper, restock a
   vending machine, and trigger a medical or changeling interaction. Check private
   versus observer messages in each client's language.
6. Trigger cargo denial messages, packet configurator mode changes, interaction
   failures, and anomaly host notifications. Compare popup text to each client's
   culture; accompanying chat/console text has the separate limitations above.
7. Rename an item and use a disguised character. Check that custom names and
   identities survive. Check ghost viewers separately where both identities are
   intentionally shown.
8. Repeat representative predicted actions and switch languages during the session.
   Check for Russian/English double popups, repeated confirmation counters, and
   unexpected recipients. Repeat from packaged resources.

Automated coverage lives in `KsPopupLocalizationTests`, with real server-to-client
energy-shotgun, sensor, radio, injector, and multi-flavour paths, plus prediction
matching. Run the wider client/verb/cache localisation tests and sandbox test when
changing shared payload representations. Microbenchmarks measure component costs
and serialized payloads, not whole-round CPU or total network throughput.

Validation for this audit: all 51 selected popup/client/verb/cache/sandbox tests
passed in Debug; the shared project also built successfully in Release.
