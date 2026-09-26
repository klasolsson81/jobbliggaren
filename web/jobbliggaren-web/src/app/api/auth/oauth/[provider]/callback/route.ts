import { createHash, timingSafeEqual } from "node:crypto";
import { NextResponse, type NextRequest } from "next/server";
import { getLocale, getTranslations } from "next-intl/server";
import { z } from "zod";
import { AUTH_ERROR_CODES } from "@/lib/auth/auth-error-codes";
import { OAUTH_STATE_COOKIE_NAME, SESSION_COOKIE_NAME, PERSISTENT_MAX_AGE_SECONDS } from "@/lib/auth/cookie-names";
import { buildContinuationDocument } from "@/lib/auth/continuation-document";
import { toExternalProviderKey, type ExternalProviderKey } from "@/lib/auth/external-login";
import { clearLoginFlow, clearOAuthState, setLoginFlow } from "@/lib/auth/external-login-responses";
import { cookieSafeNext, type LoginFlow } from "@/lib/auth/login-flow";
import { LOGIN_CODE_PATH, LOGIN_CONSENT_PATH, LOGIN_ENTRY_PATH } from "@/lib/auth/login-paths";
import { safeRedirectPath } from "@/lib/auth/safe-redirect";
import { SESSION_COOKIE_ATTRIBUTES } from "@/lib/auth/session";
import { loginOutcomeSchema } from "@/lib/dto/login-challenge";
import { env } from "@/lib/env";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";
import { readProblemTitle } from "@/lib/http/problem";

const ENTRY = LOGIN_ENTRY_PATH;
const CODE_STEP = LOGIN_CODE_PATH;
const CONSENT_STEP = LOGIN_CONSENT_PATH;

const echoedNextSchema = z.object({ next: z.string().optional() });

/** What the document sends the browser on to, and what the response sets beside it. */
type Landing = {
  readonly target: string;
  readonly flow?: LoginFlow;
  readonly sessionId?: string;
};

/** Constant time whatever the lengths: equal-length digests, so `timingSafeEqual` never throws. */
function sameState(presented: string, expected: string): boolean {
  const digest = (value: string) => createHash("sha256").update(value, "utf8").digest();
  return timingSafeEqual(digest(presented), digest(expected));
}

const notCompleted = (provider: ExternalProviderKey): Landing => ({
  target: ENTRY,
  flow: { phase: "notice", notice: "externalNotCompleted", provider },
});

async function land(provider: ExternalProviderKey, request: NextRequest): Promise<Landing> {
  const query = request.nextUrl.searchParams;
  // The provider's refusal, or the user's cancel: nothing reaches the api, and nothing of it is shown.
  if (query.has("error")) return notCompleted(provider);

  const code = query.get("code");
  const state = query.get("state");
  const presented = request.cookies.get(OAUTH_STATE_COOKIE_NAME)?.value;
  // The bind (RFC 6749 §10.12): only the browser that started this flow may complete it.
  if (!code || !state || !presented || !sameState(presented, state)) return notCompleted(provider);

  let res: Response;
  try {
    res = await fetch(`${env.BACKEND_URL}/api/v1/auth/oauth/${provider}/callback`, {
      method: "POST",
      headers: { ...pickForwardedHeaders(request.headers), "Content-Type": "application/json" },
      body: JSON.stringify({ code, state }),
      cache: "no-store",
    });
  } catch {
    return notCompleted(provider);
  }

  if (res.status === 400 && (await readProblemTitle(res)) === AUTH_ERROR_CODES.ExternalEmailUnverified) {
    return { target: ENTRY, flow: { phase: "notice", notice: "externalUnverified", provider } };
  }
  if (!res.ok) return notCompleted(provider);

  const body: unknown = await res.json().catch(() => null);
  const outcome = loginOutcomeSchema.safeParse(body);
  if (!outcome.success) return notCompleted(provider);
  const next = echoedNextSchema.safeParse(body).data?.next ?? "";

  switch (outcome.data.outcome) {
    case "signedIn":
      return { target: safeRedirectPath(next), sessionId: outcome.data.sessionId };
    case "consentRequired":
      return {
        target: CONSENT_STEP,
        flow: { phase: "consent", grantToken: outcome.data.grantToken, next: cookieSafeNext(next), via: provider },
      };
    default:
      return { target: CODE_STEP, flow: { phase: "outcome", result: outcome.data, via: provider } };
  }
}

/**
 * GET /api/auth/oauth/{provider}/callback — where the provider sends the browser back (#1744, ADR 0142 D8).
 *
 * Every branch answers 200 with the continuation document, never a redirect and never a 5xx: a
 * `Strict` cookie set on a 3xx in a cross-site chain is not sent on the next hop, and on a 5xx the
 * edge's logger writes the whole request line. The state cookie is cleared on every branch. A
 * missing or mismatched cookie, or the provider's `error`, reaches no backend at all.
 */
export async function GET(
  request: NextRequest,
  { params }: { params: Promise<{ provider: string }> }
): Promise<NextResponse> {
  const provider = toExternalProviderKey((await params).provider);
  const landing: Landing = provider === null ? { target: ENTRY } : await land(provider, request);

  const locale = await getLocale();
  const t = await getTranslations({ locale, namespace: "pages" });
  const titleTemplate = (await getTranslations({ locale, namespace: "metadata" }))("titleTemplate");
  const heading =
    provider === null
      ? t("auth.passwordless.entry.title")
      : t("auth.passwordless.external.title", {
          provider: t(`auth.passwordless.external.providerNames.${provider}`),
        });

  const response = new NextResponse(
    buildContinuationDocument({
      lang: locale,
      title: titleTemplate.replace("%s", heading),
      heading,
      continueLabel: t("auth.passwordless.external.continue"),
      target: landing.target,
    }),
    {
      status: 200,
      headers: {
        "Content-Type": "text/html; charset=utf-8",
        "Cache-Control": "no-store",
        "Referrer-Policy": "no-referrer",
        "X-Robots-Tag": "noindex",
      },
    }
  );

  clearOAuthState(response);
  if (landing.sessionId !== undefined) {
    // Always persistent (ADR 0142 D4), and the flow cookie goes with the login it belonged to.
    response.cookies.set(SESSION_COOKIE_NAME, landing.sessionId, {
      ...SESSION_COOKIE_ATTRIBUTES,
      maxAge: PERSISTENT_MAX_AGE_SECONDS,
    });
    clearLoginFlow(response);
  } else if (landing.flow !== undefined) {
    setLoginFlow(response, landing.flow);
  }
  return response;
}
