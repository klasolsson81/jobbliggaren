using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

public class EmailProviderSelectionTests
{
    private static IConfiguration With(string? provider) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = provider })
            .Build();

    [Fact]
    public void ConfiguredProvider_ShouldBeConsole_WhenNoneIsSet() =>
        EmailProviderSelection.ConfiguredProvider(With(null)).ShouldBe("Console");

    [Theory]
    [InlineData("Scaleway")]
    [InlineData("scaleway")]
    [InlineData("SCALEWAY")]
    public void IsScaleway_ShouldIgnoreCase(string provider) =>
        EmailProviderSelection.IsScaleway(provider).ShouldBeTrue();

    [Theory]
    [InlineData("Console")]
    [InlineData("Ses")]
    [InlineData("Scaleway ")]
    [InlineData("")]
    public void IsScaleway_ShouldRefuseEveryOtherValue(string provider) =>
        EmailProviderSelection.IsScaleway(provider).ShouldBeFalse();

    [Theory]
    [InlineData("Console")]
    [InlineData("console")]
    public void IsConsole_ShouldIgnoreCase(string provider) =>
        EmailProviderSelection.IsConsole(provider).ShouldBeTrue();
}
