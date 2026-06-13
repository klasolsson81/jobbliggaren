using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

public class CompanyTests
{
    [Fact]
    public void Create_WithValidName_ReturnsSuccess()
    {
        var result = Company.Create("Klarna");
        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Klarna");
    }

    [Fact]
    public void Create_WithEmptyName_ReturnsFailure()
    {
        var result = Company.Create("");
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Company.NameRequired");
    }

    [Fact]
    public void Create_WithNameExceeding200Chars_ReturnsFailure()
    {
        var result = Company.Create(new string('A', 201));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Company.NameTooLong");
    }

    [Fact]
    public void Create_TrimsWhitespace()
    {
        var result = Company.Create("  Klarna  ");
        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Klarna");
    }
}
