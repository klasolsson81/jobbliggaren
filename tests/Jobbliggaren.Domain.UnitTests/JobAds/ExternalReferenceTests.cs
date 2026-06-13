using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

public class ExternalReferenceTests
{
    [Fact]
    public void Create_WithPlatsbankenAndValidId_ReturnsSuccess()
    {
        var result = ExternalReference.Create(JobSource.Platsbanken, "26500001");
        result.IsSuccess.ShouldBeTrue();
        result.Value.Source.ShouldBe(JobSource.Platsbanken);
        result.Value.ExternalId.ShouldBe("26500001");
    }

    [Fact]
    public void Create_TrimsExternalId()
    {
        var result = ExternalReference.Create(JobSource.Platsbanken, "  26500001  ");
        result.IsSuccess.ShouldBeTrue();
        result.Value.ExternalId.ShouldBe("26500001");
    }

    [Fact]
    public void Create_WithManualSource_ReturnsFailure()
    {
        var result = ExternalReference.Create(JobSource.Manual, "anything");
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ExternalReference.ManualNotAllowed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingExternalId_ReturnsFailure(string? externalId)
    {
        var result = ExternalReference.Create(JobSource.Platsbanken, externalId);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ExternalReference.IdRequired");
    }

    [Fact]
    public void Create_WithIdLongerThan100Chars_ReturnsFailure()
    {
        var tooLong = new string('x', 101);
        var result = ExternalReference.Create(JobSource.Platsbanken, tooLong);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ExternalReference.IdTooLong");
    }

    [Fact]
    public void ValueEquality_TwoSameRefs_AreEqual()
    {
        var a = ExternalReference.Create(JobSource.Platsbanken, "26500001").Value;
        var b = ExternalReference.Create(JobSource.Platsbanken, "26500001").Value;
        a.ShouldBe(b);
    }

    [Fact]
    public void ValueEquality_DifferentSources_AreNotEqual()
    {
        var a = ExternalReference.Create(JobSource.Platsbanken, "26500001").Value;
        var b = ExternalReference.Create(JobSource.LinkedIn, "26500001").Value;
        a.ShouldNotBe(b);
    }
}
