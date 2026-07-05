using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IFrameProvider"/> over the committed, versioned frame catalog
/// (<c>frames.v1.json</c>, Fas 4b PR-5, ADR 0093 §D3). Loads + validates the embedded
/// asset once at construction — including the cross-asset invariant that every sentence
/// frame's lead verb resolves in the injected verb mapping at the pinned version — and
/// serves the cached immutable contract. Registered as a singleton (parity
/// <see cref="RubricProvider"/>); a malformed or drifted asset fails the host at
/// startup, never mid-request.
/// </summary>
internal sealed class FrameProvider(IVerbMapper verbMapper) : IFrameProvider
{
    private readonly FrameCatalog _catalog = FramesLoader.Load(verbMapper.GetVerbMapping());

    public FrameCatalog GetFrameCatalog() => _catalog;
}
