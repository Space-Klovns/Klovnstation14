# Procgen examples

[Native map images and regeneration notes](Showcases/README.md) use `Content.MapRenderer`.
The separate procgen sprite compositor, SVG renderer and gallery are removed.

The native YAML maps in `Showcases` and `Resources/Maps/_KS14/ProcgenShowcases` are renderer inputs.
Diagnostic JSON files preserve generation reports and inputs; they are not drawing instructions or
proof of physical access, gas closure, operating lighting/power or permission to publish a map.

To regenerate the maps and native images, run:

```powershell
./Klovn/Tools/_KS14/Procgen/generate_showcases.ps1 -Configuration Release
```

The native map renderer's `--wires-last` view reveals installed cables using actual client appearance
state and draws them above floors and furnishings. Its ordinary rendering and `--viewer` output
remain available for other maps. Historical diagnostic digest records are retained as history;
obsolete custom PNG/SVG renderings have been removed.
