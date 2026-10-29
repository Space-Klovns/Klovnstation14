---
name: guidebook
description: How to write guidebook entries in this repo - player-facing XML documents under Resources/ServerInfo, the guideEntry prototype and where to hang it in the tree, the en-US and ru-RU copies and their mirrored paths, the markup that works (GuideEntityEmbed, GuideReagentEmbed, Box, ColorBox, Table, [textlink], [keybind], [protodata] with [GuidebookData]), and voice and accuracy rules. Use when adding, translating or changing any guidebook page, linking pages together, or showing live prototype numbers in one.
---

# Guidebook entries

Guidebook pages are **always player-facing**. Write for a player in the round who doesn't read the code: what a
thing does, how to use it, what goes wrong, and what to do about it. Never mention components, systems, cvars,
prototype ids or tests in the text.

Good examples: `Resources/ServerInfo/_KS14/Guidebook/Engineering/SensorArrays.xml` and `ChemicalFires.xml`, each
with its ru-RU copy.

## Files: one entry is four or five edits

| What | Where |
| --- | --- |
| English page | `Resources/ServerInfo/_KS14/Guidebook/<Department>/<Name>.xml` |
| Russian page | `Resources/ServerInfo/_KS14/Guidebook/ru-RU/_KS14/Guidebook/<Department>/<Name>.xml` |
| Prototype | `Resources/Prototypes/_KS14/Guidebook/<department>.yml` (`type: guideEntry`, `# KS14` after the type) |
| Title | `guide-entry-<kebab-name>` in `Resources/Locale/en-US/_KS14/guidebook/guides.ftl` and `ru-RU/.../guides.ftl` |
| Place in the tree | the parent's `children:` list |

- **Translated pages mirror the English path** under `_KS14/Guidebook/<culture>/`, minus `/ServerInfo/`. That
  includes upstream pages: `/ServerInfo/Guidebook/Engineering/Fires.xml` is translated at
  `_KS14/Guidebook/ru-RU/Guidebook/Engineering/Fires.xml`. The client falls back to English when no copy exists (see
  `Klovn/Docs/client_localization.md`).
- **Hang it in the tree.** An entry no parent lists only shows up from links. If the parent is upstream
  (`Resources/Prototypes/Guidebook/*.yml`), add the child with `# KS14` markers, as `Fires` lists `ChemicalFires`
  and `Power` lists `ArcFlash`. If it has no `children:` yet, add `children: # KS14` as well.
- Prototype fields: `id`, `name` (the loc id), `text` (the page path), and optionally `children`, `priority` (sort
  order) and `filterEnabled` (adds a search box).

```yaml
- type: guideEntry # KS14
  id: ChemicalFires
  name: guide-entry-chemical-fires
  text: "/ServerInfo/_KS14/Guidebook/Engineering/ChemicalFires.xml"
```

## Markup

A page is one `<Document>`, indented two spaces, with Markdown-ish headings (`#` for the title, which should match the
entry's name, and `##` for sections) and `- ` bullets. Blank lines separate paragraphs.

```xml
<Document>
  # Chemical Fires
  One-paragraph summary of what this is and why the player cares.

  <Box>
    <GuideEntityEmbed Entity="FireExtinguisher"/>
    <GuideEntityEmbed Entity="SheetSteel" Caption=""/>
  </Box>

  ## A section
  Text with [bold]emphasis[/bold], [color=orange]colour[/color], a [textlink="link" link="Fires"] and a key
  [color=yellow][bold][keybind="Use"][/bold][/color].
</Document>
```

- **`<GuideEntityEmbed Entity="ProtoId"/>`** shows a sprite and its name. `Caption=""` hides the name, and
  `Rotation`, `Scale`, `State` and `Margin` are also available. Group embeds in a `<Box>`, which lays them out in a
  row. Use concrete, spawnable prototypes.
- **`<GuideReagentEmbed Reagent="Id"/>`** shows a reagent card, and `<GuideReagentGroupEmbed Group="..."/>` shows a
  whole group.
- **`<ColorBox>`** and **`<Table>`** are for panels and grids. Look at upstream pages for their attributes before
  using them.
- **`[textlink="text" link="GuideEntryId"]`** links to another entry by its id, not its path.
- **`[keybind="BoundKeyFunction"]`** shows the player's own binding, so never hard-code a key.
- **`[protodata="ProtoId" comp="CompNameWithoutComponent" member="Field" format="N0"/]`** shows a live value from a
  prototype, so the page can't go stale after a balance pass. The member needs `[GuidebookData]`
  (`Content.Shared/Guidebook/GuidebookDataAttribute.cs`), and on server-only components the attribute has to be on
  something the client can read. Prefer this to writing a number into the text. If a number can't come from
  protodata (tile effects, reaction thresholds), describe it qualitatively instead ("hot enough", "a few seconds").
- An XML error or an unknown tag breaks the whole page. `GuideEntryPrototypeTests` parses every entry.

## Voice

- Second person, present tense, short sentences. Say what to do, then why.
- Be accurate. Read the prototypes and systems first, and describe what the code actually does, including the
  limits ("water does nothing to it"). Don't promise behaviour that isn't there.
- Explain mechanics through consequences the player can see: "the flames shrink as the foam wins", not "progress
  rises to 1".
- Tips go in a `## Tips` or inline `- [bold]Do this.[/bold] Because...` bullets.
- Use the in-game names from the locale files (`ent-*`, `reagent-name-*`) in both languages, so the guide matches
  what players see on examine. For Russian, check `Resources/Locale/ru-RU` for the existing term before inventing one.
- The Russian copy is a full translation of the English one, kept in step section for section, with the same embeds
  and links. When you change one, change both.

## Checking

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~GuideEntryPrototypeTests|FullyQualifiedName~KsClientLocalizationTests"
```

The test catches parse errors and missing entries, but not wrong embeds or bad layout. Open the guidebook in game
(both languages) to check those.
