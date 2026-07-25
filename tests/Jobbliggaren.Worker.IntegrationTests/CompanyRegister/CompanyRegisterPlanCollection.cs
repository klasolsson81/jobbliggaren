namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// Its own collection, so <see cref="CompanyRegisterPlanFixture"/>'s container and corpus are
/// single-owner: the plan-CHOICE guard's determinism rests on nothing else touching
/// <c>company_register</c>'s statistics between the seed and the EXPLAIN (ADR 0119).
/// </summary>
[CollectionDefinition("CompanyRegisterPlan")]
public sealed class CompanyRegisterPlanFixtureGroup
    : ICollectionFixture<CompanyRegisterPlanFixture>;
