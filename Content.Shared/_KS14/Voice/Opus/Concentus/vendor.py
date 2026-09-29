#!/usr/bin/env python3
# Re-vendors Concentus (a pure C# port of libopus) into this folder, applying the edits the content sandbox needs.
# See README.md. Usage: python3 vendor.py <path to a concentus checkout>
#
# The sandbox (RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml plus ILVerify) type-checks Content.Shared when the
# client loads it. Both build configurations compile clean regardless; a violation only shows up at assembly load.
# Every patch below exists because of that, and each one fails loudly if upstream moved the code it expects.

import pathlib
import re
import shutil
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent

# Only the plain Opus encoder and decoder. Everything else is either native interop (Native/, the factories) or
# unused by us and would only widen what has to pass the sandbox (multistream, the Speex resampler, the test-vector
# comparer).
SKIP = {
    "AssemblyInfo.cs",
    "OpusCodecFactory.cs",
    "ResamplerFactory.cs",
    "IResampler.cs",
    "IOpusMultiStreamDecoder.cs",
    "IOpusMultiStreamEncoder.cs",
    "Common/SpeexResampler.cs",
    "Opus/OpusCompare.cs",
    "Opus/OpusMultistream.cs",
    "Opus/Structs/OpusMSDecoder.cs",
    "Opus/Structs/OpusMSEncoder.cs",
    "Opus/Structs/ChannelLayout.cs",
    "Opus/Structs/VorbisLayout.cs",
}
SKIP_DIRS = {"Native", "bin", "obj"}

# Upstream builds with PARITY defined in every configuration (bit-exact fixed-point libopus). Files that test it get
# the define themselves, since Content.Shared doesn't set it.
PARITY_FILES = {"Common/Inlines.cs", "Opus/CodecHelpers.cs"}


def header(relative):
    define = "#define PARITY // KS14: upstream defines this in its csproj\n" if relative in PARITY_FILES else ""
    return (
        f"{define}"
        "// KS14: vendored from Concentus, see README.md in this folder. Edited only where the sandbox requires it.\n"
        "#nullable disable\n"
        "#pragma warning disable\n"
    )


def replace_once(text, old, new, relative):
    if text.count(old) != 1:
        sys.exit(f"{relative}: expected exactly one of {old!r}; upstream changed, redo this patch by hand")
    return text.replace(old, new)


def patch(relative, text):
    # stackalloc is banned on the client (unverifiable localloc). A heap array converts to Span<T> the same way.
    text = re.sub(r"stackalloc\s+(\w+)\s*\[", r"new \1[", text)

    if relative == "Common/Inlines.cs":
        # Neither System.Diagnostics.Debug nor ConditionalAttribute is whitelisted; Robust's own assert is.
        text = replace_once(
            text,
            '[Conditional("DEBUG")]\n        internal static void OpusAssert',
            "internal static void OpusAssert",
            relative)
        text = replace_once(
            text,
            "            Debug.Assert(condition, message);",
            "            Robust.Shared.Utility.DebugTools.Assert(condition, message); // KS14: was Debug.Assert",
            relative)

    if relative == "Common/CPlusPlus/Pointer.cs":
        # A tracing helper; System.Diagnostics.Debug isn't whitelisted.
        text = re.sub(r"Debug\.WriteLine\(([^;]*)\);", r"_ = \1; // KS14: was Debug.WriteLine", text)

    if "Buffer.BlockCopy" in text:
        # System.Buffer isn't whitelisted. BlockCopy counts bytes, Array.Copy counts elements: every upstream call
        #      copies between arrays of one element type with sizeof() scaling, which this undoes.
        text = re.sub(
            r"Buffer\.BlockCopy\(([^,]+),\s*([^,]+?)(?:\s*\*\s*sizeof\(\w+\))?,\s*([^,]+),\s*([^,]+?)(?:\s*\*\s*sizeof\(\w+\))?,\s*([^;]+?)(?:\s*\*\s*sizeof\(\w+\))?\);",
            r"Array.Copy(\1, \2, \3, \4, \5); // KS14: was Buffer.BlockCopy",
            text)

    # System.ArgumentNullException isn't whitelisted; its base, ArgumentException, is.
    text = text.replace("new ArgumentNullException(", "new ArgumentException/* KS14: was ArgumentNullException */(")

    if relative == "Common/CPlusPlus/Pointer.cs":
        # System.Tuple isn't whitelisted; value tuples are. Only used by debug-tracking statistics.
        #      Nullable, because "no range yet" is null.
        text = text.replace("Tuple<int, int>", "(int, int)? /* KS14: was Tuple<int, int> */")
        text = re.sub(r"new \(int, int\)\? /\* KS14: was Tuple<int, int> \*/\(([^;]*)\);", r"(\1); // KS14: was new Tuple<int, int>", text)

    if relative == "Common/Inlines.cs":
        # A const field typed MethodImplOptions references the enum type, which isn't whitelisted. Written straight
        #      into [MethodImpl], the pseudo-attribute, it's just method flags.
        text = re.sub(r"#if NET35\n\s*private const MethodImplOptions INLINE_ATTR = MethodImplOptions\.PreserveSig;\n#else\n\s*private const MethodImplOptions INLINE_ATTR = MethodImplOptions\.AggressiveInlining;\n#endif\n",
                      "        // KS14: INLINE_ATTR const removed, see vendor.py\n", text)
        text = text.replace("[MethodImpl(INLINE_ATTR)]", "[MethodImpl(MethodImplOptions.AggressiveInlining)]")
        if "INLINE_ATTR" in text.replace("INLINE_ATTR const removed", ""):
            sys.exit(f"{relative}: INLINE_ATTR still referenced")

    # Native/ isn't vendored; nothing we keep uses it, but a stray using still refers to the namespace.
    text = text.replace("using Concentus.Native;", "// using Concentus.Native; // KS14: Native/ isn't vendored")

    if relative == "Celt/CeltPitchXCorr.cs":
        # System.Numerics.Vector<T> isn't whitelisted: always take the scalar path.
        text = replace_once(
            text,
            "if (Vector.IsHardwareAccelerated)",
            "if (false) // KS14: was Vector.IsHardwareAccelerated",
            relative)
        text = replace_once(
            text,
            "Kernels.xcorr_kernel_vector(",
            "Kernels.xcorr_kernel/* KS14: was xcorr_kernel_vector */(",
            relative)

    if relative == "Celt/Kernels.cs":
        # The Vector<T> kernel, now unreachable. Removed so the type isn't referenced at all.
        start = text.index("        internal static void xcorr_kernel_vector(")
        end = text.index("        internal static int celt_inner_prod(", start)
        text = text[:start] + "        // KS14: xcorr_kernel_vector removed (System.Numerics.Vector<T> isn't whitelisted)\n\n" + text[end:]

    for forbidden in ("stackalloc", "Buffer.BlockCopy", "Debug.Assert", "Debug.WriteLine", "Vector<", "Vector.",
                      "DllImport", "MemoryMarshal", "Unsafe.", "Conditional(", "Tuple<", "ArgumentNullException",
                      "const MethodImplOptions"):
        # Comments may mention these; code may not.
        for line in text.splitlines():
            code = re.sub(r"/\*.*?\*/", "", line).split("//")[0]
            if forbidden in code:
                sys.exit(f"{relative}: still uses {forbidden}: {line.strip()}")

    return header(relative) + text


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__ or "usage: vendor.py <concentus checkout>")

    checkout = pathlib.Path(sys.argv[1]).resolve()
    source = checkout / "CSharp" / "Concentus"
    commit = subprocess.run(["git", "-C", str(checkout), "rev-parse", "HEAD"], capture_output=True, text=True,
                            check=True).stdout.strip()

    for existing in HERE.iterdir():
        if existing.is_dir():
            shutil.rmtree(existing)
        elif existing.suffix == ".cs":
            existing.unlink()

    count = 0
    for path in sorted(source.rglob("*.cs")):
        relative = path.relative_to(source).as_posix()
        if relative in SKIP or relative.split("/")[0] in SKIP_DIRS:
            continue

        destination = HERE / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(patch(relative, path.read_text(encoding="utf-8-sig")), encoding="utf-8", newline="\n")
        count += 1

    shutil.copyfile(checkout / "LICENSE", HERE / "LICENSE")
    (HERE / "UPSTREAM_COMMIT").write_text(commit + "\n")
    print(f"vendored {count} files from {commit}")


if __name__ == "__main__":
    main()
