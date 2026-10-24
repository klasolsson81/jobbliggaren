using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (CTO conflicts 9–11) — the selection on the one password key. Absent and blank are two states; neither
/// stops the host, and resolving the adapter connects to nothing.
/// </summary>
public class DmarcMailboxCollectionExtensionsTests
{
    [Fact]
    public async Task No_key_reads_no_mailbox()
    {
        var mailbox = Resolve(new Dictionary<string, string?>());

        mailbox.ShouldBeOfType<NotConfiguredDmarcMailbox>();
        (await mailbox.OpenAsync(TestContext.Current.CancellationToken)).ShouldBeOfType<DmarcMailboxOpen.NotConfigured>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task A_blank_password_is_misconfigured(string password)
    {
        var mailbox = Resolve(new Dictionary<string, string?> { [DmarcMailboxCollectionExtensions.PasswordKey] = password });

        mailbox.ShouldBeOfType<MisconfiguredDmarcMailbox>();
        (await mailbox.OpenAsync(TestContext.Current.CancellationToken)).ShouldBeOfType<DmarcMailboxOpen.Misconfigured>();
    }

    [Fact]
    public void A_password_selects_the_imap_mailbox_without_connecting_and_never_shows_it()
    {
        var password = "test-" + Guid.NewGuid().ToString("N");

        var mailbox = Resolve(new Dictionary<string, string?> { [DmarcMailboxCollectionExtensions.PasswordKey] = password });

        mailbox.ShouldBeOfType<ImapDmarcMailbox>();
        mailbox.ToString()!.ShouldNotContain(password);
    }

    [Fact]
    public void The_key_is_the_one_the_deployment_writes()
    {
        DmarcMailboxCollectionExtensions.PasswordKey.ShouldBe("Dmarc:Mailbox:Password");
    }

    [Fact]
    public void The_host_port_and_account_are_constants()
    {
        ImapDmarcMailbox.Host.ShouldBe("imap.strato.de");
        ImapDmarcMailbox.Port.ShouldBe(993);
        ImapDmarcMailbox.Username.ShouldBe("dmarc@jobbliggaren.se");
        ImapDmarcMailbox.Mechanism.ShouldBe("PLAIN");
    }

    [Fact]
    public void The_mailbox_is_one_singleton_and_registers_nothing_that_validates_at_start()
    {
        var services = new ServiceCollection();

        services.AddDmarcMailbox(Configuration(new Dictionary<string, string?> { [DmarcMailboxCollectionExtensions.PasswordKey] = "test-value" }));

        services.ShouldHaveSingleItem().ServiceType.ShouldBe(typeof(IDmarcMailbox));
        services[0].Lifetime.ShouldBe(ServiceLifetime.Singleton);
        services.ShouldNotContain(descriptor => descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IValidateOptions<>));
    }

    private static IDmarcMailbox Resolve(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDmarcMailbox(Configuration(settings));
        return services.BuildServiceProvider().GetRequiredService<IDmarcMailbox>();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
