namespace Outspoken.Core.Injection;

/// <summary>Delivers dictated text per the never-lost invariant. Production: <see cref="InjectionEngine"/>.</summary>
public interface IInjector
{
    Task<InjectionResult> InjectAsync(string text, CancellationToken cancellationToken = default);
}
