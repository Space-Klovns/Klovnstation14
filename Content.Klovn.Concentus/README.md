# Concentus (vendored)

[Concentus](https://github.com/lostromb/concentus) is a pure C# port of libopus (fixed point, bit-exact with libopus
1.1.x when built with `PARITY`). Voice chat uses it for its Opus codec (`KsVoiceCodec.Opus`,
`Content.Shared/_KS14/Voice/KsVoiceCodecs.cs`), and TTS for Ogg Opus clips (`Content.Shared/_KS14/TTS/KsTtsOpus.cs`).

It is a content assembly of its own, `Content.Klovn.Concentus`, referenced by `Content.Shared`: forty-odd thousand
lines of someone else's code don't belong inside ours. The name matters. The client loads every `Content.*` assembly
it is shipped as a sandboxed module, so a name without that prefix would never be loaded. It is also listed in
`Content.Packaging/ClientPackaging.cs`, which names the assemblies clients are shipped (servers work theirs out
from `Content.Server.deps.json`).

It's vendored as source because client content runs sandboxed. Only `Content.*` assemblies load, so a NuGet package
isn't reachable from the client, and native libopus isn't either.

- License: BSD 3-Clause, in `LICENSE`. Every file keeps its original copyright header.
- Upstream commit: `UPSTREAM_COMMIT`.
- Namespaces are upstream's, moved under `Content.Klovn.Concentus.*` (see Edits). `vendor.py` does that too, so a re-sync is still one command.

## What's included

Everything under `CSharp/Concentus` that the plain Opus encoder and decoder use: `Celt/`, `Common/`, `Opus/`, `Silk/`
and the two codec interfaces.

These are left out:
- native interop: `Native/`, `OpusCodecFactory.cs`, `ResamplerFactory.cs`;
- anything we don't use: multistream, the Speex resampler, the test-vector comparer.

## Edits

All edits are made by `vendor.py`, and each is marked `// KS14:` in the code. Most exist because the content sandbox
(`RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml` plus ILVerify) checks this code when the client loads it:

| Edit | Why |
| --- | --- |
| File header: `#nullable disable`, `#pragma warning disable` | Upstream isn't written for our nullability and warning settings, and Release builds treat warnings as errors. |
| `#define PARITY` in `Inlines.cs` and `CodecHelpers.cs` | Upstream defines it in its csproj, selecting libopus's exact fixed-point routines. |
| `stackalloc T[n]` becomes `new T[n]` | `stackalloc` is banned on the client (unverifiable `localloc`). |
| `Buffer.BlockCopy` becomes `Array.Copy`, with element counts | `System.Buffer` isn't whitelisted. |
| `OpusAssert` calls `DebugTools.Assert`, without `[Conditional("DEBUG")]` | Neither `System.Diagnostics.Debug` nor `ConditionalAttribute` is whitelisted. |
| `Debug.WriteLine` in `Pointer.cs`'s trace helper removed | As above. |
| The `Vector<T>` pitch kernel removed; the scalar path is always taken | `System.Numerics.Vector<T>` isn't whitelisted. |
| `Tuple<int, int>` becomes `(int, int)?` in `Pointer.cs`'s debug statistics | `System.Tuple` isn't whitelisted; value tuples are. |
| `ArgumentNullException` becomes `ArgumentException` | Only the base class is whitelisted. |
| `Inlines.INLINE_ATTR` const removed, written into each `[MethodImpl]` | A field of type `MethodImplOptions` references a type that isn't whitelisted; the pseudo-attribute doesn't. |
| `using Concentus.Native;` commented out | `Native/` isn't vendored. |
| `Concentus.*` namespaces become `Content.Klovn.Concentus.*` | The sandbox whitelists content by namespace (`Content.*`). Referenced from `Content.Shared` as a separate assembly, `Concentus.*` types would be refused. |

A violation compiles cleanly in both configurations and only fails at assembly load. Ordinary integration tests
don't run the sandbox (pooled pairs load content unchecked). **`SandboxTest` is the check**: it runs the whitelist
and ILVerify over `Content.Client`, `Content.Shared` and this assembly (CONTRIBUTING.md §6).

## Updating

```sh
git clone https://github.com/lostromb/concentus /tmp/concentus
python3 Content.Klovn.Concentus/vendor.py /tmp/concentus
```

The script refuses to finish if upstream moved something a patch expects, or if a banned API it knows about survives.
Then run the sandbox check, and the voice tests in Debug (where the `DEBUG`-only paths exist):

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~SandboxTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~_KS14.Voice"
```
