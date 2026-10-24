using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Email.Dmarc.Queries.GetDmarcReports;

/// <summary>
/// The stored DMARC reports for the region's fixed window (#1981 PR 4a, ADR 0184). It takes no period: the window is
/// <see cref="DmarcReportsEvaluator.Window"/>, whatever the page's delivery period is.
/// </summary>
public sealed record GetDmarcReportsQuery : IQuery<EmailDmarcDto>, IAdminRequest;
