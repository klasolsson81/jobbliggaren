/**
 * Prune the i18n catalog to only the namespaces client components actually use,
 * for the payload passed to `NextIntlClientProvider` (#740 — perf finding
 * `b1-full-i18n-catalog-hydrated`).
 *
 * The full 21-namespace catalog (~141 KB minified) is serialized into every
 * document's RSC Flight payload. Server rendering keeps the full catalog (via
 * `getTranslations`, unaffected — `request.ts` still returns everything); only
 * the CLIENT provider is trimmed. Grep-verified (see the PR) that no client
 * component (`useTranslations`) references a stripped namespace:
 *
 * - `content-*` — the marketing pages render this copy server-side only
 *   (`(marketing-inner)/*` via `getTranslations`); no client consumer. Stripped
 *   by prefix so future `content-*` namespaces are trimmed automatically.
 * - `metadata` — `generateMetadata` / `manifest.ts` only (server).
 * - `errors` — server actions only (`lib/actions/*` via `getTranslations`).
 * - `admin` — used client-side ONLY inside the `(admin)` group (`admin-nav.tsx`
 *   + the admin tables/filters). Stripped from the root provider; the `(admin)`
 *   layout re-provides it via `includeAdmin` (React context replaces, not
 *   merges, so that provider passes the full client set + admin).
 *
 * Any client component that starts using one of these namespaces must add it
 * back here (next-intl surfaces a `MISSING_MESSAGE` if it does not).
 *
 * Generic over the caller's message type (what `getMessages()` returns) so the
 * result stays assignable to `NextIntlClientProvider`'s `messages` prop. The
 * returned object intentionally has fewer top-level namespaces than the type
 * advertises — that is the point (the server type is a superset of what the
 * client carries); the `as T` records that intent.
 */
export function pickClientMessages<T extends Record<string, unknown>>(
  messages: T,
  { includeAdmin = false }: { includeAdmin?: boolean } = {}
): T {
  return Object.fromEntries(
    Object.entries(messages).filter(([namespace]) => {
      if (namespace.startsWith("content-")) return false;
      if (namespace === "metadata" || namespace === "errors") return false;
      if (namespace === "admin") return includeAdmin;
      return true;
    })
  ) as T;
}
