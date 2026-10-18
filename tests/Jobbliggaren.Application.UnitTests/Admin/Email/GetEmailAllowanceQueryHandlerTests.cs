using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Queries.GetEmailAllowance;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Time;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

public class GetEmailAllowanceQueryHandlerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 18, 19, 30, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeSource(Func<CancellationToken, ValueTask<EmailProviderUsageRead>> read) : IEmailProviderUsageSource
    {
        public int Reads { get; private set; }

        public ValueTask<EmailProviderUsageRead> ReadUsageAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return read(cancellationToken);
        }
    }

    private static GetEmailAllowanceQueryHandler Handler(IEmailProviderUsageSource source, EmailAllowanceOptions? options = null) =>
        new(source, Options.Create(options ?? new EmailAllowanceOptions()), new SwedishCalendar());

    [Fact]
    public void Query_ShouldBeAnAdminRequest() =>
        new GetEmailAllowanceQuery().ShouldBeAssignableTo<IAdminRequest>();

    [Fact]
    public async Task Handle_ShouldReadTheSourceOnce_AndEvaluateItAgainstTheConfiguredTerms()
    {
        var source = new FakeSource(_ => ValueTask.FromResult<EmailProviderUsageRead>(
            new EmailProviderUsageRead.Observed(1_301, 0, At)));

        var dto = await Handler(source, new EmailAllowanceOptions { IncludedMonthlyEmails = 1_000 })
            .Handle(new GetEmailAllowanceQuery(), Ct);

        source.Reads.ShouldBe(1);
        dto.Terms.IncludedMonthlyEmails.ShouldBe(1_000);
        dto.Usage.Billable.ShouldBe(301);
        dto.Usage.EstimatedExtraCostCents.ShouldBe(8);
    }

    [Fact]
    public async Task Handle_ShouldPassTheCallersCancellationToken_ToTheSource()
    {
        CancellationToken seen = default;
        var source = new FakeSource(token =>
        {
            seen = token;
            return ValueTask.FromResult<EmailProviderUsageRead>(new EmailProviderUsageRead.NotConfigured());
        });
        using var cancellation = new CancellationTokenSource();

        await Handler(source).Handle(new GetEmailAllowanceQuery(), cancellation.Token);

        seen.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task Handle_ShouldPropagateCancellation_FromTheSource()
    {
        var source = new FakeSource(token => throw new OperationCanceledException(token));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Handler(source).Handle(new GetEmailAllowanceQuery(), cancelled.Token));
    }

    [Fact]
    public async Task Handle_ShouldReturnTheFailure_AsAValueAndNotAnException()
    {
        var source = new FakeSource(_ => ValueTask.FromResult<EmailProviderUsageRead>(
            new EmailProviderUsageRead.Failed(EmailAllowanceReason.Forbidden)));

        var dto = await Handler(source).Handle(new GetEmailAllowanceQuery(), Ct);

        dto.Usage.Status.ShouldBe(EmailAllowanceStatus.Failed);
        dto.Usage.Reason.ShouldBe(EmailAllowanceReason.Forbidden);
    }
}
