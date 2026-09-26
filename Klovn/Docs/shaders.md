# Shaders

How shaders work in this codebase: where they live, what the engine does with them, which built-ins you get, and
the traps that fail silently. Engine paths below are under `RobustToolbox/Robust.Client/Graphics/`.

## The two files

A shader is a source file plus a prototype.

**Source**: `.swsl` under `Resources/Textures/` (fork shaders in `Resources/Textures/_KS14/Shaders/`). SWSL is
GLSL ES 1.0-ish with a small header language on top.

**Prototype**: `type: shader` under `Resources/Prototypes/` (fork ones in `Resources/Prototypes/_KS14/Shaders/`):

```yaml
- type: shader
  id: KsBarGlitch
  kind: source                 # 'source' = your .swsl. 'canvas' = the engine's default sprite shader,
  path: "/Textures/_KS14/Shaders/bar_glitch.swsl"   # optionally with light_mode/blend_mode set in YAML.
  params:                      # Default uniform values. A name that isn't a uniform in the .swsl is logged
    barCount: 5.0              #     as an error and skipped - it does not fail the load.
    barRegion: 0,1             # vec2 is "x,y". vec4 is a colour ('#RRGGBBAA') or "x,y,z,w".
  stencil:                     # Optional. See Shaders/StencilEnums.cs and Prototypes/Shaders/stencils.
    ref: 1
    op: Keep
    func: NotEqual
```

Code gets the prototype by id: `ProtoMan.Index<ShaderPrototype>(id)`.

## What a .swsl looks like

```glsl
light_mode unshaded;      // Optional header lines, before anything else:
blend_mode add;           //   light_mode unshaded      - skip the light map (see below)
preset raw;               //   blend_mode mix|add|subtract|multiply|none
                          //   preset raw               - use the raw wrapper instead of the default one

uniform highp float speed;          // Settable from params: and from code.
uniform highp float c = 0.3;        // Scalar initialisers work.
varying highp vec2 myVarying;       // Passed from vertex() to fragment().

highp float helper(highp float x) { return x * 2.0; }   // Ordinary functions are fine.

void vertex() {        // Optional.
    myVarying = tCoord2;
}

void fragment() {      // Required. Write COLOR.
    COLOR = zTexture(UV);
}
```

### What the engine does with it

`Clyde/Clyde.Shaders.cs` pastes your file into a wrapper: `Clyde/Shaders/base-default.vert` and
`base-default.frag`, or the `base-raw.*` pair under `preset raw`. The shared helper library
`Clyde/Shaders/z-library.glsl` goes in front of both.

**`fragment()` and `vertex()` are not called. Their bodies are pasted into the wrapper's `main()`.** That has
consequences:

- **Never `return` early from `fragment()`.** The wrapper writes `gl_FragColor` *after* your code. A `return`
  skips that, and the pixel comes out as undefined garbage. Use `if`/`else` and assign `COLOR` instead.
- Anything declared in `main()` is in your scope, including `LIGHT`, `MODULATE` and `FRAGCOORD`.

After your code, the default wrapper does `gl_FragColor = zAdjustResult(COLOR * MODULATE * LIGHT)`:

- `MODULATE` is the sprite or draw colour.
- `LIGHT` is the light map sample. `light_mode unshaded` makes it `vec4(1.0)`.
- The raw wrapper does neither.

If compilation fails, the client writes the complete, spliced GLSL to `error.glsl` in its working directory. Read
that rather than your `.swsl`: the line numbers in the error refer to it.

## Built-ins

In `fragment()`:

| Name | What it is |
| --- | --- |
| `COLOR` | Output colour. Write it. |
| `TEXTURE` | The texture being drawn. For an atlased sprite, this is **the whole atlas**. |
| `UV` | Texture coordinates **in `TEXTURE`**, so for an atlased sprite they cover only a small sub-rectangle, not 0-1. |
| `UV2` | Coordinates **in the quad being drawn**: 0-1 across the quad whatever the texture. `(0,0)` is bottom-left. |
| `SCREEN_UV` | Coordinates in the viewport. For post-shaders, still relative to the real viewport. |
| `FRAGCOORD` | `gl_FragCoord`. |
| `TEXTURE_PIXEL_SIZE` | `1 / size` of `TEXTURE` (the atlas, for sprites). |
| `SCREEN_PIXEL_SIZE` | `1 / size` of the current render target. |
| `TIME` | Seconds of real time. The same for every shader, every frame. |
| `LIGHT`, `MODULATE` | See above. |

In `vertex()`: `VERTEX` (clip-space position, writable), `tCoord` (texture coordinate, already mapped into the
atlas), and `tCoord2` (quad coordinate; `UV2` in the fragment is this).

Helpers from `z-library.glsl`:

- **Sampling.** `zTexture(uv)` samples `TEXTURE` and handles sRGB. `zTextureSpec(sampler, uv)` does the same for
  any other sampler. Use these, not `texture2D(TEXTURE, ...)`.
- **Noise.** `zRandom(vec2)` returns a pseudo-random `vec2` in -1..1. `zNoise(vec2)` is smooth value noise in
  0..1. `zFBM(vec2)` is fractal noise.
- **sRGB.** `zFromSrgb` and `zToSrgb`.
- **Uniform-name capabilities.** `#ifdef HAS_DFDX` means `dFdx`, `dFdy` and `fwidth` are available. It is not
  guaranteed, so write an `#else` fallback. See `Textures/Shaders/cooldown.swsl`.

### UV versus UV2: the one that bites

`UV` addresses the texture, `UV2` addresses the quad. For an atlased sprite they are different spaces:

- To lay something out across a sprite (bars, a gradient, a map), use `UV2`.
- To read the sprite's pixels, use `UV`.
- To offset a sample by "a tenth of the sprite's width", convert quad space to texture space.

The texture rectangle is baked into the vertex data, so no uniform tells you how big it is. Measure it with
derivatives instead. This is how `_KS14/Shaders/bar_glitch.swsl` does it:

```glsl
#ifdef HAS_DFDX
    highp float uvPerQuad = length(vec2(dFdx(UV.x), dFdy(UV.x))) / max(length(vec2(dFdx(UV2.x), dFdy(UV2.x))), 0.000001);
#endif
```

`TEXTURE_PIXEL_SIZE` is not a substitute. It is per atlas texel, not per sprite. In a post-shader, one texel is one
screen pixel, so the value changes with zoom.

## Instances, and uniforms that leak

- `prototype.Instance()` returns a **shared, immutable** instance. `SetParameter` on it throws.
- `prototype.InstanceUnique()` returns your own mutable copy. You own it: `Dispose()` it when finished.
- `instance.SetParameter(name, value)` sets a uniform.
  - Textures go in as `Texture`.
  - `vec4` takes a `Color` or a `Vector4`.
  - Setting a uniform that doesn't exist fails silently.

**Uniform values belong to the GL program, not to the instance**, and Clyde uploads nothing for an instance with no
parameters of its own. So an instance that never sets a uniform draws with whatever the last instance of that
shader wrote. This is not hypothetical:

- Lava sinking once shared a shader with upstream's floor occlusion.
- A mob that had finished sinking into lava set the shader's alpha to 0.
- Every mob standing in water then drew with that alpha 0 and turned invisible.

`_KS14/Shaders/sinking_cut.swsl` has the full story. If your shader has uniforms that change per use, either set
every one of them on every instance, or give the shader its own `.swsl` so it has its own program.

## Where a shader can be applied

### 1. Sprite layers

Set `shader:` on a layer in YAML, or `SpriteComponent.LayerSetShader` from code. The layer is drawn straight into the
world with it, so `UV` is the atlas sub-rectangle and lighting applies unless the shader is unshaded.

Upstream's `unshaded` shader is the common case. Displacement maps (`Textures/Shaders/displacement.swsl`) are also
applied this way by the engine.

### 2. Sprite post-shaders

A post-shader runs over the *finished* sprite, all layers composited. Use one when an effect must treat the sprite
as one image: glitching, outlines, stealth, sinking.

```csharp
_spriteSystem.SetPostShader(spriteEntity, new SpriteComponent.PostShaderArgs(KsPostShaderIds.WaveDistortion, shaderInstance)
{
    Before = KsPostShaderIds.BeforeOutlines,  // or After = KsPostShaderIds.AfterBaseEffects for an outline
    GetScreenTexture = false,                  // true to get SCREEN_TEXTURE (costs a screen copy)
    RaiseShaderEvent = false,                  // true to get BeforePostShaderRenderEvent every draw
});

_spriteSystem.RemovePostShader(spriteEntity, KsPostShaderIds.WaveDistortion);
```

- **Several post-shaders stack on one sprite**, each under its own string id. Replacing or removing one leaves the
  others alone. Put fork ids in `Content.Client/_KS14/Graphics/KsPostShaderIds.cs`.
- **Order them.**
  - A base effect goes *before* the outlines: `Before = KsPostShaderIds.BeforeOutlines`.
  - An outline goes *after* the base effects: `After = KsPostShaderIds.AfterBaseEffects`.
  - Use the `KsPostShaderIds` arrays, not upstream's `ContentPostShaderIds`, or you end up unordered against the
    fork's own shaders.
- **How it renders.** This is `Clyde/Clyde.HLR.cs` → `RenderSpritePostShaders`:
  1. The sprite is drawn, **already lit**, into a transparent render target.
  2. That target is sized to the sprite's on-screen bounds **times 1.25** (`Clyde.PostShadeScale`), with the
     sprite centred in it.
  3. Each post-shader then draws that target as a quad. The last one composites it into the viewport.
- **What that means for the shader:**
  - **Write it `light_mode unshaded`.** The sprite is already lit, and lighting it again darkens it.
  - **The quad is padded.** In `UV2` the sprite fills roughly 0.1 to 0.9 on each axis, not 0 to 1. Anything laid
    out "over the sprite" needs that range as a parameter; see `barRegion` in `KsBarGlitch`.
  - **Its resolution follows zoom.** It is the sprite's size on screen, so measure distances in `UV2`, not in
    texture pixels.
  - **The input is premultiplied alpha.** The final composite uses premultiplied blending, so if you scale alpha,
    scale RGB with it or partly transparent edges come out too bright.
  - **The render target is pooled** and may be bigger than the sprite. Stay inside the quad's own `UV` range and
    treat anything outside it as empty.
- `BeforePostShaderRenderEvent` fires once *per post-shader entry* that asked for it. Check `args.Id` before
  touching `args.Shader`; see `KsWaveDistortionSystem`.

### 3. Overlays (full screen or world space)

Subclass `Overlay` (see `KsShaderStatusEffectOverlay`), then:

- set `RequestScreenTexture => true`;
- declare `uniform sampler2D SCREEN_TEXTURE;` in the shader;
- set it with `shaderInstance.SetParameter("SCREEN_TEXTURE", ScreenTexture)`;
- draw a rect over `args.WorldBounds`.

To chain several full-screen passes, ping-pong between render targets. When sampling a render target you drew into
yourself, **watch for the image coming out upside down**: render targets and the screen don't agree on which way Y
points. `KsShaderStatusEffectOverlay` flips Y on its intermediate passes for this.

### 4. Status effects, without writing any C#

Both are client-only components; add new ones to `Content.Server/_KS14/Entry/IgnoredComponents.cs`. Put either on
a status effect prototype (`parent: MobStatusEffectDebuff`):

| Component | Shades | Seen by |
| --- | --- | --- |
| `KsShaderStatusEffect` | the affected player's whole screen (overlay) | only that player |
| `KsPostShaderStatusEffect` | the affected entity's sprite (post-shader) | everyone who can see it |

```yaml
- type: entity
  parent: MobStatusEffectDebuff
  id: StatusEffectBarGlitch
  name: glitching
  components:
  - type: KsPostShaderStatusEffect
    shader: KsBarGlitch
    timeLeftParameter: timeLeft   # float uniform fed 1 -> 0 over the effect's duration, every frame
    seedParameter: seed           # post-shader only: float unique per effect, so sprites don't animate in lockstep
    parameters:                   # set once, when the instance is created
      barRegion: !type:KsShaderVector2
        value: 0.1,0.9
```

Parameter types are in `Content.Client/_KS14/ShaderStatusEffect/KsShaderParameter.cs`: `KsShaderFloat`,
`KsShaderInt`, `KsShaderBool`, `KsShaderVector2`, `KsShaderColor`. Each effect entity gets its own
`InstanceUnique()`, disposed when the effect ends.

## Checklist

- [ ] No early `return` in `fragment()`.
- [ ] No variables named after GLSL built-ins: `step`, `distance`, `noise`, `length`, `mix`, `sign`, and so on.
- [ ] Every float literal has a decimal point: `1.0`, not `1`. GLSL ES does not convert `int` to `float` for you.
- [ ] `highp` on anything that holds positions, UVs or `TIME`. `TIME` gets big, and at `mediump` it loses its
      fractional part.
- [ ] Derivatives are behind `#ifdef HAS_DFDX` with a fallback.
- [ ] Post-shaders are `light_mode unshaded` and allow for the 1.25x padding.
- [ ] Every per-use uniform is set on every instance, or the shader has its own program.
- [ ] Every `InstanceUnique()` is disposed.

## Testing

Nothing headless compiles or runs a shader. Integration tests and the YAML linter load the prototype but don't
check the GLSL. Launch the client and look.

- **Compile errors** show in the client log when the shader is first used, with `error.glsl` next to them.
- **For iterating on values,** VV the component or the effect entity and change parameters live, rather than
  restarting.
