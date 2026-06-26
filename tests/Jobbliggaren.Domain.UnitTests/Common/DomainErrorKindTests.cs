using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Common;

/// <summary>
/// Pins the <see cref="DomainError"/> kind-union contract (#203 / TD-84 / TD-63): each factory
/// stamps the correct <see cref="ErrorKind"/>, which the API layer's central mapper will translate
/// to an HTTP status (NotFound→404, Conflict→409, Gone→410, Validation→400). This is the foundation
/// that PR-1b's mapper consumes; getting the kind right at the source is the load-bearing invariant.
/// </summary>
public class DomainErrorKindTests
{
    [Fact]
    public void NotFound_entity_id_StampsNotFoundKind_AndConventionalCode()
    {
        var id = Guid.NewGuid();

        var error = DomainError.NotFound("Resume", id);

        error.Kind.ShouldBe(ErrorKind.NotFound);
        error.Code.ShouldBe("Resume.NotFound");
        error.Message.ShouldContain(id.ToString());
    }

    [Fact]
    public void NotFound_explicitCode_StampsNotFoundKind_AndPreservesCodeAndMessage()
    {
        // The (string code, string message) overload — for not-found cases whose code doesn't
        // follow the "{entity}.NotFound" shape (e.g. "Auth.JobSeekerNotFound") or whose message is
        // bespoke (a token lookup). Both string args → this overload wins over (string, object).
        var error = DomainError.NotFound("Auth.JobSeekerNotFound", "JobSeeker hittades inte.");

        error.Kind.ShouldBe(ErrorKind.NotFound);
        error.Code.ShouldBe("Auth.JobSeekerNotFound");
        error.Message.ShouldBe("JobSeeker hittades inte.");
    }

    [Fact]
    public void Validation_StampsValidationKind()
    {
        var error = DomainError.Validation("X.Invalid", "nope");

        error.Kind.ShouldBe(ErrorKind.Validation);
        error.Code.ShouldBe("X.Invalid");
    }

    [Fact]
    public void Conflict_StampsConflictKind()
    {
        var error = DomainError.Conflict("X.NotPending", "wrong state");

        error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void Gone_StampsGoneKind()
    {
        var error = DomainError.Gone("X.Expired", "too late");

        error.Kind.ShouldBe(ErrorKind.Gone);
    }

    [Fact]
    public void None_DefaultsToValidationKind()
    {
        // None is the no-error sentinel (success path) — its kind is never mapped, but a defaulted
        // DomainError must land on the 400 floor, never accidentally NotFound/Gone.
        DomainError.None.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void DirectConstruction_DefaultsToValidationKind()
    {
        // A direct (non-factory) construction is safe: the 400 floor, never a softer status.
        new DomainError("Some.Code", "msg").Kind.ShouldBe(ErrorKind.Validation);
    }
}
