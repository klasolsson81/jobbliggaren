/**
 * Scope the `NextIntlClientProvider` payload to the namespaces a provider
 * boundary's client subtree actually uses (#737 — the sequel step the audit
 * finding `b1-full-i18n-catalog-hydrated` left open after #740).
 *
 * #740 stripped the server-only namespaces from ONE global payload. That still
 * serialized all 13 remaining namespaces (~102 KB minified) into every
 * document's RSC Flight payload, which is why `resource-summary:document:size`
 * failed its ADR 0045 budget on 8/8 measured URLs (40–46 KB against 30 KB).
 * A payload is a property of the PROVIDER BOUNDARY, not of the app.
 *
 * Server rendering is unaffected — `request.ts` still returns the full catalog
 * and `getTranslations` reads all of it. Only the CLIENT provider is scoped.
 *
 * ## Contract
 *
 * Every `NextIntlClientProvider` in the app passes a payload built here, with
 * the namespaces its boundary needs written as an ARRAY LITERAL at the call
 * site. The fitness function `client-namespace-payload.test.ts` computes each
 * boundary's reachable namespaces from the import graph and asserts the
 * declaration EQUALS it — so a declaration is a measured fact, not a guess:
 *
 *   - too small → a client component reads a namespace the provider does not
 *     carry, which is a blank / `MISSING_MESSAGE` at runtime on that route;
 *   - too large → the payload silently re-inflates toward the 102 KB this
 *     change removed (ADR 0045 Beslut 6 is a non-regression ratchet).
 *
 * Nesting note: React context REPLACES rather than merges, so a nested provider
 * carries its boundary's full set — it does not inherit the root's. The root
 * boundary's own set is empty, which matters: whatever root carries is paid by
 * EVERY document on top of the boundary's own set.
 *
 * The server-only namespaces are filtered here as a hard floor rather than left
 * to the declarations — they can never legitimately appear in a client payload,
 * so no boundary should have to remember them:
 *   - `content-*` — marketing copy rendered server-side only (stripped by
 *     prefix, so future `content-*` namespaces are covered automatically);
 *   - `metadata` — `generateMetadata` / `manifest.ts`;
 *   - `errors` — server actions (`lib/actions/*`).
 *
 * Generic over the caller's message type so the result stays assignable to
 * `NextIntlClientProvider`'s `messages` prop. The returned object intentionally
 * has fewer top-level namespaces than the type advertises — that is the point
 * (the server type is a superset of what any client boundary carries); the
 * `as T` records that intent.
 */

/** Namespaces that must never reach a client payload (see the doc comment). */
export function isServerOnlyNamespace(namespace: string): boolean {
  return namespace.startsWith("content-") || namespace === "metadata" || namespace === "errors";
}

export function pickClientMessages<T extends Record<string, unknown>>(
  messages: T,
  namespaces: readonly string[]
): T {
  const wanted = new Set(namespaces);
  return Object.fromEntries(
    Object.entries(messages).filter(
      ([namespace]) => wanted.has(namespace) && !isServerOnlyNamespace(namespace)
    )
  ) as T;
}
