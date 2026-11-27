using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IFrameProvider"/> over the committed, versioned frame catalog
/// (<c>frames.v1.json</c>, Fas 4b PR-5, ADR 0093 §D3). Loads + validates the embedded
/// asset once at construction — including the cross-asset invariant that every sentence
/// frame's lead verb resolves in the injected verb mapping at the pinned version — and
/// serves the cached immutable contract. Registered as a singleton (parity
/// <see cref="RubricProvider"/>).
/// </summary>
internal sealed class FrameProvider(IVerbMapper verbMapper) : IFrameProvider
{
    // The frames are Swedish sentences, so they bind to the Swedish verb mapping (#2341).
    private readonly FrameCatalog _catalog = FramesLoader.Load(verbMapper.GetVerbMapping(ResumeLanguage.Sv));

    public FrameCatalog GetFrameCatalog() => _catalog;
}
