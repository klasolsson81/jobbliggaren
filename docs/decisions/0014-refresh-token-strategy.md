# ADR 0014 — Refresh tokens i DB + Redis för access-token jti (avviker från BUILD.md §11.2)

**Datum:** 2026-04-19
**Status:** **Superseded by ADR 0017/0018 (2026-07-25) — never implemented.**
The `refresh_tokens` table was dropped in #504a; the Redis jti-blacklist
(`RedisAccessTokenRevocationStore`) was deleted in #827/#569 together with the rest of the
JWT cluster. Opaque server-side sessions replaced the mechanism wholesale — there is no
refresh token and no access-token jti to revoke. The text below is retained as the record of
a decision that was taken and then abandoned, not as a description of the system. Current
truth: BUILD.md §11.2.

> **Superseded by:** [ADR 0017](./0017-frontend-auth-pattern.md) + [ADR 0018](./0018-cookie-and-csrf-strategy.md)
>
> **Why this needed a status flip and not an amendment (#827):** this ADR declared its own
> authority over the spec — "BUILD.md §11.2 är nu inaktuell … Denna ADR är auktoritativ tills
> dess". #827 rewrote §11.2 to the shipped session model, which ends that authority on the
> ADR's own terms, and removed §11.2's pointer to this ADR — the only cross-reference that
> warned a reader the two disagreed. Leaving it `Accepted` would have left a wholly false ADR
> standing behind that removed warning. Superseded rather than amended because the decision
> was abandoned in full, not drifted from.
>
> **It is not unreferenced, and that is a loose end this change does not close.** Five tracked
> cross-references survive: ADR 0012, ADR 0013, ADR 0017, this index, and — the ones that
> matter — **ADR 0028 and ADR 0029, both `Accepted`, which cite this ADR as authority for
> CURRENT behaviour** ("roller är giltiga tills session refreshas, default 7d per ADR
> 0014/0017"). That 7d figure is wrong twice over: it points at a superseded ADR, and no
> profile in `SessionStoreOptions` uses 7 days (Session 24h/24h; Persistent 30d sliding /
> 180d absolute / 24h rotation). Re-pointing 0028/0029 at BUILD.md §11.2 with the real
> numbers is its own change-reason — tracked as a #827 follow-up, not silently folded in
> here.
**Kontext:** STEG 3 — Auth-stack
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0012, BUILD.md §11.2

## Kontext

BUILD.md §11.2 specificerar "revokation-lista i Redis för refresh tokens". Inför Fas B-implementation har strategin utvärderats mot OWASP ASVS och mot den faktiska ansvarsuppdelningen mellan Redis och Postgres.

Problemet med Redis för refresh tokens: refresh-tokens har 14 dagars livslängd. Redis utan persistens (eller vid krasch/flush) innebär att alla 14-dagars tokens invalideras — alla aktiva användare loggas ut. En redis-krasch under deployment eller OOM-event ger fullständigt session-tapp. För access-tokens (15 min) är detta acceptabelt; för refresh-tokens är det inte det.

OWASP ASVS V3 (Session Management) rekommenderar stateful refresh tokens med rotation: varje `/refresh`-anrop invaliderar gammal token och utfärdar ny. Rotation gör det möjligt att detektera token-stöld (gammal token använd efter rotation = tecken på kompromiss).

## Beslut

**Avvikelse från BUILD.md §11.2.**

- **Refresh tokens** lagras i `refresh_tokens`-tabell (Postgres, `identity`-schema) med kolumnerna: `user_id`, `token_hash` (SHA-256 av raw token-värdet), `expires_at`, `revoked_at`, `replaced_by_token_id`, `created_at`, `created_by_ip`
- **Rotation:** varje `/auth/refresh`-anrop invaliderar gammal token och utfärdar ny (OWASP Token Rotation). Replay-detektering: om en redan roterad token används → revokera hela token-kedjan och logga säkerhetshändelse
- **Refresh token transport:** httpOnly Secure cookie — inte localStorage (XSS-risk, CLAUDE.md §5.2)
- **Access tokens** är stateless JWT (15 min livslängd). Revokation vid logout eller misstänkt kompromiss: Redis jti-blacklist med TTL = tokenets återstående livslängd
- **Redis-rollen** ändras: access-token jti-blacklist (kort-livad data, data-loss acceptabel) istället för refresh-lista (lång-livad data, data-loss oacceptabel)

## Konsekvenser

**Positivt:**

- Robust mot Redis-krasch: refresh-flödet fallback-ar inte på Redis, ingen full logout vid Redis-outage
- Full audit-trail för refresh-events: vilken IP, när rotation skedde, chain av `replaced_by_token_id`
- OWASP ASVS V3-compliant: stateful refresh + rotation + replay-detektering
- httpOnly cookie eliminerar XSS-exponering av refresh token

**Negativt:**

- Refresh-anrop gör ett DB-anrop (latens ~1 ms lokalt — försumbart för Fas 0–1-volymer)
- `refresh_tokens`-tabellen växer över tid och behöver en cleanup-jobb (expired tokens)
- BUILD.md §11.2 är nu inaktuell och behöver uppdateras manuellt av Klas

**Mitigering:**

- Hangfire-bakgrundsjobb (Fas 1) rensar `revoked_at IS NOT NULL OR expires_at < NOW()` nattligen
- BUILD.md §11.2 patchas i separat session av Klas. Denna ADR är auktoritativ tills dess

## Alternativ övervägda

**Alt 1 — Refresh-lista i Redis (BUILD.md-spec):** Avfärdat. Data-loss risk vid Redis-krasch = full logout för alla aktiva användare (14-dagars tokens). Ingen audit-trail för refresh-events. Redis passar inte för lång-livad token-data utan persistens + replikering (vilket ökar komplexitet och kostnad).

**Alt 2 — Stateless refresh (JWT-format refresh token):** Avfärdat. Omöjligt att revokera enskild token utan stateful komponent. Strider mot OWASP ASVS V3. Rotation och replay-detektering är inte genomförbara.

**Alt 3 — Långlivade access-tokens (timmar istället för 15 min):** Avfärdat. Bredare attackfönster vid token-stöld — komprometterad token kan utnyttjas tills den löper ut. Eliminerar värdet av Redis jti-blacklist (meningslöst med TTL på timmar).

## Implementationsstatus

**Beslutsdatum:** 2026-04-19 (session 8, inför Fas B — Auth-stack)

**Ej implementerat än:** Fas B implementerar: `refresh_tokens`-tabell via Identity-migration, `/auth/login`, `/auth/refresh`, `/auth/logout` endpoints, Redis jti-store med StackExchange.Redis.

**Nästa steg:** Fas B-implementation sker direkt efter ADR-skrivning. BUILD.md §11.2 uppdateras av Klas i separat pass för att reflektera att Redis hanterar access-token jti, inte refresh-tokens.
