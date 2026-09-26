import "server-only";
import { cookies } from "next/headers";
import { LOGIN_FLOW_COOKIE_NAME } from "@/lib/auth/cookie-names";
import {
  decodeLoginFlow,
  encodeLoginFlow,
  maxAgeSecondsFor,
  type LoginFlow,
} from "@/lib/auth/login-flow";

// `__Host-` forces Secure, Path=/ and no Domain. Strict because every step is reached same-site;
// the link landing arrives cross-site and deliberately never reads this cookie (ADR 0142 D2).
export const LOGIN_FLOW_COOKIE_ATTRIBUTES = {
  httpOnly: true,
  secure: true,
  sameSite: "strict",
  path: "/",
} as const;

export function nowEpochSeconds(): number {
  return Math.floor(Date.now() / 1000);
}

export async function readLoginFlow(): Promise<LoginFlow | null> {
  const cookieStore = await cookies();
  return decodeLoginFlow(cookieStore.get(LOGIN_FLOW_COOKIE_NAME)?.value);
}

/** Replaces whatever phase was there: the name is shared, so one phase never sits beside another. */
export async function writeLoginFlow(flow: LoginFlow): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.set(LOGIN_FLOW_COOKIE_NAME, encodeLoginFlow(flow), {
    ...LOGIN_FLOW_COOKIE_ATTRIBUTES,
    maxAge: maxAgeSecondsFor(flow, nowEpochSeconds()),
  });
}

/** `set` with the full attribute set, never `delete`: a `__Host-` cookie is only overwritten by a
 *  Set-Cookie that still satisfies the prefix (`deleteSessionCookie` does the same). */
export async function clearLoginFlow(): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.set(LOGIN_FLOW_COOKIE_NAME, "", { ...LOGIN_FLOW_COOKIE_ATTRIBUTES, maxAge: 0 });
}
