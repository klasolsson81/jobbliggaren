namespace Jobbliggaren.Worker.IntegrationTests.Backup;

/// <summary>
/// #197 gate M-4 — a dedicated collection binding the two-container
/// <see cref="RestoreDrillFixture"/>. Separate from <c>[Collection("Worker")]</c> on purpose: the
/// drill's evidence is counts over <c>job_seekers</c>/<c>user_data_keys</c>, which cannot be read
/// in a container dozens of classes seed. See the fixture docblock.
/// </summary>
[CollectionDefinition("RestoreDrill")]
public sealed class RestoreDrillFixtureGroup : ICollectionFixture<RestoreDrillFixture>;
