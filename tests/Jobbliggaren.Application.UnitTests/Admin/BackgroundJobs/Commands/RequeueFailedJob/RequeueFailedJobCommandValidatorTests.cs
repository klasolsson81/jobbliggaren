using Jobbliggaren.Application.Admin.BackgroundJobs.Commands.RequeueFailedJob;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.BackgroundJobs.Commands.RequeueFailedJob;

/// <summary>
/// #204 / TD-83 PR2 — shape validation only ("shape in validator, invariant in handler"). The
/// existence + Failed-state precondition is live state resolved in the handler via the port; the
/// validator only guards that JobId is present and within the 64-char bound (a Hangfire job id is a
/// short storage key, never user prose).
/// </summary>
public class RequeueFailedJobCommandValidatorTests
{
    private readonly RequeueFailedJobCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidJobId_Passes()
    {
        var result = _validator.Validate(new RequeueFailedJobCommand("server:1:job:42"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyJobId_Fails()
    {
        var result = _validator.Validate(new RequeueFailedJobCommand(string.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RequeueFailedJobCommand.JobId));
    }

    [Fact]
    public void Validate_JobIdAtMaxLength_Passes()
    {
        // Boundary: exactly 64 chars is allowed (MaximumLength is inclusive).
        var result = _validator.Validate(new RequeueFailedJobCommand(new string('a', 64)));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_JobIdOverMaxLength_Fails()
    {
        var result = _validator.Validate(new RequeueFailedJobCommand(new string('a', 65)));

        result.IsValid.ShouldBeFalse();
    }
}
