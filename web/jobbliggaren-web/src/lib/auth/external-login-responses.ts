import "server-only";
import type { NextResponse } from "next/server";
import { LOGIN_FLOW_COOKIE_NAME, OAUTH_STATE_COOKIE_NAME } from "@/lib/auth/cookie-names";
import { OAUTH_STATE_MAX_AGE_SECONDS } from "@/lib/auth/external-login";
import { encodeLoginFlow, maxAgeSecondsFor, type LoginFlow } from "@/lib/auth/login-flow";
import { LOGIN_FLOW_COOKIE_ATTRIBUTES, nowEpochSeconds } from "@/lib/auth/login-flow-cookie";

// The external login's route handlers set every cookie on the response they return, so what a test
// reads with `getSetCookie()` is what the browser is served.

/** `__Host-` (Secure, Path=/, no Domain), HttpOnly, and Lax: the provider sends the browser back cross-site. */
const OAUTH_STATE_COOKIE_ATTRIBUTES = {
  httpOnly: true,
  secure: true,
  sameSite: "lax",
  path: "/",
} as const;

export function setOAuthState(response: NextResponse, state: string): void {
  response.cookies.set(OAUTH_STATE_COOKIE_NAME, state, {
    ...OAUTH_STATE_COOKIE_ATTRIBUTES,
    maxAge: OAUTH_STATE_MAX_AGE_SECONDS,
  });
}

/** `set` with the full attribute set and Max-Age 0, never `delete`: only a Set-Cookie that still
 *  satisfies the `__Host-` prefix overwrites the cookie. */
export function clearOAuthState(response: NextResponse): void {
  response.cookies.set(OAUTH_STATE_COOKIE_NAME, "", { ...OAUTH_STATE_COOKIE_ATTRIBUTES, maxAge: 0 });
}

export function setLoginFlow(response: NextResponse, flow: LoginFlow): void {
  response.cookies.set(LOGIN_FLOW_COOKIE_NAME, encodeLoginFlow(flow), {
    ...LOGIN_FLOW_COOKIE_ATTRIBUTES,
    maxAge: maxAgeSecondsFor(flow, nowEpochSeconds()),
  });
}

export function clearLoginFlow(response: NextResponse): void {
  response.cookies.set(LOGIN_FLOW_COOKIE_NAME, "", { ...LOGIN_FLOW_COOKIE_ATTRIBUTES, maxAge: 0 });
}
