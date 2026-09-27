using Jobbliggaren.Application.JobSeekers.Commands.SetDigestCadence;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetDigestCadence;

// The validator is pre-handler defense-in-depth: IsInEnum closes the numeric-coercion gap (the wire
// binds the enum by NAME, so an unknown string already fails model-binding with a 400).
public class SetDigestCadenceCommandValidatorTests
{
    private readonly SetDigestCadenceCommandValidator _validator = new();

    [Theory]
    [InlineData(DigestCadence.Daily)]
    [InlineData(DigestCadence.Weekly)]
    public void Validate_WithDefinedCadence_Passes(DigestCadence cadence)
    {
        _validator.Validate(new SetDigestCadenceCommand(cadence)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithOutOfRangeCadence_IsInvalid()
    {
        _validator.Validate(new SetDigestCadenceCommand((DigestCadence)99)).IsValid.ShouldBeFalse();
    }
}
