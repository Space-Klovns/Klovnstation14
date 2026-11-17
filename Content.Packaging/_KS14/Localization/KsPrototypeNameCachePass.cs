using Content.Shared._KS14.Localization;
using System.Runtime.ExceptionServices;
using Robust.Packaging.AssetProcessing;

namespace Content.Packaging._KS14.Localization;

/// <summary>
/// Observes the package inputs, then emits the compiled localization tables alongside them.
/// </summary>
public sealed class KsPrototypeNameCachePass : AssetPass
{
    private readonly List<AssetFile> _sources = [];
    private Exception? _compilationError;

    protected override AssetFileAcceptResult AcceptFile(AssetFile file)
    {
        // Replace a development-build artifact with the version compiled from this package.
        if (file.Path == KsPrototypeNameCache.ResourcePath
            || file.Path.StartsWith(KsPrototypeNameCache.ResourcePath + ".", StringComparison.Ordinal))
            return AssetFileAcceptResult.Consumed;
        if (KsPrototypeNameCacheCompiler.IsSource(file.Path))
        {
            lock (_sources)
                _sources.Add(file);
        }
        return AssetFileAcceptResult.Pass;
    }

    protected override void AcceptFinished()
    {
        RunJob(() =>
        {
            try
            {
                var cache = KsPrototypeNameCacheCompiler.Compile(_sources);
                SendFileFromMemory(KsPrototypeNameCache.ResourcePath, cache);
                Logger?.Info($"Compiled prototype-name cache ({cache.Length:N0} bytes).");
            }
            catch (Exception exception)
            {
                // AssetPass.RunJob does not propagate exceptions. Report them to the
                // packaging caller instead of terminating a live server's thread pool.
                _compilationError = exception;
                Logger?.Error($"Prototype-name cache compilation failed: {exception.Message}");
            }
        });
    }

    public void ThrowIfCompilationFailed()
    {
        if (_compilationError != null)
            ExceptionDispatchInfo.Capture(_compilationError).Throw();
    }
}
