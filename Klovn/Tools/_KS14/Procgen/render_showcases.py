"""Render actual procgen showcase JSON as an offline sprite/diagnostic gallery."""
import argparse
import hashlib
import html
import json
from pathlib import Path

EXAMPLES = ["tiny-strip", "l-shape", "kitchenette", "gas-room", "wired-office"]
TITLES = ["Tiny 1 x 3 fill", "L-shaped footprint", "Supported kitchenette", "Initial atmosphere", "Automatic electrical plan"]
COLORS = {"High": "#fb7185", "Medium": "#fbbf24", "Low": "#38bdf8"}


def esc(value):
    return html.escape(str(value), quote=True)


def cell(value):
    return value["x"], value["y"]


def render(data):
    tiles = [cell(item["cell"]) for key in ("floors", "walls", "windows", "doors") for item in data[key]]
    left, right = min(x for x, _ in tiles), max(x for x, _ in tiles)
    bottom, top = min(y for _, y in tiles), max(y for _, y in tiles)
    width, height = (right - left + 1) * 48 + 60, (top - bottom + 1) * 48 + 60

    def xy(x, y):
        return 30 + (x - left) * 48, 30 + (top + 1 - y) * 48

    def center(value):
        x, y = cell(value)
        return xy(x + .5, y + .5)

    def rect(value, fill, stroke="#25354a"):
        x, y = cell(value)
        sx, sy = xy(x, y + 1)
        return f'<rect x="{sx}" y="{sy}" width="48" height="48" rx="3" fill="{fill}" stroke="{stroke}"/>'

    title = TITLES[EXAMPLES.index(data["id"])]
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" role="img" aria-label="{esc(title)}">',
             '<style>text{font:11px system-ui,sans-serif}.coverage{display:none}</style>',
             f'<rect width="{width}" height="{height}" rx="12" fill="#0b1422"/>']
    for index, room in enumerate(data["rooms"]):
        parts.extend(rect(tile, ["#1d3044", "#233349", "#273847"][index % 3]) for tile in room["cells"])
    for tile in data["floors"]:
        parts.append(f'<g><title>{esc(tile["tileId"])} / {esc(cell(tile["cell"]))}</title>{rect(tile["cell"], "transparent")}</g>')
    parts.append('<g class="gas">')
    for gas in data.get("initialAtmosphere") or []:
        parts.append(f'<g><title>{gas["pressureKPa"]:.3f} kPa / {gas["temperatureKelvin"]:.2f} K</title>{rect(gas["cell"], "#34d39955", "none")}</g>')
    parts.append('</g>')
    for key, fill in [("walls", "#6d7f95"), ("windows", "#98ddec"), ("doors", "#c5a36e")]:
        for item in data[key]:
            parts.append(f'<g><title>{esc(item["entityId"])}</title>{rect(item["cell"], fill, "#b5c3d155")}</g>')
    parts.append('<g class="paths" fill="none" stroke="#5eead4" stroke-width="3" stroke-linecap="round">')
    for access in data["approaches"]:
        points = " ".join(f"{x},{y}" for x, y in map(center, access["path"]))
        sx, sy = center(access["cell"])
        parts.append(f'<polyline points="{points}" stroke-dasharray="4 5"/><circle cx="{sx}" cy="{sy}" r="5" fill="#5eead4"/>')
    parts.append('</g><g class="coverage" fill="#38bdf811" stroke="#38bdf866" stroke-dasharray="4 6">')
    service = data.get("electricalService")
    if service:
        for key, fill in [("covered", "#34d39944"), ("uncovered", "#fb718566")]:
            for point in service[key]:
                parts.append(f'<g><title>LV floor service: {key}</title>{rect(point, fill, "none")}</g>')
    for consumer in data.get("consumers") or []:
        sx, sy = center(consumer["providerCell"])
        parts.append(f'<circle cx="{sx}" cy="{sy}" r="{data["transferRadius"] * 48}"/>')
    parts.append('</g><g class="entities">')
    for entity in sorted(data["entities"], key=lambda item: item["layer"] != "Floor"):
        if not entity["exposed"] or entity["layer"] == "Container":
            continue
        prototype = entity["prototypeId"]
        color, label, size = "#bb88e7", "M", 27
        if prototype == "Table":
            color, label, size = "#a88058", "T", 39
        elif "Substation" in prototype:
            color, label = "#f19b7c", "S"
        elif "APC" in prototype:
            color, label = "#81d7ac", "A"
        elif "Computer" in prototype:
            color, label = "#9bbcff", "C"
        elif "Microwave" in prototype:
            color, label, size = "#dcb5f7", "MW", 23
        sx, sy = xy(entity["x"], entity["y"])
        parts.append(f'<g><title>{esc(prototype)} / {esc(entity["role"])} / {esc(entity["layer"])} / turn {entity["quarterTurns"]}</title>')
        parts.append(f'<rect x="{sx-size/2}" y="{sy-size/2}" width="{size}" height="{size}" rx="5" fill="{color}" stroke="#090f19" stroke-width="2"/>')
        parts.append(f'<text x="{sx}" y="{sy+4}" text-anchor="middle" fill="#101826" font-weight="700">{label}</text>')
        if prototype != "Table":
            dx, dy = [(0, 1), (-1, 0), (0, -1), (1, 0)][entity["quarterTurns"]]
            parts.append(f'<path d="M{sx+dx*15},{sy+dy*15} L{sx+dx*21},{sy+dy*21}" stroke="#e9f3ff" stroke-width="3"/>')
        parts.append('</g>')
    parts.append('</g>')
    for x in range(left, right + 1):
        sx, _ = center({"x": x, "y": bottom})
        parts.append(f'<text x="{sx}" y="{height-10}" text-anchor="middle" fill="#8b9db4">{x}</text>')
    for y in range(bottom, top + 1):
        _, sy = center({"x": left, "y": y})
        parts.append(f'<text x="12" y="{sy+4}" text-anchor="middle" fill="#8b9db4">{y}</text>')
    # Keep cable diagnostics visible over every room and entity layer.
    parts.append('<g class="wires" fill="none" stroke-width="4" stroke-linecap="round">')
    for tier, color in COLORS.items():
        points = {cell(item["cell"]) for item in data.get("cables") or [] if item["voltage"] == tier}
        shift = {"High": -5, "Medium": 0, "Low": 5}[tier]
        for x, y in sorted(points):
            sx, sy = center({"x": x, "y": y})
            sy += shift
            parts.append(f'<circle cx="{sx}" cy="{sy}" r="3" fill="{color}" stroke="{color}"/>')
            for dx, dy in [(1, 0), (0, 1)]:
                if (x + dx, y + dy) in points:
                    ex, ey = center({"x": x + dx, "y": y + dy})
                    parts.append(f'<path d="M{sx},{sy} L{ex},{ey + shift}" stroke="{color}"/>')
    parts.append('</g>')
    return "".join(parts) + '</svg>'


PAGE = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>KS procgen / small showcases</title>
<style>*{box-sizing:border-box}body{margin:0;background:#080e18;color:#e3edf9;font:15px/1.5 system-ui,sans-serif}main{max-width:1280px;margin:auto;padding:40px 28px}h1{font-size:34px;letter-spacing:-.03em;margin:4px 0 8px}h2{font-size:21px;margin:0}p{margin:10px 0}.eyebrow{font-size:11px;letter-spacing:.15em;color:#83b9f7;font-weight:700;margin:0}.intro{max-width:850px;color:#9db0c8}.toolbar{position:sticky;top:0;z-index:2;padding:15px 0;background:#080e18ee;display:flex;gap:18px;flex-wrap:wrap;border-bottom:1px solid #26354b;margin-bottom:24px}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:22px}.card{background:#101a2a;border:1px solid #28384f;border-radius:16px;padding:22px;min-width:0}.card header{display:flex;gap:12px;align-items:start;justify-content:space-between}.badge{border:1px solid #3c5270;color:#b8cce7;font-size:11px;padding:5px 8px;border-radius:7px;max-width:170px}.facts{color:#9cb1cb;font-size:13px}.diagram{display:flex;justify-content:center;background:#0b1422;border-radius:12px;overflow:hidden;min-height:150px}.diagram svg{width:100%;max-width:510px;max-height:510px}.downloads{font-size:12px;line-height:2}a{color:#8ac5ff}details{border-top:1px solid #2b3a50;padding-top:12px;font-size:13px}summary{cursor:pointer;color:#b7cbe5}li span{display:block;color:#8eabc9;font-size:12px}code{font-size:11px;color:#94b1cf}.legend{display:flex;gap:18px;flex-wrap:wrap;color:#9db0c8;font-size:12px;margin:14px 0 24px}.notice{border-left:3px solid #ceaa69;padding-left:14px;color:#bcc5d4;font-size:13px;margin-top:28px}input{accent-color:#5eead4}label{cursor:pointer}@media(max-width:850px){.grid{grid-template-columns:1fr}main{padding:24px 16px}.card header{display:block}.badge{display:inline-block;margin-top:8px}}</style>
<style>[hidden]{display:none!important}.sprite-preview{display:flex;justify-content:center;align-items:center;background:#0b0f17;border-radius:12px;min-height:150px}.sprite-preview img{display:block;max-width:100%;max-height:510px;image-rendering:pixelated}.diagnostic-tools{display:flex;gap:18px;flex-wrap:wrap}select{background:#16263b;color:#e3edf9;border:1px solid #48617e;border-radius:5px;padding:4px 8px;font:inherit}</style>
<main><p class="eyebrow">KLOVNSTATION / PROCEDURAL GENERATION</p><h1>Small, inspectable showcases.</h1><p class="intro">Five seeded outputs from the actual KS planner, rendered with the game's own sprite assets. Switch to diagnostics to inspect paths, gas and wiring. Import the native grids to see full client lighting and simulation.</p>
<div class="toolbar"><label>View <select id="render-mode"><option value="sprites">Game sprites</option><option value="diagnostics">Diagnostics</option></select></label><span class="diagnostic-tools"><label><input type="checkbox" data-layer="entities" checked> Furniture</label><label><input type="checkbox" data-layer="paths" checked> Clean approaches</label><label><input type="checkbox" data-layer="wires" checked> Cable tiers</label><label><input type="checkbox" data-layer="gas" checked> Initial gas</label><label><input type="checkbox" data-layer="coverage"> LV coverage</label></span></div>
<div class="legend"><span>T / table, vault blocker</span><span>MW / supported microwave</span><span>C / computer</span><span>S / substation</span><span>A / APC</span><span style="color:#fb7185">HV</span><span style="color:#fbbf24">MV</span><span style="color:#38bdf8">LV</span><span style="color:#5eead4">Approach path</span></div><div class="grid">{{CARDS}}</div>
<p class="notice">Sprite previews use repository textures, static animation frames and full-bright rendering; they are not client screenshots. Normal previews hide under-floor cables; the office's wiring view draws cables above every floor, wall and furnishing. Stored items stay inside their containers. Actual machine use, stable atmosphere, operating electrical supply and publication remain uncertified. <a href="sprite-assets.json">Sprite sources and attribution</a>.</p></main>
<script>document.querySelectorAll('[data-layer]').forEach(input=>{const apply=()=>document.querySelectorAll('g.'+input.dataset.layer).forEach(group=>group.style.display=input.checked?'inline':'none');input.addEventListener('change',apply);apply()});const mode=document.querySelector('#render-mode');mode.value='{{DEFAULT_VIEW}}';const applyView=()=>{const sprites=mode.value==='sprites';document.querySelectorAll('.card').forEach(card=>{const preview=card.querySelector('.sprite-preview');if(preview)preview.hidden=!sprites;card.querySelector('.diagram').hidden=sprites&&!!preview});document.querySelector('.diagnostic-tools').hidden=sprites;document.querySelector('.legend').hidden=sprites};mode.addEventListener('change',applyView);applyView();</script></html>'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    directory = parser.parse_args().directory.resolve()
    sprite_manifest = directory / "sprite-assets.json"
    sprite_outputs = {item["path"]: item for item in json.loads(sprite_manifest.read_text(encoding="utf-8-sig"))["outputs"]} if sprite_manifest.exists() else {}
    cards = []
    sprite_count = 0
    for index, name in enumerate(EXAMPLES):
        data = json.loads((directory / (name + ".json")).read_text(encoding="utf-8"))
        assert data["kind"] == "KsProcgenShowcase" and data["id"] == name
        svg = render(data)
        (directory / (name + ".svg")).write_text(svg, encoding="utf-8")
        links = f'<a href="{name}.json">Diagnostics</a> / <a href="{name}.request.yml">Request</a> / <a href="{name}.svg">SVG</a>'
        if data["nativeGridLoadVerified"]:
            links += f' / <a href="{name}.yml">Importable grid</a>'
        if (directory / (name + ".content.yml")).exists():
            links += f' / <a href="{name}.content.yml">Room pack</a>'
        preview = ""
        sprite = sprite_outputs.get(name + "-sprites.png")
        if (sprite and sprite["sourcePlanHash"] == data["planHash"] and (directory / sprite["path"]).is_file()
                and hashlib.sha256((directory / sprite["path"]).read_bytes()).hexdigest() == sprite["sha256"]):
            sprite_count += 1
            preview = f'<div class="sprite-preview"><a href="{name}-sprites.png"><img src="{name}-sprites.png" alt="{esc(TITLES[index])}, using game sprites" width="{sprite["width"]}" height="{sprite["height"]}"></a></div>'
            links += f' / <a href="{name}-sprites.png">Sprite PNG</a>'
            if name == "wired-office":
                links += ' / <a href="wired-office-wiring.png">Under-floor wiring PNG</a>'
        status = "Initialized / paused / save-load checked" if data["nativeGridLoadVerified"] else "Electrical plan / unmaterialized"
        entities = "".join(f'<li>{esc(item["role"])}<span>{esc(item["prototypeId"])} / {esc(item["layer"])}</span></li>' for item in data["entities"])
        facts = f'{len(data["floors"])} floor tiles / {len(data["walls"])} walls / {len(data["entities"])} content members'
        pressure = data.get("initialAtmosphere") or []
        if data["windows"]:
            facts += f' / {len(data["windows"])} windows'
        if pressure:
            facts += f' / initial air {sum(item["pressureKPa"] for item in pressure)/len(pressure):.2f} kPa'
        if data.get("cables"):
            facts += f' / {len(data["cables"])} cable segments / circular LV reach {data["transferRadius"]} tiles'
        service = data.get("electricalService")
        if service:
            facts += f' / {len(service["covered"])}/{len(service["floorDomain"])} usable floor cells within LV reach / {len(service["rooms"])} electrical closet'
        cards.append(f'<section class="card" id="{name}"><header><div><p class="eyebrow">SEED {data["seed"]}</p><h2>{index+1:02d} / {TITLES[index]}</h2></div><span class="badge">{status}</span></header><p class="facts">{facts}</p>{preview}<div class="diagram">{svg}</div><p class="downloads">{links}</p><details><summary>Contents and evidence</summary><ul>{entities or "<li>Geometry only</li>"}</ul><p>{esc(data["notes"])}</p><code>Plan {esc(data["planHash"])}</code></details></section>')
    (directory / "index.html").write_text(PAGE.replace("{{CARDS}}", "".join(cards)).replace("{{DEFAULT_VIEW}}", "sprites" if sprite_count == len(EXAMPLES) else "diagnostics"), encoding="utf-8")
    print(f"Rendered {len(cards)} native-planner showcases in {directory}")


if __name__ == "__main__":
    main()
