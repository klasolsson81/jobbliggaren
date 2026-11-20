using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetJuniorItJobAdCount;

/// <summary>
/// ADR 0188 (#2265) — the count behind the junior quick link in the /jobb hero, for a signed-in user. It is
/// <c>/junior</c>'s own number: the same port method, read at one instant. Not a public surface (ADR 0166, Amendment
/// 2026-11-20); it composes <c>JuniorMembership</c> through the port and never copies it.
/// </summary>
public sealed record GetJuniorItJobAdCountQuery : IQuery<JuniorItJobAdCountDto>, IAuthenticatedRequest;
