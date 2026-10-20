using System.Globalization;
using System.Text.Json;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Reads Scaleway Transactional Email's <c>emails</c> list for the most recent failed mails in a period (#1981 PR 2),
/// one page of at most <see cref="EmailDeliveryLimits.MaxRecentFailures"/>, newest first.
/// <para>
/// <b>What it sends.</b> <c>project_id</c> (from the transport), <c>statuses=failed</c>, <see cref="ScalewaySince"/>,
/// <c>page_size</c> and <c>order_by=created_at_desc</c>, and nothing else: never <c>mail_rcpt</c>, <c>search</c>,
/// <c>mail_from</c> or <c>message_id</c>, so the list is never a lookup of one person.
/// </para>
/// <para>
/// <b>What it reads.</b> Per item only <c>status</c>, <c>created_at</c>, <c>flags</c> and <c>mail_rcpt</c>. The query
/// is checked against the answer, because a provider that ignored a parameter would answer 200 with a plausible wrong
/// list: an item that is not failed, created before the window, out of order, or an eleventh item makes the whole read
/// unreadable. <c>mail_rcpt</c> is masked from a stack buffer that is cleared after each item, so the full address never
/// becomes a string; an address that is not a string or is longer than an address can be is shown as the mask alone,
/// and never fails the read. <c>rcpt_to</c>, <c>subject</c>, <c>last_tries</c>, <c>status_details</c> and the ids are
/// skipped by the reader and never become values.
/// </para>
/// <para>
/// <b>The cap.</b> <see cref="MaxBodyBytes"/>, measured 2026-10-19 against one delivered item of about 657 bytes, so a
/// page of ten is about 6.6 KB; no failed item was measured. A larger answer is unreadable, never a cut list.
/// </para>
/// </summary>
internal sealed class ScalewayRecentFailuresSource(
    ScalewayReadTransport transport,
    IDateTimeProvider clock) : IRecentFailuresAttemptSource
{
    internal const int MaxBodyBytes = 64 * 1024;

    private const string EmailsPath = "emails";
    private const string StatusesParameter = "statuses";
    private const string PageSizeParameter = "page_size";
    private const string OrderByParameter = "order_by";
    private const string FailedStatus = "failed";
    private const string NewestFirst = "created_at_desc";

    private const string EmailsField = "emails";
    private const string StatusField = "status";
    private const string CreatedAtField = "created_at";
    private const string FlagsField = "flags";
    private const string RecipientField = "mail_rcpt";

    private const string MailboxNotFoundFlag = "mailbox_not_found";
    private const string HardBounceFlag = "hard_bounce";
    private const string MailboxFullFlag = "mailbox_full";
    private const string SoftBounceFlag = "soft_bounce";
    private const string SpamFlag = "spam";
    private const string BlocklistedFlag = "blocklisted";

    public ValueTask<ProviderAttempt<EmailRecentFailuresRead>> AttemptAsync(
        EmailDeliveryPeriod period, CancellationToken cancellationToken)
    {
        var since = ScalewaySince.Of(clock.UtcNow, EmailDeliveryPeriods.LengthOf(period));
        return transport.GetAsync<EmailRecentFailuresRead>(
            EmailsPath,
            [
                new(StatusesParameter, FailedStatus),
                new(ScalewaySince.Parameter, ScalewaySince.Format(since)),
                new(PageSizeParameter, EmailDeliveryLimits.MaxRecentFailures.ToString(CultureInfo.InvariantCulture)),
                new(OrderByParameter, NewestFirst),
            ],
            MaxBodyBytes,
            body => TryReadFailures(body, since, out var failures)
                ? new EmailRecentFailuresRead.Observed(failures, clock.UtcNow)
                : null,
            reason => new EmailRecentFailuresRead.Failed(reason),
            cancellationToken);
    }

    private static bool TryReadFailures(byte[] body, DateTimeOffset since, out List<EmailFailure> failures)
    {
        failures = [];
        var sawEmails = false;
        Span<char> address = stackalloc char[MaskedEmailAddress.MaxAddressLength];

        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isEmails = reader.ValueTextEquals(EmailsField);
                reader.Read();
                if (!isEmails)
                {
                    reader.Skip();
                    continue;
                }

                if (sawEmails || reader.TokenType != JsonTokenType.StartArray)
                {
                    return false;
                }

                sawEmails = true;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (failures.Count == EmailDeliveryLimits.MaxRecentFailures
                        || reader.TokenType != JsonTokenType.StartObject
                        || !TryReadFailure(ref reader, since, address, out var failure)
                        || (failures.Count > 0 && failure.CreatedAt > failures[^1].CreatedAt))
                    {
                        return false;
                    }

                    failures.Add(failure);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }

        return sawEmails;
    }

    /// <summary>One item, from its <c>StartObject</c> to its <c>EndObject</c>. False when it breaks the query's contract.</summary>
    private static bool TryReadFailure(
        ref Utf8JsonReader reader, DateTimeOffset since, scoped Span<char> address, out EmailFailure failure)
    {
        failure = null!;
        var isFailed = false;
        DateTimeOffset? createdAt = null;
        var reason = EmailFailureReason.Other;
        var recipient = MaskedEmailAddress.HiddenAddress;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals(StatusField))
            {
                reader.Read();
                isFailed = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(FailedStatus);
            }
            else if (reader.ValueTextEquals(CreatedAtField))
            {
                reader.Read();
                createdAt = reader.TokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out var at) ? at : null;
            }
            else if (reader.ValueTextEquals(FlagsField))
            {
                reader.Read();
                reason = MostSevereFlag(ref reader);
            }
            else if (reader.ValueTextEquals(RecipientField))
            {
                reader.Read();
                recipient = Masked(ref reader, address);
            }
            else
            {
                reader.Read();
            }

            // A no-op on a scalar or a consumed array; on an unread object or array it moves past the whole value.
            reader.Skip();
        }

        if (!isFailed || createdAt is not { } created || created < since)
        {
            return false;
        }

        failure = new EmailFailure(created, recipient, reason);
        return true;
    }

    /// <summary>The most severe known flag in the array; anything else, or no array, is <see cref="EmailFailureReason.Other"/>.</summary>
    private static EmailFailureReason MostSevereFlag(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return EmailFailureReason.Other;
        }

        var most = EmailFailureReason.Other;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                most = EmailFailureReasons.MostSevere([most, ReasonOf(ref reader)]);
            }
            else
            {
                reader.Skip();
            }
        }

        return most;
    }

    private static EmailFailureReason ReasonOf(ref Utf8JsonReader reader) =>
        reader.ValueTextEquals(MailboxNotFoundFlag) ? EmailFailureReason.MailboxNotFound
        : reader.ValueTextEquals(HardBounceFlag) ? EmailFailureReason.HardBounce
        : reader.ValueTextEquals(MailboxFullFlag) ? EmailFailureReason.MailboxFull
        : reader.ValueTextEquals(SoftBounceFlag) ? EmailFailureReason.SoftBounce
        : reader.ValueTextEquals(SpamFlag) ? EmailFailureReason.Spam
        : reader.ValueTextEquals(BlocklistedFlag) ? EmailFailureReason.Blocklisted
        : EmailFailureReason.Other;

    /// <summary>
    /// The recipient masked from <paramref name="address"/>, which is cleared before returning. The raw length is checked
    /// before the copy, so a value no address can have never reaches the buffer: it, a non-string, and a string that
    /// cannot be decoded are the mask alone.
    /// </summary>
    private static MaskedEmailAddress Masked(ref Utf8JsonReader reader, scoped Span<char> address)
    {
        if (reader.TokenType != JsonTokenType.String
            || reader.HasValueSequence
            || reader.ValueSpan.Length > MaskedEmailAddress.MaxAddressLength)
        {
            return MaskedEmailAddress.HiddenAddress;
        }

        try
        {
            var written = reader.CopyString(address);
            return MaskedEmailAddress.Of(address[..written]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return MaskedEmailAddress.HiddenAddress;
        }
        finally
        {
            address.Clear();
        }
    }
}
