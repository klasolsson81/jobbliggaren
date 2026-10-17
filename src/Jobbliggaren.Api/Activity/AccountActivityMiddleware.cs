using Jobbliggaren.Application.Auth.Activity;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Api.Activity;

/// <summary>
/// #2161 (ADR 0172 D4) — puts the authenticated request's account in the activity record. It runs directly after
/// <c>UseAuthentication</c>, reads only the principal the session handler built, and does nothing for an anonymous
/// request; the recorder decides whether this minute is already recorded and never throws.
/// </summary>
internal sealed class AccountActivityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IAccountActivityRecorder recorder)
    {
        if (currentUser.IsAuthenticated && currentUser.UserId is { } userId)
            await recorder.RecordRequestAsync(userId, context.RequestAborted);

        await next(context);
    }
}
