# DeepL: holopad relays, AHelp, and reconnects

Ported from Black Space commit `8a52e01e0f89954da14754986879b9c5a046a103`.
The translation setup window from `9d9ca7e906` is outside this port.

## Behavior

Holopad relays retain the original player's identity and clear text before
accents. The hologram remains the visible speaker and the source of hearing
and fictional-language checks. Muffled microphone deliveries carry no clear
text to translate. Group broadcasts update nearby speech bubbles without
adding hidden messages to chat history or translating for distant observers.
Hidden broadcasts retain their formatted template before client highlights
and role codewords are applied. Translation replaces the body in that template
and reapplies the same decoration once, preserving server formatting even when
a highlighted word splits the original body into markup fragments.

AHelp sends the original message immediately and replaces its existing row
when a translation becomes available. Replacement preserves the timestamp,
sender prefix, administrator name override, unread count, sound behavior, and
window state. The blue `»` marker shows the original body. Both original and
translated user text remain escaped.

AHelp shares the existing translator, glossary, limits, cooldown, budget, and
request queue. Its fixed support context and separate cache scope prevent
private conversations from entering ordinary chat context. The server creates
translation metadata and checks recipient permissions again before delivering
an asynchronous result, including `AdminOnly` access.

Disconnecting clears pending results and private AHelp UI state. Retained chat
rows lose their server-local message IDs; their original text is preserved in
the marker itself, so a new connection can safely reuse those IDs.

## Source layout and upstream maintenance

Standalone additions live under `_KS14/Translation/`. Extensions of existing
upstream classes use same-folder `*.Klovn.*.cs` partial files. Necessary hooks
in upstream files retain Black Space provenance comments. No engine, DeepL
backend, API-key configuration, or onboarding changes are included.

When Black Space receives this port back through a Klovn upstream merge, it
must first align its existing translation helpers with the canonical paths
and identifiers below. Otherwise both partial implementations survive and
the shared hooks conflict. This alignment changes structure and references;
Black Space's own speech rules and holopad prototypes remain in place.

| Legacy Black Space helper | Canonical Klovn location |
| --- | --- |
| Client `AHelpUIController.BSTranslation.cs` | `Content.Client/UserInterface/Systems/Bwoink/AHelpUIController.Klovn.Translation.cs` |
| Client `BSBwoinkTranslatedMarkerTag.cs` | `Content.Client/_KS14/Translation/KsBwoinkTranslatedMarkerTag.cs` |
| Client `BwoinkPanel.BSTranslation.cs` | `Content.Client/Administration/UI/Bwoink/BwoinkPanel.Klovn.Translation.cs` |
| Client `ChatUIController.BSHiddenTranslation.cs` | `Content.Client/UserInterface/Systems/Chat/ChatUIController.Klovn.HiddenTranslation.cs` |
| Client `ChatUIController.BSTranslationSession.cs` | `Content.Client/UserInterface/Systems/Chat/ChatUIController.Klovn.TranslationSession.cs` |
| Server `BwoinkSystem.BSTranslation.cs` | `Content.Server/Administration/Systems/BwoinkSystem.Klovn.Translation.cs` |
| Server `KsTranslationSystem.BSAHelp.cs` | `Content.Server/_KS14/Translation/KsTranslationSystem.AHelp.cs` |
| Shared `BSBwoinkTranslationEvent.cs` | `Content.Shared/_KS14/Translation/KsBwoinkTranslationEvent.cs` |
| Shared `SharedBwoinkSystem.BSTranslation.cs` | `Content.Shared/Administration/SharedBwoinkSystem.Klovn.Translation.cs` |

Legacy files are under each project's `_BS/_BS/Translation/` directory.
Update translation-specific `BS*`, `_bs*`, and `bs*` references to their `Ks*`,
`_ks*`, and `ks*` equivalents; relay text parameters use
`ksRelayedTranslationText`, and the retained marker uses `OriginalParam`.
Keep unrelated Black Space symbols unchanged. Move administrator/user AHelp
replacement members into the new partial extension. Keep Black Space's own
relay/session tests with updated references, including their BS-prototype cases.

The first dry merge of this port into the recorded Black Space base added ten
conflicting paths to thirteen existing conflicts. Applying the prepared
alignment removed all ten additions and the duplicate production helpers.
The alignment was applied to Black Space on 2026-10-17. A second audit using
both actual working trees, including this document and the adaptation record,
confirmed the same thirteen conflicts. A follow-up review added the shared
`ChatUIController.Klovn.TranslationFormatting.cs` partial to both trees; there
are now ten identical canonical helpers, including the nine aligned files.
The thirteen existing conflicts are outside translation.
This is evidence for the recorded revisions, not a guarantee about future
unrelated changes. Track the adoption in `UPSTREAM-BS-TRANSLATION-001` in Black
Space's adaptation registry.

## Validation

`KsTranslationRelayTest` covers authenticated AHelp delivery, access revocation,
private cache/context separation, both event arrival orders, real holopad
calls, clear/muffled/unknown-language speech, and hidden broadcasts.
Hidden-broadcast cases cover both arrival orders with and without body
highlights, retained server formatting, and duplicate translation results.
`KsTranslationSessionTest` reconnects the client and checks reused visible and
hidden message IDs, retained tooltips, pending-result cleanup, and AHelp
ownership. `KsTranslationDisconnectTest` reproduces the original cleanup
defects without relying on the newly introduced APIs.

Use these tests together with `KsTranslationTests`,
`KsTranslationBubbleTests`, and `Utility.SandboxTest`. Build Release with
analyzers enabled and run the tests in Debug as required by `CONTRIBUTING.md`.
Tests use `FakeKsTranslator`; they do not validate live DeepL service behavior.
Current holopad tests cover whisper output on ordinary calls and speak output
on group broadcasts. The existing whisper/broadcast restriction is unchanged.
