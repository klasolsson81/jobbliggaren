using System.Text.Json;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Reads Scaleway Transactional Email for the allowance figure (#1981, ADR 0178): <c>project-consumption</c> for
/// <c>monthly_emails_count</c> ("emails sent during the current month in the project") and <c>webhooks_count</c>, and
/// <c>offer-subscriptions</c> for the plan's <c>offer_name</c>. Both need only <c>offer_subscription:read</c>
/// (measured 2026-10-19, ADR 0178).
/// <para>
/// <b>What it never does.</b> It reads a 2xx body only up to <see cref="MaxBodyBytes"/>, and from it only the named
/// fields: <c>project_id</c>, <c>organization_id</c> and every other field are skipped by the reader and never become
/// a value.
/// </para>
/// </summary>
internal sealed class ScalewayUsageSource(
    ScalewayReadTransport transport,
    IDateTimeProvider clock) : IProviderUsageAttemptSource
{
    internal const int MaxBodyBytes = 64 * 1024;

    private const string ConsumptionPath = "project-consumption";
    private const string OfferSubscriptionsPath = "offer-subscriptions";
    private const string MonthlyEmailsCountField = "monthly_emails_count";
    private const string WebhooksCountField = "webhooks_count";
    private const string OfferSubscriptionsField = "offer_subscriptions";
    private const string OfferNameField = "offer_name";
    private const string EssentialOfferName = "essential";
    private const string ScaleOfferName = "scale";

    public ValueTask<ProviderAttempt<EmailProviderUsageRead>> AttemptUsageAsync(CancellationToken cancellationToken) =>
        transport.GetAsync<EmailProviderUsageRead>(
            ConsumptionPath,
            [],
            MaxBodyBytes,
            body => TryReadCounts(body, out var emails, out var webhooks)
                ? new EmailProviderUsageRead.Observed(emails, webhooks, clock.UtcNow)
                : null,
            reason => new EmailProviderUsageRead.Failed(reason),
            cancellationToken);

    public ValueTask<ProviderAttempt<EmailProviderPlanRead>> AttemptPlanAsync(CancellationToken cancellationToken) =>
        transport.GetAsync<EmailProviderPlanRead>(
            OfferSubscriptionsPath,
            [],
            MaxBodyBytes,
            body => TryReadPlan(body, out var plan) ? new EmailProviderPlanRead.Observed(plan, clock.UtcNow) : null,
            reason => new EmailProviderPlanRead.Failed(reason),
            cancellationToken);

    /// <summary>
    /// Reads the two counts from the top-level object and skips every other value without materialising it. A count
    /// that is absent, negative or not an integer is unreadable; the webhook count alone may be unreadable.
    /// </summary>
    private static bool TryReadCounts(byte[] body, out long monthlyEmails, out int? webhooks)
    {
        monthlyEmails = 0;
        webhooks = null;
        long? emails = null;

        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isEmails = reader.ValueTextEquals(MonthlyEmailsCountField);
                var isWebhooks = !isEmails && reader.ValueTextEquals(WebhooksCountField);
                reader.Read();

                if (isEmails)
                {
                    emails = reader.TokenType == JsonTokenType.Number && reader.TryGetUInt32(out var value)
                        ? value
                        : null;
                }
                else if (isWebhooks)
                {
                    webhooks = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) && value >= 0
                        ? value
                        : null;
                }

                // A no-op on a number; on an object or array it moves past the whole value, so nothing nested is
                // ever read as a top-level field.
                reader.Skip();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }

        if (emails is not { } count)
        {
            return false;
        }

        monthlyEmails = count;
        return true;
    }

    /// <summary>
    /// Reads <c>offer_subscriptions[].offer_name</c> and nothing else. The plan is the one subscription's name mapped onto
    /// <see cref="EmailPlan"/>; a name this code does not know, or anything but exactly one subscription, is
    /// <see cref="EmailPlan.Unknown"/>. A body without the array is unreadable.
    /// </summary>
    private static bool TryReadPlan(byte[] body, out EmailPlan plan)
    {
        plan = EmailPlan.Unknown;
        var subscriptions = -1;
        var onlyPlan = EmailPlan.Unknown;

        try
        {
            var reader = new Utf8JsonReader(body);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isSubscriptions = reader.ValueTextEquals(OfferSubscriptionsField);
                reader.Read();
                if (!isSubscriptions)
                {
                    reader.Skip();
                    continue;
                }

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    return false;
                }

                subscriptions = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    subscriptions++;
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    var named = EmailPlan.Unknown;
                    while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var isName = reader.ValueTextEquals(OfferNameField);
                        reader.Read();
                        if (isName && reader.TokenType == JsonTokenType.String)
                        {
                            named = reader.ValueTextEquals(EssentialOfferName) ? EmailPlan.Essential
                                : reader.ValueTextEquals(ScaleOfferName) ? EmailPlan.Scale
                                : EmailPlan.Unknown;
                        }

                        reader.Skip();
                    }

                    onlyPlan = named;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }

        if (subscriptions < 0)
        {
            return false;
        }

        plan = subscriptions == 1 ? onlyPlan : EmailPlan.Unknown;
        return true;
    }
}
