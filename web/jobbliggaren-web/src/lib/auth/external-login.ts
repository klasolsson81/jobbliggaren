/**
 * The external identity providers (ADR 0142 D8, #1744). A plain module: the route handlers, the
 * login flow's schema and the provider rows all read it.
 */

/** MIRROR of the backend `ExternalProviderKey.Known`: the only keys a route segment may carry. */
export const EXTERNAL_PROVIDER_KEYS = ["google"] as const;

export type ExternalProviderKey = (typeof EXTERNAL_PROVIDER_KEYS)[number];

/** A segment is interpolated into a backend URL only once it is one of the known keys, exactly. */
export function toExternalProviderKey(raw: unknown): ExternalProviderKey | null {
  return EXTERNAL_PROVIDER_KEYS.find((key) => key === raw) ?? null;
}

/** MIRROR of the backend `ExternalLoginPolicy.StateTtl`: the state cookie must not outlive the flow. */
export const OAUTH_STATE_MAX_AGE_SECONDS = 10 * 60;

/** The one URL each provider's authorization request may point at; a start answers nothing else. */
export const AUTHORIZATION_ENDPOINTS: Readonly<Record<ExternalProviderKey, string>> = {
  google: "https://accounts.google.com/o/oauth2/v2/auth",
};

/** An `<a href>`, never a form and never `next/link`: a prefetch must not mint a flow. */
export function externalLoginStartHref(provider: ExternalProviderKey, next: string): string {
  const path = `/api/auth/oauth/${provider}/start`;
  return next === "" ? path : `${path}?next=${encodeURIComponent(next)}`;
}
