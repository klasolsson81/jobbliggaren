using Jobbliggaren.Application.Admin.Email.Dmarc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — which mailbox the Worker reads, chosen from the one password key. No key means the
/// deployment does not read DMARC reports; a key that is set but blank is a deployment mistake, shown as its own state
/// and never as a stopped Worker, because the same process runs the erasure and retention jobs. Nothing is validated
/// at start for the same reason; the only DMARC setting that can stop the Worker is the house <c>_FILE</c> seam, which
/// <c>--check</c> guards.
/// </summary>
public static class DmarcMailboxCollectionExtensions
{
    /// <summary>The deployment sets it through <c>Dmarc__Mailbox__Password_FILE</c>, which stores a blank file as "".</summary>
    public const string PasswordKey = "Dmarc:Mailbox:Password";

    public static IServiceCollection AddDmarcMailbox(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var password = configuration[PasswordKey];
        if (password is null)
        {
            services.AddSingleton<IDmarcMailbox>(new NotConfiguredDmarcMailbox());
        }
        else if (string.IsNullOrWhiteSpace(password))
        {
            services.AddSingleton<IDmarcMailbox>(new MisconfiguredDmarcMailbox());
        }
        else
        {
            services.AddSingleton<IDmarcMailbox>(provider =>
                new ImapDmarcMailbox(password, provider.GetRequiredService<ILogger<ImapDmarcMailbox>>()));
        }

        return services;
    }
}

/// <summary>The answer where no mailbox password is configured, so nothing is read.</summary>
internal sealed class NotConfiguredDmarcMailbox : IDmarcMailbox
{
    public ValueTask<DmarcMailboxOpen> OpenAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<DmarcMailboxOpen>(new DmarcMailboxOpen.NotConfigured());
}

/// <summary>The answer where the password pointer is set but the password is blank.</summary>
internal sealed class MisconfiguredDmarcMailbox : IDmarcMailbox
{
    public ValueTask<DmarcMailboxOpen> OpenAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<DmarcMailboxOpen>(new DmarcMailboxOpen.Misconfigured());
}
