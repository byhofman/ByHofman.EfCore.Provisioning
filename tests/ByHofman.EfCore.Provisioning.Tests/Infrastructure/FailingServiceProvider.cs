using Microsoft.Extensions.DependencyInjection;

namespace ByHofman.EfCore.Provisioning.Tests.Infrastructure;

/// <summary>
/// Wraps a real provider but causes CreateScope() to throw for the first <paramref name="failCount"/> calls.
/// Used to simulate a database that isn't ready at startup.
/// </summary>
internal sealed class FailingServiceProvider : IServiceProvider
{
    private readonly IServiceProvider _inner;
    private int _failsRemaining;

    public FailingServiceProvider(IServiceProvider inner, int failCount)
    {
        _inner = inner;
        _failsRemaining = failCount;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceScopeFactory) && _failsRemaining > 0)
        {
            _failsRemaining--;
            return FailingScopeFactory.Instance;
        }

        return _inner.GetService(serviceType);
    }
}

internal sealed class FailingScopeFactory : IServiceScopeFactory
{
    public static readonly FailingScopeFactory Instance = new();

    public IServiceScope CreateScope()
        => throw new InvalidOperationException("Database not ready (simulated transient failure)");
}
