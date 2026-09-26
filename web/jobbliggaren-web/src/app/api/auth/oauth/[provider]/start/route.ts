import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import {
  AUTHORIZATION_ENDPOINTS,
  toExternalProviderKey,
  type ExternalProviderKey,
} from "@/lib/auth/external-login";
import { setLoginFlow, setOAuthState } from "@/lib/auth/external-login-responses";
import { cookieSafeNext } from "@/lib/auth/login-flow";
import { LOGIN_ENTRY_PATH } from "@/lib/auth/login-paths";
import { safeRedirectPath } from "@/lib/auth/safe-redirect";
import { env } from "@/lib/env";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";

const ENTRY = LOGIN_ENTRY_PATH;

const startResponseSchema = z.strictObject({
  authorizeUrl: z.string(),
  state: z.string().regex(/^[A-Za-z0-9_-]{43}$/),
});

/** Relative, so the browser resolves it against the origin it is on rather than the one Next sees. */
function redirectTo(location: string): NextResponse {
  return new NextResponse(null, { status: 302, headers: { Location: location, "Cache-Control": "no-store" } });
}

function notCompleted(provider: ExternalProviderKey): NextResponse {
  const response = redirectTo(ENTRY);
  setLoginFlow(response, { phase: "notice", notice: "externalNotCompleted", provider });
  return response;
}

function isPrefetch(request: NextRequest): boolean {
  return (
    request.headers.get("next-router-prefetch") !== null ||
    (request.headers.get("sec-purpose") ?? "").includes("prefetch") ||
    request.headers.get("purpose") === "prefetch"
  );
}

/**
 * A start is a click on the row, a top-level navigation. An image, a frame or a fetch on another page is not one,
 * and would otherwise spend the start budget and the visitor's own rate limit. A client that sends neither header
 * is let through.
 */
function isEmbedded(request: NextRequest): boolean {
  const mode = request.headers.get("sec-fetch-mode");
  const dest = request.headers.get("sec-fetch-dest");
  return (mode !== null && mode !== "navigate") || (dest !== null && dest !== "document");
}

/** The authorization request goes to the provider's one endpoint, and carries the state the cookie will. */
function isAuthorizationRequestFor(provider: ExternalProviderKey, authorizeUrl: string, state: string): boolean {
  try {
    const url = new URL(authorizeUrl);
    return `${url.origin}${url.pathname}` === AUTHORIZATION_ENDPOINTS[provider] && url.searchParams.get("state") === state;
  } catch {
    return false;
  }
}

/**
 * GET /api/auth/oauth/{provider}/start — where "Fortsätt med Google" points (#1744, ADR 0142 D8).
 *
 * A GET because it is reached by an `<a href>`, never a form: the CSP's `form-action 'self'` would
 * refuse a form that navigates to the provider. It reads no account and writes nothing durable: the
 * api mints a ten-minute flow, and this binds the browser to it with the state cookie. ADR 0018's
 * rule against state changes on a GET names this route as one of its two exceptions.
 */
export async function GET(
  request: NextRequest,
  { params }: { params: Promise<{ provider: string }> }
): Promise<NextResponse> {
  const provider = toExternalProviderKey((await params).provider);
  if (provider === null) return redirectTo(ENTRY);
  if (isPrefetch(request) || isEmbedded(request)) {
    return new NextResponse(null, { status: 204, headers: { "Cache-Control": "no-store" } });
  }

  // Guarded here and again where the target is used: a link anyone can build ends in a redirect.
  const rawNext = request.nextUrl.searchParams.get("next");
  const next = rawNext ? cookieSafeNext(safeRedirectPath(rawNext)) : "";

  let res: Response;
  try {
    res = await fetch(`${env.BACKEND_URL}/api/v1/auth/oauth/${provider}/start`, {
      method: "POST",
      headers: { ...pickForwardedHeaders(request.headers), "Content-Type": "application/json" },
      body: JSON.stringify({ next }),
      cache: "no-store",
    });
  } catch {
    return notCompleted(provider);
  }
  if (!res.ok) return notCompleted(provider);

  const parsed = startResponseSchema.safeParse(await res.json().catch(() => null));
  if (!parsed.success || !isAuthorizationRequestFor(provider, parsed.data.authorizeUrl, parsed.data.state)) {
    return notCompleted(provider);
  }

  const response = redirectTo(parsed.data.authorizeUrl);
  setOAuthState(response, parsed.data.state);
  return response;
}
