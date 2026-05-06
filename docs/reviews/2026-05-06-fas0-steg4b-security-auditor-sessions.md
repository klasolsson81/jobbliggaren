# Security Advisory: ISessionStore (STEG 4b Turn 3)

**Date:** 2026-05-06
**Role:** Advisory only (no code changes)
**Auktoritet:** OWASP ASVS v4.0.3 §3 (Session Management), GDPR Art. 5/25/32, CLAUDE.md §5.4, ADR 0017, ADR 0018
**Granskat artefakt:** Föreslagen design för `ISessionStore` (Redis-backed sessions) inför implementation i STEG 4b Turn 3.

---

## Q1: Session-id entropy

### Findings

- **256 bitar är mer än tillräckligt.** OWASP ASVS V3.2.2 kräver ≥64 bits effektiv
  entropi och rekommenderar 128 bits för session-tokens. NIST SP 800-63B §5.1.1.2
  rekommenderar minst 64 bits för "look-up secrets". 256 bits från
  `RandomNumberGenerator.GetBytes(32)` ligger klart över alla relevanta tröskelvärden.
- `RandomNumberGenerator.GetBytes` är en CSPRNG (på Windows: BCryptGenRandom; på
  Linux: getrandom/`/dev/urandom`). Detta är rätt API. Använd **inte** `Random`,
  `Guid.NewGuid()` (V4 GUIDs har bara ~122 bits effektiv entropi och är inte
  garanterat krypto-säkra på alla plattformar) eller `RNGCryptoServiceProvider`
  (deprecated, ersatt av `RandomNumberGenerator`).
- **Base64Url är rätt val** för session-id som dubbelanvänds som Redis-nyckel
  och som värde i `Authorization: Bearer`-headers. Det är RFC 4648 §5,
  URL-säkert (inga `+`/`/`/`=`), och kompatibelt med RFC 6750 (Bearer-token
  syntax tillåter alfa-numeriska + `-._~+/`).
- 32 bytes → 43 base64url-tecken utan padding. Konsekvent längd, gör
  log-prefix-sanering förutsägbar (Q4).
- `Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode` är den
  kanoniska .NET-implementationen och finns redan i ASP.NET Core-trädet —
  ingen extra dependency, ingen kontroversiell tredjepart. OK.

### Recommendation

**Approved as designed.** 256 bits CSPRNG-entropi via `RandomNumberGenerator`,
base64url-kodat med `WebEncoders`. Inga ändringar krävs.

Ett litet förstärkande tips: deklarera storleken som en `const int` (t.ex.
`SessionIdByteLength = 32`) i `SessionIdGenerator` snarare än ett magic
number — gör eventuell framtida höjning explicit i diff.

---

## Q2: Redis key hashing — should session-id be hashed at rest?

### Findings

Det här är den viktigaste frågan i hela rapporten. Tre olika hot:

1. **Redis-dump exponerar levande sessioner.** Om en angripare får läsa Redis
   (misconfig, SSRF mot Redis CONFIG/KEYS, ElastiCache-snapshot som hamnar i
   fel S3-bucket, intern logg som dumpat `KEYS *`-output, AWS-IAM-fel), så
   ger den raw key:n direkt åtkomst till alla aktiva användarkonton. Detta
   är _exakt_ samma klass av problem som okrypterade lösenord i databas och
   är väl-känt (se OWASP "Session Management Cheat Sheet": "Session
   identifiers should be considered as sensitive as credentials").
2. **Operativ exponering.** AWS ElastiCache slow-log, `MONITOR`-kommandot,
   tredjeparts-Redis-GUI:n (RedisInsight, Medis), backup-script som loggar
   nycklar — alla dessa läcker raw-nycklar in i operations-spåret som
   typiskt har lägre skyddsklass än applikationsspåret.
3. **Vår egen tooling.** Inte hypotetiskt — JobbPilots dev-miljö använder
   Docker Compose Redis utan auth (bekräftat i frågan). En `docker exec
   redis redis-cli KEYS jobbpilot:session:*` ger varje utvecklare full
   impersonation-kapacitet om det körs mot dev-data delad med kollega.

**Mitigationer som _inte_ räcker:**
- 14-dagars TTL — räcker inte. Aktiv användare = giltig token i 14 dagar
  rullande. En dump fångar live-sessioner.
- 256-bit entropi — irrelevant. Brute-force är inte hotet; vi lämnar ut
  klartext-nyckeln frivilligt om vi inte hashar.

**Performance-cost för SHA-256 av 32 bytes:** ~200 nanosekunder på modern
CPU, helt försumbart jämfört med Redis round-trip (~5-15 ms).

**HMAC vs ren SHA-256:**
- Ren SHA-256(session_id) räcker eftersom input redan har 256 bits
  CSPRNG-entropi. Det finns inget rainbow-table-hot mot 256-bit random
  input. **HMAC behövs inte här** — det skulle introducera en nyckel-hantering
  som är onödig.
- Om vi senare lägger till ett "pepper" från Secrets Manager (för att
  ytterligare separera Redis-dump från användbara tokens om _både_ Redis och
  app-secrets läcker) — då blir det HMAC-SHA-256. Det är en Fas 1-konversation,
  inte en STEG 4b-konversation.

### Recommendation

**TODO — implementera nu, inte i Fas 1.**

Motivering för "nu, inte senare":
- Cost är en handfull rader kod (`SHA256.HashData(Encoding.UTF8.GetBytes(id))`
  + base64url-kod tillbaka till en sträng för Redis-nyckeln, med raw id som
  Bearer-värde).
- Att lägga till hashing efter att session-formatet är "i bruk" är en
  migration som måste hantera koexistens av hash-format och raw-format —
  betydligt jobbigare än att göra rätt från början.
- Det är _inte_ en BLOCKER för dev/localhost (där skyddsbehovet är lågt),
  men eftersom STEG 4b syftar till "produktionsfärdig auth innan Fas 1"
  och cost är ~10 minuter, finns ingen rationell anledning att vänta.

**Föreslagen implementation:**
```csharp
private static string Key(string sessionId)
{
    Span<byte> hash = stackalloc byte[32];
    SHA256.HashData(Encoding.UTF8.GetBytes(sessionId), hash);
    return $"session:{WebEncoders.Base64UrlEncode(hash)}";
}
```

Kalla denna i `SetAsync`/`GetAsync`/`RefreshAsync`/`InvalidateAsync`. Raw
session-id går aldrig till Redis som nyckel, men förblir det som klienten
har i sin cookie och skickar som Bearer.

**Konsekvens för befintligt mönster:** `RedisAccessTokenRevocationStore`
använder `revoked-jti:{jti}` raw. JTI är OK att lämna raw eftersom JTI är
ett offentligt fält i en signerad JWT (signaturen är hemligheten, inte
JTI:t). Session-id däremot _är_ själva hemligheten. Olika regler.

**Rekommendation till Klas:** Lägg in hashing i denna PR. Notera i ADR 0017
under "Consequences" att session-id hashas vid storage.

---

## Q3: Redis connection security

### Findings

**Dev (localhost via Docker Compose):**
- Acceptabelt utan TLS och utan auth, **förutsatt att Redis-porten inte
  exponeras utanför Docker-nätverket**. Verifiera att `docker-compose.yml`
  använder intern bridge-network och _inte_ publicerar 6379 på host-interfacet
  (eller endast på `127.0.0.1:6379`, inte `0.0.0.0:6379`).
- Connection-string kommer från `appsettings.Development.json` — inte från
  miljövariabel som standard. Det är OK för dev. Det viktiga är att samma
  config-key (`ConnectionStrings:Redis`) i prod-config-källan kommer från
  Secrets Manager.

**Prod (AWS Fas 1):**
- TLS **ska** krävas. ElastiCache stöder TLS-only-läge (`ssl=true` i
  StackExchange.Redis ConfigurationOptions).
- Auth **ska** krävas. ElastiCache stöder Redis AUTH (`password=...` i
  connection-string) eller IAM-auth (kräver provider-side stöd, mer komplext
  — börja med Redis AUTH).
- Connection-string hämtas från Secrets Manager via app:ens IAM-roll —
  detta är redan beskrivet i CLAUDE.md §11 och behöver inte återupprepas.

**Bör appen vägra starta utan TLS?**

Ja, i prod. Inte i dev. Detta är klassisk "fail closed in production" —
samma mönster som vi (lite längre fram) kommer behöva för att kräva
HTTPS-cookies, KMS-encryption, etc. Implementationsmönster:

```csharp
// Pseudocode, lägg i DependencyInjection.cs efter raden som läser opts.Configuration
if (env.IsProduction())
{
    var parsed = ConfigurationOptions.Parse(redisConnectionString);
    if (!parsed.Ssl)
    {
        throw new InvalidOperationException(
            "Redis connection string must enable TLS (ssl=true) in production.");
    }
    if (string.IsNullOrEmpty(parsed.Password))
    {
        throw new InvalidOperationException(
            "Redis connection string must include AUTH password in production.");
    }
}
```

Detta är en cheap startup-check, inte runtime-overhead.

### Recommendation

**For STEG 4b (Fas 0):** Acceptabelt att dev kör Redis utan TLS/auth.
Verifiera att `docker-compose.yml` _inte_ publicerar Redis-porten på
`0.0.0.0`. Om den gör det → MAJOR, fixa innan merge.

**TODO för Fas 1 (prod-readiness, dokumentera nu):**
1. Fail-closed startup-check som ovan, gated på `env.IsProduction()`.
2. ElastiCache provisioneras med TLS-only och Redis AUTH.
3. Password-värdet kommer från Secrets Manager, connection-string-skelettet
   från app-konfig.
4. Lägg in i `docs/runbooks/aws-setup.md` (eller motsvarande) när Fas 1
   AWS-setup beskrivs.

Ingen BLOCKER för STEG 4b givet att Docker Compose är intern.

---

## Q4: Session-id in logs

### Findings

**6-tecken-prefix räcker för korrelation, är inte återanvändbart som token.**

- 6 base64url-tecken = 36 bits sökrymd. Vid 10 000 samtidiga sessioner är
  kollisionsannolikheten ~10 000² / 2³⁷ ≈ 7 × 10⁻⁴ — högt nog att tappa
  unikhet i loggar vid större trafik. För Fas 0 (få användare) räcker det.
  För Fas 1 → överväg 8 tecken (48 bits) eller logga `userId` parallellt.
- 6 tecken är **inte** användbart för att rekonstruera de återstående 37
  tecknen — 256 - 36 = 220 bits återstående entropi. Helt säkert.
- Föredra dock att logga `userId` (Guid) som primary correlation key och
  session-prefix som sekundär, eftersom userId är stabilt över sessions
  och redan är vad vi vill korrelera på i support-fall.

**Enforcement:**

- **Statisk analys:** Roslyn analyzer som flaggar `ILogger.Log*`-anrop där
  ett argument heter `sessionId`/`session_id` och inte är wrappat i en
  prefix-funktion. Detta är möjligt men icke-trivialt att skriva. Inte
  värt det för en kodbas av denna storlek **om** vi har ett alternativ.
- **Arkitekturell enforcement (rekommenderat):** Skapa en
  `SessionId`-value-object (`readonly record struct`) med:
  - `private string Value`
  - `public string Reveal()` — explicit och sökbar metod, används endast i
    Redis-store och cookie-skrivning
  - `public override string ToString() => $"{Value[..6]}…"` — så att
    `_logger.LogInformation("Session {Id} created", sessionId)` automatiskt
    blir log-säker
  - Implicit cast till string **förbjudet**.

  Detta är "make wrong code look wrong"-mönstret. Cost: en fil, ~30 rader.
  Värde: GDPR-säkerhet by design, inte by code-review.

- **Backup: code-review-regel.** Lägg in i `code-reviewer`-agentens checklist
  (om inte redan): "Session-id, JWT, refresh-token, OAuth-token: aldrig
  loggas raw — verifiera ToString-implementation maskerar."

### Recommendation

**Implementera `SessionId` value-object i denna PR.** Cost ~30 rader, gör
hela logging-frågan till en non-issue strukturellt. Detta är inte
"abstraction-cost för dess egen skull" — det är defensive programming
för en GDPR-känslig sträng.

`SessionIdGenerator.Generate()` returnerar `SessionId`. `ISessionStore.GetAsync`
tar `SessionId`. Cookie-läsning i Next.js-proxy → backend tar emot raw
sträng från Bearer-headern och wrappar i `SessionId.FromHeader(string)`
direkt i Authorization-handlern.

Inget BLOCKER om Klas föredrar att skjuta value-object till Fas 1 — då
gäller code-review-regel + grep-baserad pre-commit-hook (`git diff |
grep -i 'sessionId'` mot `LogInformation|LogDebug|LogError`).

---

## Q5: Timing attack on GetAsync

### Findings

**Det här är en non-issue givet 256-bit entropi.**

Resonemang:
1. Hotet skulle vara: angripare ber backend slå upp `GetAsync(guess)` och
   mäter tidsskillnaden mellan miss och hit för att enumerera giltiga
   session-id:n.
2. För att ens ha en _kollision_ på första byte (8 bits) behöver angriparen
   256 försök i genomsnitt. För att hitta hela sessionen: 2²⁵⁵ försök i
   genomsnitt. Tidsskillnaden mellan miss/hit (kanske 1-10 ms) är
   irrelevant — angriparen får aldrig en hit alls.
3. Timing-attacker är farliga när jämförelseproceduren har _per-byte_
   exit (`memcmp`/string equality som ger upp tidigt). Redis GET av en
   nyckel är **inte** sådan jämförelse — Redis hashar nyckeln och slår
   upp i sin interna hash-tabell. Det finns ingen byte-by-byte
   prefix-leak på en hash-uppslagning. Hela nyckeln måste matcha.

**JSON-deserialisering:**
- Variabel tid baserat på record-storlek: ja, men `Session`-recorden
  innehåller bara `UserId (16 bytes Guid) + 2 timestamps` ≈ konstant
  storlek. Ingen meningsfull variation.
- Om sessions senare innehåller listor, claims, etc. — separat fråga.
  Inte relevant nu.

**Constant-time-jämförelse:**
- Inte nödvändigt här. Constant-time är för _hemlighet-jämförelse_ (HMAC
  verify, password verify). Här är hela poängen att vi gör ett **lookup**,
  inte en jämförelse — Redis säger ja/nej baserat på sin egen hash-tabell.

**En relaterad sak att notera (inte direkt din fråga):**
Om vi implementerar Q2-rekommendationen (SHA-256 av session-id som Redis-nyckel),
så blir _även_ en hypotetisk timing-attack mot Redis-hash-uppslagning
ointressant — angriparen mäter tid mot en hash-value, inte mot session-id:t,
och har ingen väg att rekonstruera input.

### Recommendation

**Approved as designed.** Ingen åtgärd krävs.

Dokumentera gärna i en kort kommentar i `RedisSessionStore.GetAsync`:
```csharp
// Timing-säkerhet: Redis GET är hash-tabell-uppslagning, inte byte-jämförelse.
// 256-bit session-id-entropi gör enumeration via timing oexploaterbar.
```

Detta för att framtida läsare inte återkommer med samma fråga.

---

## Q6: GDPR / PII in session store

### Findings

**Är `UserId (Guid)` PII?**

- Under GDPR Art. 4(1) definieras personuppgifter som "any information
  relating to an identified or identifiable natural person". Recital 26
  klargör att pseudonymiserade data som **kan kopplas tillbaka** till en
  individ är personuppgifter.
- En `UserId` Guid är pseudonymt men trivialt linkable till `User.Email`
  via en SQL-join i samma system. **Den är därmed PII enligt GDPR.**
  Praktiskt sett: behandla den som PII.

**Vad innebär det för designen?**

- **Lawful basis:** Nödvändig för att utföra avtalet (Art. 6(1)(b)) —
  utan session kan användaren inte använda tjänsten. Ingen separat
  consent krävs. ✓
- **Storage location:** Redis i EU (ElastiCache eu-north-1 i Fas 1, lokal
  Docker i dev). ✓ (men verifiera prod-region när ElastiCache provisioneras)
- **Encryption at rest:** AWS KMS standard räcker för session-data — det
  är inte "high-sensitivity" som CV eller OAuth-tokens (BYOK). KMS-default
  är acceptabelt per CLAUDE.md. ✓
- **Encryption in transit:** TLS i prod (se Q3). ✓
- **Retention:** 14-dagars TTL → automatisk deletion vid expiry. ✓
- **Right to erasure (Art. 17):** Behöver `InvalidateAllForUserAsync`. Den
  är planerad till Fas 1 enligt din beskrivning. **Detta är OK för STEG
  4b** eftersom kontot inte kan raderas i Fas 0 — dvs det finns ingen
  väg där en användare kan utöva erasure-rätten innan implementationen
  finns. Men:
  - **TODO för Fas 1:** Account-deletion endpoint **måste**
    `InvalidateAllForUserAsync` synkront innan SQL DELETE bekräftas.
    Att kvarlämna sessioner pekande på en raderad userId = orphan-data
    + GDPR-bug.
  - Implementation: Redis SCAN är dyr; lös det med ett sekundärindex
    `user-sessions:{userId}` → `Set<sessionId>` parallellt. Lägger man
    in detta nu (i denna PR) blir Fas 1-implementationen trivial.
- **Audit log:** Session create + invalidate ska in i audit-log per
  CLAUDE.md §5.4 / Område 6. **Verifiera att audit-log-skrivning är
  planerad för session-livscykel-events.** Om inte → MAJOR att lägga in
  innan merge.
- **PII i logs:** Endast userId loggas (per Q4-rekommendation). userId är
  PII men loggas redan i andra spår (audit-log) — konsekvent och
  acceptabelt. Inget email, namn, IP utan rationale.

**Record-strukturen i sig:** `{ UserId, CreatedAt, ExpiresAt }` är
minimal-data per design. Bra. Inget behov att lägga till
`UserAgent`/`IPAddress` om det inte finns ett **definierat** säkerhetssyfte
(t.ex. anomaly-detection vid Fas 1-rate-limiting). Defaulta till data-minimalism.

### Recommendation

**Approved with two TODOs för Fas 1:**

1. **TODO (Fas 1):** Implementera `InvalidateAllForUserAsync` som del av
   account-deletion-flödet. Synkront innan SQL DELETE commits. Right to
   erasure är inte förhandlingsbart.
2. **TODO (Fas 1, valfritt nu):** Sekundärindex `user-sessions:{userId}`
   för att undvika SCAN. Att lägga till nu = trivialt; att lägga till
   senare = migration. Min rekommendation: gör det i denna PR om scope
   tillåter, annars dokumentera tydligt.

**Möjlig MAJOR att verifiera:** Audit-log-skrivning för session create /
invalidate. Om saknas → flagga som MAJOR. Om planerat → noted, inget
problem.

---

## Summary: BLOCKER vs TODO

### Blockers (ingen)

Inga BLOCKER:s identifierade i den föreslagna designen. Designen är
strukturellt sund.

### Recommended in this PR (förstärkningar att inkludera nu, inte senare)

| # | Item | Effort | Rationale |
|---|------|--------|-----------|
| R1 | **SHA-256-hasha session-id som Redis-nyckel** (Q2) | ~10 rader | Cheap-now, expensive-later. Skyddar mot Redis-dump-läckage. |
| R2 | **`SessionId` value-object** med maskerande `ToString()` (Q4) | ~30 rader | Strukturell GDPR-säkerhet. Förhindrar log-PII via design, inte via vaksamhet. |
| R3 | **Sekundärindex `user-sessions:{userId}`** (Q6) | ~15 rader | Möjliggör trivial Fas 1-implementation av account-deletion (right to erasure). |
| R4 | **Verifiera Docker Compose Redis** inte exponeras på `0.0.0.0:6379` (Q3) | 1 min check | Triviall verifiering. Om exponerad → MAJOR. |
| R5 | **Verifiera audit-log-integration** för session create/invalidate (Q6) | Verifikation | Om saknas → MAJOR att lägga in. |

### TODOs (acceptable to defer to Fas 1, dokumentera explicit)

| # | Item | Trigger |
|---|------|---------|
| T1 | Fail-closed startup-check: kräv `ssl=true` + AUTH-password i prod (Q3) | Fas 1 AWS-setup |
| T2 | ElastiCache med TLS-only + Redis AUTH (Q3) | Fas 1 AWS-setup |
| T3 | `InvalidateAllForUserAsync` invokerad synkront i account-deletion-flow (Q6) | När account-deletion implementeras |
| T4 | PwnedPasswords-integration (redan känd, inte denna fråga) | Fas 1, MAJOR-1 från 2026-04-20-audit |
| T5 | Rate-limiting på `/auth/login` (ADR 0017 noterar redan) | Fas 1 |

### Eskalerat till Klas

- **R1 (Redis key hashing):** Beslut om att inkludera nu vs senare. Min
  rekommendation: nu. Klas avgör.
- **R2 (`SessionId` value-object):** Beslut om abstraktionsnivå. Min
  rekommendation: nu, eftersom det är defensive design för en GDPR-känslig
  sträng. Klas avgör.

Samtliga övriga punkter är antingen icke-förhandlingsbara TODOs (Fas 1)
eller verifikationer som inte ändrar designen.

---

**Re-review krävs inte** för att gå vidare med implementation, förutsatt
att R1-R5 antingen inkluderas i PR:n eller explicit dokumenteras som
medvetet uppskjutna. Vid implementation: be `code-reviewer` köra normal
review; jag (security-auditor) gör en kort post-implementation pass på
session-id-hantering, log-output och audit-log-integration när PR är öppen.
