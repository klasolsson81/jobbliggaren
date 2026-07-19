# ADR 0029 — HTTP-auth-pipeline och `IClaimsTransformation`-disciplin

**Datum:** 2026-05-11
**Status:** Accepted · role-resolution *mechanism* superseded-in-part by #746 PR-B (2026-07-19) — see Amendment below
**Kontext:** Fas 2 polish-block — TD-60-stängning efter H-3 SoC-split (role-fetch flyttad från `SessionAuthenticationHandler` till `SessionRoleClaimsTransformation`)
**Beslutsfattare:** Klas Olsson (efter senior-cto-advisor-triage 2026-05-11 Block C, TD-60-defer)
**Relaterad:** ADR 0008 (Mediator pipeline-ordning, komplementär), ADR 0017 (frontend auth-pattern, opaque session-id), ADR 0022 (audit-log + marker-interface), ADR 0024 D1 (audit-bypass-port-allowlist-pattern), ADR 0028 (admin authorization defense-in-depth, **komplementär — supersedas inte**)

## Amendment (2026-07-19, #746 PR-B — perf audit #737 d2/d4)

The role-resolution **mechanism** this ADR documents has moved. `SessionRoleClaimsTransformation` (the
`IClaimsTransformation` that resolved roles on *every* authenticated request) is **deleted**; role
resolution is now performed **on demand** by `AdminRoleAuthorizationHandler`
(`AuthorizationHandler<AdminRoleRequirement>` in `Jobbliggaren.Api.Authorization`) **only when the Admin
policy is evaluated**. Motive: the sole consumer of `ClaimTypes.Role` is the Admin path, so the eager
per-request fetch was pure waste on the non-admin fan-out (2 identity queries × every request, ×8 on
`/oversikt`; and a 429'd flood paid it per rejected request). The handler attaches `ClaimTypes.Role` to
the request principal (in endpoint routing `AuthorizationHandlerContext.User` is the same reference as
`HttpContext.User`), so the defense-in-depth chain (HTTP policy + Mediator `AdminAuthorizationBehavior`
via `ICurrentUser.IsInRole`) is unchanged.

The **decisions this ADR made stand**: per-request resolution (immediate-revoke, no cache — A1); the
401-vs-403 pipeline discipline (now via `RequireAuthenticatedUser()`); and conscious review of
privilege-granting auth extension points. Only **where** in the pipeline roles resolve changed — a policy
handler under `UseAuthorization`, not an `IClaimsTransformation` after authentication. The guard follows:
`ClaimsTransformationAllowlistTests` is emptied (any new transformation still breaks the build) and the
privilege surface is now locked by `AuthorizationHandlerAllowlistTests`. Beslut 1's pipeline diagram
(`UseAuthentication → IClaimsTransformation → UseAuthorization`) is superseded by
`UseAuthentication → UseAuthorization(AdminRoleAuthorizationHandler resolves+attaches) → endpoint`.

## Kontext

ADR 0028 etablerade Fas 1-stängningens admin-authorization-modell: Alt A1 per-request roll-fetch, defense-in-depth-dubbel-gate (HTTP-policy + Mediator-behavior), marker-interface `IAdminRequest`, bootstrap-seeder, och separata `Roles.Admin`/`AuthorizationPolicies.Admin`-konstanter. Implementationen lade roll-fetch direkt i `SessionAuthenticationHandler.HandleAuthenticateAsync` (ADR 0028 §Implementation rad 208 — ADR 0028 anger fil-path `src/JobbPilot.Api/Authentication/SessionAuthenticationHandler.cs`; faktisk fil ligger i `src/JobbPilot.Infrastructure/Auth/SessionAuthenticationHandler.cs`. ADR 0028 är immutable post-Accepted, korrigering noterad här).

Fas 2 polish-block 2026-05-11 levererade **H-3 SoC-split**: role-fetch-logiken flyttades från `SessionAuthenticationHandler` till en ny `SessionRoleClaimsTransformation : IClaimsTransformation`. ADR 0028:s kärnbeslut (A1 per-request, defense-in-depth, marker, bootstrap, konstant-separation) är oförändrade — bara **var i HTTP-pipelinen** roll-claims populeras har flyttats.

H-3 gjorde tre saker explicit som tidigare var implicit:

1. **HTTP-auth-pipelinen är en separat pipeline från Mediator-pipelinen.** ADR 0008 och ADR 0022 dokumenterar Mediator-behavior-ordningen. ADR 0028 §5 utökar den till 6 behaviors. Ingen ADR dokumenterar HTTP-pipelinen — den är ASP.NET-runtime-implicit. Efter H-3 har vi nu två tydliga extension-punkter i HTTP-pipelinen (auth-handler + claims-transformation) som båda kan emit:a privilege-claims. Ordningen mellan dem är säkerhetskritisk.
2. **`IClaimsTransformation` är en konsument-kategori som behöver allowlist-disciplin.** Samma logik som audit-bypass-portar (ADR 0024 D1): en ny transformation kan addera privilege-claims som `RequireRole`/`RequireClaim`-policies konsulterar. Implicit registrering är säkerhetskritisk regression-risk.
3. **Claim-placerings-regeln är inte uppskriven.** Auth-handlern och claims-transformationen har olika ansvar (protokoll-validation vs claim-population från extern data). Utan formaliserad regel kommer framtida tillägg (impersonation Fas 6, federerat IdP, tenant-membership) hamna på fel ställe.

TD-60 lyftes i arch-audit Fas 1 Discovery 2026-05-11 Block C (dotnet-architect Minor c+d) som "auth-pipeline-ordning och claims-transformation-disciplin är inte single-source-of-truth-dokumenterad". Denna ADR stänger TD-60.

Kontextuella krafter:

1. **Microsoft Learn — "Claims-based authorization" + "IClaimsTransformation":** ASP.NET kör `IClaimsTransformation.TransformAsync` **efter** authentication-success och **före** authorization-policy-utvärdering. Ordningen är runtime-implicit men inte dokumenterad i kodbasen.
2. **CLAUDE.md §2.1 Clean Architecture:** Authentication-handler och claims-transformation är båda Infrastructure-impl, men har distinkta ansvar. Att blanda dem bryter SRP och försvårar dekomponering (t.ex. när token-validation-latency ska mätas separat från role-fetch-latency).
3. **ADR 0028 §Beslut 1:** "Roll-revoke verkar omedelbart, inte efter session-refresh." Per-request-fetch är säkerhetskritiskt — varje mellanlager (cache, session-payload) introducerar stale-fönster.
4. **ADR 0024 D1 audit-bypass-port-pattern:** strukturell allowlist-test bevisat värde — build bryts vid medveten review-spärr, inte bara linter-varning.
5. **Fas 6-roadmap:** impersonation-flöde och federerat IdP introducerar nya claim-källor som behöver disciplin om var de hör hemma.

## Beslut

### 1. HTTP-pipeline-ordning explicit

JobbPilots HTTP-auth-pipeline är:

```
UseAuthentication (SessionAuthenticationHandler)
  → IClaimsTransformation (SessionRoleClaimsTransformation)
  → UseAuthorization (RequireRole / RequireClaim-policies)
```

ASP.NET Core 10 kör `IClaimsTransformation.TransformAsync` automatiskt efter authentication-success och före authorization-policy-utvärdering. Denna ordning **formaliseras här som JobbPilot-specifik single source of truth**. Förändring av ordningen kräver ny ADR.

Komplementär till ADR 0008 + ADR 0022 + ADR 0028 §5 (Mediator pipeline-behavior-ordning) — de två pipelines är **separata** och dokumenteras i separata ADRs:

- **HTTP-pipelinen** (denna ADR): ASP.NET-middleware-kedja, kör per HTTP-request, populerar `ClaimsPrincipal`.
- **Mediator-pipelinen** (ADR 0008/0022/0028): behavior-kedja runt handlers, kör per Mediator-dispatch (HTTP **eller** Worker/CLI/test), konsumerar `ClaimsPrincipal` via `ICurrentUser`.

Motivering: pipeline-ordningen är säkerhetskritisk eftersom `IClaimsTransformation` kan addera `ClaimTypes.Role` och andra privilege-claims som `RequireRole`-policy konsulterar. En framtida transformation som körs **efter** `UseAuthorization` skulle vara helt verkningslös för auktorisation — och det är inte uppenbart för utvecklare som inte känner ASP.NET-pipelinens default-ordning.

### 2. Claim-placerings-regel — auth-handler vs claims-transformation

**Authentication-handler** (`SessionAuthenticationHandler`) emit:ar **endast claims som kommer direkt från session-protokollet**:

- `ClaimTypes.NameIdentifier` (user-id från Redis Session-record)
- `Sub` (user-id duplicerad för JWT-konvention)
- `session_id_prefix` (de första 6 tecknen av opaque session-id + ellips, för observability — `_value[..6] + "…"` per `SessionId.ToString()`, aldrig hela rå-värdet)

Detta är "vem är detta + session-identitet".

**ClaimsTransformation** (`SessionRoleClaimsTransformation`) emit:ar **claims som kräver extern data-source-lookup**:

- `ClaimTypes.Role` (populerad från `AspNetUserRoles` × `AspNetRoles` via `IUserAccountService.GetRolesAsync(userId, ct)`)
- Sentinel-claim `jobbpilot:roles_resolved` för idempotens (se Beslut 3)

Detta är "vad får detta `vem` göra".

**Regel:** nya security-kritiska claims som kräver extern lookup hör hemma i en **ny eller utvidgad `IClaimsTransformation`-impl**, inte i auth-handlern. Auth-handlern hanterar bara protokoll-validation (session-id-parse, Bearer header-extraktion, Redis-lookup, signatur-verify om/när det införs).

Konkreta framtida exempel:

- **Impersonation-claim (Fas 6):** `impersonated_by`-claim som kräver lookup mot `impersonation_session`-tabellen — ny `IClaimsTransformation`-impl.
- **Federerat IdP-claim-mapping:** mappning från extern IdP-claim till JobbPilot-roll/permission — ny `IClaimsTransformation`-impl.
- **Tenant-membership-claim (om multi-tenant introduceras):** lookup mot `tenant_membership`-tabell — ny `IClaimsTransformation`-impl.

Motivering: SRP (Robert C. Martin, *Clean Architecture* kap. 7). Auth-handler ansvarar för "är denna user autentiserad?". ClaimsTransformation ansvarar för "vilka säkerhets-attribut har denna autentiserad user?". Att blanda gör auth-handlern "fet" och försvårar dekomponering vid framtida observability-behov (token-validation-latency vs role-fetch-latency mätt separat) och vid behov av att stänga av eller mock:a specifika claim-källor i tester.

### 3. Per-request-fetch-disciplin (ingen cache i Fas 1)

`SessionRoleClaimsTransformation` kör per-request DB-query (`GetRolesAsync(userId, ct)`) mot `AspNetUserRoles` × `AspNetRoles`. **Ingen cache** introduceras (inte heller request-scope-cache utöver `UserManager`:s inbyggda dedup).

Idempotens inom samma request säkerställs via sentinel-claim `jobbpilot:roles_resolved`. Ursprunglig implementation använde `HasClaim`-guard på faktiska Role-claims, men dotnet-architect Minor 2026-05-11 identifierade att guarden var otillförlitlig vid mid-session-promotion (om en user just fått sin första roll och guarden testar `HasClaim(ClaimTypes.Role, ...)` på en princip som ännu inte har Role-claims, kör transformation igen — vilket är **rätt** vid mid-session-promotion men gör guarden meningslös för dedup). Sentinel-claim löser detta: sätts unconditionally efter första körning per request, garanterar att den dyra DB-querien körs exakt 1× per request oavsett om rollerna är 0, 1 eller N.

Resilience-detalj: `ct` propageras från `IHttpContextAccessor.HttpContext?.RequestAborted` (security-auditor Major in-block-fix 2026-05-11: resurs-läckage-skydd vid client-disconnect, så role-fetch-querien avbryts om HTTP-requesten cancelleras). Fel-handling: `catch` logs strukturerat + returns principal **utan** Role-claims → authorization-policy fail → 403. Detta håller auth-protokollet stängt (fail-closed) snarare än fail-open vid transient DB-fel.

Motivering (lyft från ADR 0028 §Beslut 1 + senior-cto-advisor-triage 2026-05-11):

- **Security-first:** roll-revoke verkar omedelbart, inte efter cache-TTL eller session-refresh. ADR 0028 §Konsekvenser:Positiva dokumenterar integration-test `GetAuditLog_AfterRoleRevoke_Returns403OnNextRequest` som verifierar detta.
- **Microsoft Learn — "Role-based authorization in ASP.NET Core":** rekommenderar explicit per-request-utvärdering.
- **Mätt overhead:** `UserManager` har inbyggd request-scope-cache som dedupliciterar `GetRolesAsync` inom samma request. 1× DB-query per autentiserat request, inte N×.
- **Cost-mitigering uppskjuten:** vid prestanda-problem (>1000 req/s sustained) kan kort-livad memory-cache (30 s TTL) införas — **separat ADR**, inte aktuellt i Fas 1.

**Trigger för omvärdering** (då separat ADR skrivs):

- Prestanda-mätningar i CloudWatch visar sustained DB-belastning från `AspNetUserRoles`-läsning som korrelerar med p95-latency-degradering.
- Federerat IdP introduceras med slow claim-mapping (då blir cache nödvändig och måste designas mot stale-fönster mot extern IdP).

### 4. Konsument-allowlist-mönstret för `IClaimsTransformation`

Architecture-test `ClaimsTransformationAllowlistTests` (i `tests/JobbPilot.Architecture.Tests/`) låser konsument-listan av `IClaimsTransformation`-impl i Infrastructure-assembly. Allowlist:en är initialt `["SessionRoleClaimsTransformation"]`. Ny `IClaimsTransformation`-impl bryter build:en tills allowlist:en uppdateras explicit.

Direkt analog till audit-bypass-port-pattern i `AuditingLayerTests` (ADR 0024 D1).

Motivering: `IClaimsTransformation` kan addera `ClaimTypes.Role` och andra privilege-claims som `RequireRole`/`RequireClaim`-policies konsulterar. En ny transformation är säkerhetskritisk per definition och måste passera medveten review. Strukturell spärr (arch-test) över policy-dokument (CLAUDE.md eller denna ADR): build bryts om regeln överträds, inte bara linter-varning eller missad code-review.

**Trigger för allowlist-uppdatering:**

- Ny transformation (impersonation-promote-flow Fas 6, test-only role-injector för integration-tester, federerat-IdP-claim-mapping) tillkommer.
- Krav: medveten code-reviewer + security-auditor-pass + uppdatering av `ClaimsTransformationAllowlistTests` + ny ADR-rad om scope är arkitekturellt nytt (t.ex. ny claim-källa eller ny extern beroende).

## Konsekvenser

### Positiva

- **HTTP-pipeline-ordning är dokumenterad single source of truth.** Framtida utvecklare (eller framtida CC-sessioner) kan läsa denna ADR istället för att rekonstruera ordningen från ASP.NET-docs.
- **Auth-handler / claims-transformation SoC är typsäker och arch-test-låst.** Marker-interface-disciplin från ADR 0022/0028 utvidgas till en hel kategori (transformations).
- **Framtida auth-ändringar har ADR att läsa.** Fas 6 impersonation-flöde, federerat IdP, tenant-membership — alla har formaliserad placerings-regel.
- **Per-request-fetch-disciplin formaliserad.** Micro-cache-frestelser kräver ny ADR — beslutet att inte cache:a är medvetet, inte glömt.
- **Komponerar med ADR 0028:s defense-in-depth.** Mediator-behavior `AdminAuthorizationBehavior` konsumerar `ICurrentUser.IsInRole(...)` som i sin tur konsumerar `ClaimTypes.Role` från `SessionRoleClaimsTransformation`. Pipeline-ordning + transformation-disciplin gör att hela kedjan är spårbar.
- **Komponerar med ADR 0024 D1.** Konsument-allowlist-pattern är nu etablerat för två kategorier (audit-bypass-portar + claims-transformations). Generaliserbart till framtida kategorier (Mediator-behavior-allowlist, EF Core interceptor-allowlist).

### Negativa

- **Pipeline-ordningen är ASP.NET-runtime-implicit, inte kompilator-låst.** Architecture-test för "UseAuthentication kommer före UseAuthorization i `Program.cs`" saknas. Möjligt framtida tillägg — i nuläget accepterat som låg risk (ordningen är trivial att verifiera vid code-review och ändringar i `Program.cs`-pipelinen är säkerhetskritiska oavsett).
- **Per-request DB-query-modellen bär en prestanda-skuld vid extrem load.** Accepterad tradeoff (Beslut 3). Trigger för omvärdering dokumenterad.
- **Sentinel-claim-pattern (`jobbpilot:roles_resolved`) är något esoterisk.** Kräver att framtida utvecklare läser kod-kommentaren eller denna ADR för att förstå varför `HasClaim`-guard på Role-claims inte används. Mitigerat av strukturerad logging (transformation logs vid skip + vid run) och unit-tests som verifierar idempotens.

### Trade-offs accepterade

- **Två pipelines (HTTP + Mediator) dokumenteras i separata ADRs.** ADR 0008/0022/0028 för Mediator, ADR 0029 för HTTP. Två-ADR-disciplin över att blanda — pipelines är olika abstraktioner med olika dispatch-vägar och olika observability-behov.
- **Fail-closed på transient DB-fel under role-fetch.** En transient PostgreSQL-felperiod resulterar i 403 för admin-endpoints (rollerna kan inte resolveras → policy fail). Acceptabelt: bättre att neka åtkomst än att glida in i fail-open. Mitigerat av RDS multi-AZ + connection-pool-retry i Npgsql.
- **Sentinel-claim över request-scope-cache i `IMemoryCache`.** Sentinel är simpler och allokerar inga ytterligare resurser per request. `IMemoryCache`-baserad dedup skulle kräva eviction-policy och vara overkill för 1-bit-state.

### Mitigering

- `ClaimsTransformationAllowlistTests` arch-test förhindrar omedveten transformation-registrering.
- Integration-tester i `SessionRoleClaimsTransformationTests` verifierar: anonym request passerar transformation utan effekt (early-return), rolless user → ingen Admin-Role-claim → 403 på admin-endpoint, user med Admin-roll → Role-claim populerad → 200 på admin-endpoint, roll-revoke verkar omedelbart på nästa request (per-request-fetch-disciplin), anonym request till admin-endpoint → 401 (pipeline-ordning UseAuthentication före UseAuthorization).
- Integration-test `GetAuditLog_AfterRoleRevoke_Returns403OnNextRequest` (ADR 0028) verifierar end-to-end att roll-revoke verkar omedelbart även efter SoC-split.
- Sentinel-claim-call-count-verifiering (att `GetRolesAsync` anropas exakt 1× per request) kräver isolerad enhetstest mot transformation-klassen — defererat (Infrastructure.UnitTests-projekt finns inte idag, integration-test-pipelinen exercise:ar inte status-code re-execution). Sentinel-disciplinen försvaras av kod-kommentar i `SessionRoleClaimsTransformation.cs:33-38` + code-reviewer-disciplin vid framtida transformation-tillägg.
- Strukturerad logging i `SessionRoleClaimsTransformation` (`Warning` vid catch:ad exception) syns i Seq lokalt och CloudWatch i staging/prod.

## Alternativ övervägda

### Alt 1 — Behåll role-fetch i `SessionAuthenticationHandler` (pre-H-3-state)

Auth-handlern äger både session-protokoll-validation och role-fetch i samma `HandleAuthenticateAsync`-metod, som ADR 0028 §Implementation initialt specificerade.

**Avvisat.** Skäl:

- **SoC-brott:** handlern blandar protokoll-validation (session-id-parse, Redis-lookup) och claim-population från extern data-source. Två ansvar i samma klass.
- **Försvårar observability-dekomponering:** token-validation-latency och role-fetch-latency kan inte mätas separat utan invasiva ändringar.
- **Försvårar test-isolation:** mock:a bort role-fetch i auth-tester kräver att hela `IUserAccountService` injiceras i handler-tester som egentligen testar session-protokollet.
- **ASP.NET-konvention:** `IClaimsTransformation` är den dedikerade extension-punkten för claim-augmentation efter authentication. Att inte använda den är att gå emot framework-konventionen utan tydlig vinst.

### Alt 2 — Lägg role-claims i Session-record i Redis (ADR 0028 Alt A2)

Roller serialiseras som JSON-fält i `Session`-recorden i Redis. `SessionAuthenticationHandler` läser dem och emit:ar `ClaimTypes.Role` direkt; ingen `IClaimsTransformation` behövs.

**Avvisat.** Redan avvisat i ADR 0028 §Alternativ Alt A2 av samma skäl som gäller här:

- **Stale-fönster:** roller giltiga tills session refreshas (default 7d per ADR 0014/0017). Strider mot Microsoft Learn:s rekommendation om per-request-utvärdering.
- **SRP-brott:** `Session`-record äger session-lifecycle, inte authorization-membership.
- **Cross-cutting kontrakt-ändring:** 7+ touch-points (Session-record, Redis-serializer, login, refresh, logout, impersonation Fas 6, Worker-stub).

Se ADR 0028 §Alternativ Alt A2 för fullständig motivering.

### Alt 3 — Bara dokumentation utan arch-test

Skriv denna ADR + uppdatera CLAUDE.md, men ingen `ClaimsTransformationAllowlistTests`. Förlita oss på code-review och adr-keeper-disciplin.

**Avvisat.** Skäl:

- **Policy-dokument utan strukturell spärr är inte trovärdigt försvar.** ADR 0024 D1 audit-bypass-pattern har bevisat värdet av allowlist-tester: build bryts vid medveten override, inte bara missad code-review eller missad ADR-läsning.
- **Konsistens:** vi har redan allowlist-test för audit-bypass-portar. Att inte göra samma för transformations är inkonsekvent.
- **Regression-risk:** ny transformation kan introduceras av en framtida CC-session som inte läst denna ADR. Arch-testet är "fail-fast"-mekanism.

### Alt 4 — Supersede ADR 0028 helt

Skapa ADR 0029 som supersederar ADR 0028 eftersom claim-placering-detaljen har ändrats.

**Avvisat.** Skäl:

- **ADR 0028:s kärnbeslut är oförändrade:** Alt A1 per-request, defense-in-depth-dubbel-gate, marker-interface `IAdminRequest`, bootstrap-seeder, konstant-separation `Roles` vs `AuthorizationPolicies`. Bara *var* role-claims populeras har flyttats (H-3 SoC-split).
- **Komplementär ADR är rätt verktyg:** ADR 0028 dokumenterar admin-authorization-modellen (vad gäller, hur enforce:as, var startas). ADR 0029 dokumenterar HTTP-pipeline-ordning + claim-placerings-regel + transformation-disciplin (vilken extension-punkt populerar vilka claims). De svarar på olika frågor.
- **Supersession av en Accepted-ADR där 80 % av innehållet fortfarande gäller är förvirrande för läsare.** Bättre att additivt utöka med tydlig korsreferens.
- **Immutable-policyn:** ADR 0028 är immutable. En komplementär ADR (denna) är hur vi addresserar implementations-evolution utan att förlora historisk audit-trail.

## Implementation

ADR är **retrospektiv** — implementationen finns redan i kodbasen post-Block C (commit `35b9dc0` 2026-05-11).

**Infrastructure:**

- `src/JobbPilot.Infrastructure/Auth/SessionAuthenticationHandler.cs` — post-SoC-split: enbart session-id-parse (Bearer header) + Redis-lookup via `ISessionStore` + identity-konstruktion med `NameIdentifier` + `Sub` + `session_id_prefix`-claims. Kommentar rad 24-29 + 82-84 hänvisar till `SessionRoleClaimsTransformation` för Role-claims.
- `src/JobbPilot.Infrastructure/Auth/SessionRoleClaimsTransformation.cs` — `IClaimsTransformation`-impl. Per-request DB-query via `IUserAccountService.GetRolesAsync(userId, ct)`. Idempotent via sentinel-claim `jobbpilot:roles_resolved`. `ct` från `IHttpContextAccessor.HttpContext?.RequestAborted`. Defensiv `is ClaimsIdentity identity`-cast. Catch logs + returns principal utan Role-claims (fail-closed).
- `src/JobbPilot.Infrastructure/DependencyInjection.cs:164-168` — `services.AddScoped<IClaimsTransformation, SessionRoleClaimsTransformation>()` i HTTP-only auth-modul (`AddIdentityAndSessions`).

**Api:**

- `src/JobbPilot.Api/Program.cs:46-60, 183-184` — `AddAuthentication("Bearer", SessionAuthenticationHandler)` + `AddAuthorization(Admin policy = RequireRole(Roles.Admin))`. Pipeline: `app.UseAuthentication()` (rad 183) → `app.UseAuthorization()` (rad 184). ASP.NET kör `IClaimsTransformation` automatiskt mellan dessa.

**Tester:**

- `tests/JobbPilot.Architecture.Tests/ClaimsTransformationAllowlistTests.cs` — allowlist `["SessionRoleClaimsTransformation"]`. 32 arch-tester totalt.
- `tests/JobbPilot.Api.IntegrationTests/Auth/SessionRoleClaimsTransformationTests.cs` — 5 integration-tester: anonym-skip, rolless-no-claim, admin-claim-populerad, per-request-fetch-revoke-immediacy, pipeline-ordning 401-vs-403-distinktion.
- `tests/JobbPilot.Api.IntegrationTests/Admin/AdminAuditLogTests.cs` (förexisterande, ADR 0028) — `GetAuditLog_AfterRoleRevoke_Returns403OnNextRequest` fortsätter passera post-SoC-split.
- Sentinel-claim-call-count-verifiering defererad (kräver Infrastructure.UnitTests-projekt; försvaras idag av kod-kommentar + code-reviewer-disciplin).

**Konfiguration:** ingen ändring — `AdminBootstrap:InitialAdminEmail` från ADR 0028 §Implementation kvarstår oförändrad.

## Status

**Accepted** 2026-05-11 efter:

- senior-cto-advisor-triage 2026-05-11 Block C (dotnet-architect Minor c+d → TD-60-defer-beslut: lyft som ny ADR eftersom claim-placering-disciplin är arkitektur-scope, inte in-block-fix).
- H-3 SoC-split levererad i commit `35b9dc0`.
- security-auditor Major in-block-fix (`ct`-propagation) + dotnet-architect Minor in-block-fix (sentinel-claim-idempotens) verifierade i samma commit.

Omvärderas vid Fas 6 (impersonation-flöde) — denna ADR förväntas hålla, men ny `IClaimsTransformation`-impl för impersonation-claim kommer kräva allowlist-uppdatering + ev. utvidgad claim-placerings-regel om impersonation-claim ska komponera med Role-claims.

## Referenser

- **Microsoft Learn** — ["Claims-based authorization in ASP.NET Core"](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/claims), ["IClaimsTransformation"](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.iclaimstransformation), ["Role-based authorization in ASP.NET Core"](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- **Robert C. Martin**, *Clean Architecture* (2017) kap. 7 (SRP), kap. 22 (Dependency Rule)
- **OWASP ASVS V4** — Access Control Verification Requirements (fail-closed-disciplin, per-request-utvärdering)
- ADR 0008 (Mediator pipeline-ordning) — komplementär domän (Mediator vs HTTP)
- ADR 0017 (frontend auth-pattern) — opaque session-id-modellen som denna ADR bygger vidare på
- ADR 0022 (audit-log + marker-interface) — samma marker/allowlist-disciplin
- ADR 0024 D1 (audit-bypass-port-allowlist-pattern) — direkt mall för `ClaimsTransformationAllowlistTests`
- ADR 0028 (admin authorization defense-in-depth) — **komplementär, supersedas inte**: 0028 dokumenterar marker + dubbel-gate, 0029 dokumenterar var role-claims populeras
- TD-60 (denna ADR stänger TD-60)
- senior-cto-advisor-rapport 2026-05-11 Block C (implicit i chat-trail)
