import "server-only";
import { headers } from "next/headers";

/**
 * The two forwarding headers the API consumes. `UseForwardedHeaders` is registered with
 * `XForwardedFor | XForwardedProto` (`Api/Program.cs`), and nothing reads `X-Forwarded-Host`,
 * so relaying that one would hand the API a value it has not opted into trusting.
 */
const RELAYED = ["x-forwarded-for", "x-forwarded-proto"] as const;

/** The minimum a header bag has to offer to be readable here — `Headers` and `next/headers` both satisfy it. */
type HeaderSource = { get(name: string): string | null };

/**
 * Relays the inbound forwarding headers verbatim, or nothing.
 *
 * RELAY, NEVER APPEND — Next is one hop but must not look like one. Caddy is the only
 * component that writes `X-Forwarded-For` here: since 2.5 it IGNORES an inbound value from an
 * untrusted peer and SETS the header to the TCP client address, and this edge configures no
 * `trusted_proxies`, so exactly one entry crosses it. Adding our own hop would make the chain
 * two deep against the API's `ForwardLimit: 1`, and the value the API then trusted would be
 * the web container's, not the client's — the same collapse this whole change exists to undo.
 *
 * OMIT, NEVER SYNTHESISE — with no header the API's middleware is a no-op and the client IP
 * falls back to the connection address, which for an internal call IS the right answer. A
 * fabricated value would be a lie the rate limiter and the auth audit trail both believe.
 */
export function pickForwardedHeaders(source: HeaderSource): Record<string, string> {
  const relayed: Record<string, string> = {};
  for (const name of RELAYED) {
    const value = source.get(name);
    if (value !== null && value !== "") {
      relayed[name] = value;
    }
  }
  return relayed;
}

/**
 * {@link pickForwardedHeaders} against the ambient request, for call sites that have no
 * `Request` object — server actions, RSC data functions, and route handlers declared as
 * `GET()` with no parameter.
 *
 * Returns `{}` outside a request scope. That is not defensive noise: the same fetchers run
 * during build and static rendering, where `headers()` throws by design and where there is no
 * client whose address could be forwarded. The try wraps ONLY the `headers()` call, so a bug
 * in the relay itself still surfaces.
 */
export async function forwardedHeaders(): Promise<Record<string, string>> {
  let inbound: HeaderSource;
  try {
    inbound = await headers();
  } catch {
    return {};
  }
  return pickForwardedHeaders(inbound);
}
