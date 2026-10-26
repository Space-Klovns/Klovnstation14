#nullable enable
using System;
using System.Runtime.ExceptionServices;
using Content.IntegrationTests.Fixtures;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

/// <summary>Preserves server assertion failures across the pool's dirty-disposal warning.</summary>
public abstract class KsProcgenGameTest : GameTest
{
    private Exception? _assertionFailure;

    public override async Task DoTeardown()
    {
        try { await base.DoTeardown(); }
        finally
        {
            // Assert.Warn during disposal can replace a failed test with a skipped result.
            // Rethrow after cleanup so a broken generator cannot silently pass the suite.
            if (_assertionFailure != null) ExceptionDispatchInfo.Capture(_assertionFailure).Throw();
        }
    }

    protected async Task AssertOnServer(Action assertion)
    {
        try { await Pair.Server.WaitAssertion(assertion); }
        catch (Exception exception)
        {
            _assertionFailure = exception;
            TestContext.Error.WriteLine(exception.ToString());
            throw;
        }
    }
}
