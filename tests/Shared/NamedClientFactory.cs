namespace Jobbliggaren.TestSupport;

/// <summary>
/// Hands out clients over one scripted handler for exactly one client name (#1745): an adapter whose name constant is
/// misspelled, for instance as another provider's, throws here instead of passing unseen (test-writer, 6b form round,
/// Minor 14). The adapters' names share one configuration today, so this is a pin on the constant's spelling, not on
/// any behaviour a wrong name would change.
/// </summary>
internal sealed class NamedClientFactory(string name, HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string requested) =>
        requested == name
            ? new HttpClient(handler, disposeHandler: false)
            : throw new InvalidOperationException($"No client is named '{requested}'; this factory serves '{name}'.");
}
