using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IRubricProvider"/> over the committed, versioned CV-quality rubric
/// (F4-7). Loads + maps + validates the embedded <c>rubric.v1.2.0.json</c> once at
/// construction (fail-loud at startup, never mid-request) and serves the cached
/// immutable contract — registered as a singleton, parity with
/// <c>ITaxonomyReadModel</c>.
/// </summary>
internal sealed class RubricProvider : IRubricProvider
{
    private readonly Rubric _rubric = RubricLoader.Load();

    public Rubric GetRubric() => _rubric;
}
