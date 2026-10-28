# Procgen native map examples

Map images use the existing `Content.MapRenderer` pipeline. There is no separate procgen sprite,
SVG or gallery renderer. Native maps and bounded generation reports remain generator outputs.

Build and regenerate the five examples, verify their native map roundtrip, then render them:

```powershell
./Klovn/Tools/_KS14/Procgen/generate_showcases.ps1 -Configuration Release
```

Render an existing exported map directly:

```powershell
dotnet run --project Content.MapRenderer/Content.MapRenderer.csproj -c Release --no-build -- --files Resources/Maps/_KS14/ProcgenShowcases/wired-office.yml --wires-last --output Klovn/Docs/Examples/Procgen/Showcases/Native
```

`--wires-last` reveals installed cables through native appearance/subfloor handling and paints them
last, using their real client sprites and connection states. Loose cable coils retain ordinary
rendering. Omit the option for the normal native map view. `--viewer` enables the existing map
viewer's JSON output; `--markers` enables native mapping markers.

Images follow the map renderer's standard `<output>/<map>/<map>-<grid>.png` layout. Generation,
ownership, lifecycle checks, utility adapters and native map export remain procgen feature code.
Rendering does not certify access, power supply, gas stability or permission to publish a map.
