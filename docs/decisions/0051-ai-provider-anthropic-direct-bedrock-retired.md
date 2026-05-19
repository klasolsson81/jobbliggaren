# ADR 0051 — AI-provider-strategi: Bedrock utgår, Anthropic Direct API för systemnyckel + BYOK

**Status:** Proposed
**Datum:** 2026-05-19
**Kontext:** Post-Fas-3 + pre-migration-discovery (Block 3). Konsekvens av full AWS-exit (ADR 0050) + Klas-direktiv "det måste inte vara EU-baserat allt".
**Beslutsfattare:** Klas Olsson (riktnings-GO 2026-05-19); senior-cto-advisor (§9.6 decision-maker); security-auditor (GDPR-veto-villkor, icke-förhandlingsbara); dotnet-architect (greenfield-design-dom)
**Relaterad:** ADR 0049 (TD-13 PII-fält-kryptering — **decrypt-före-AI-interaktion, cross-ref**); ADR 0050 (AWS-exit — denna ADR möjliggör ren exit); ADR 0005 (kostnadsskydd — Bedrock-deny moot). Underlag: `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md` (tre agent-domar §4–7). BUILD.md Bilaga B planerad `NNNN-bedrock-eu-for-system-key.md` — denna ADR fyller den slotten med **inverterad** slutsats (Bedrock formaliseras EJ; utgår).

> **Livscykel-not:** Skriven 2026-05-19 av Claude Code på explicit Klas-begäran
> (medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen för
> denna session). Besluts-substansen är transkriberad verbatim från
> dotnet-architect-/security-auditor-/senior-cto-advisor-domarna +
> discovery-rapporten — inga nya beslut konstruerade. Status **Proposed**:
> Accepted-flip kräver separat Klas-GO. **Spec-amendments som denna ADR
> implicerar (BUILD.md/CLAUDE.md/privacy-policy) appliceras EJ av CC** —
> kräver Klas spec-edit-approve-mekanism (CC self-godkänner aldrig, memory).

---

## Kontext

JobbPilots AI-lager (Fas 4) är **inte byggt** (`Grep IAiProvider|Bedrock|
Anthropic` i `*.cs` = 0 träffar). BUILD.md §8 *specificerar* (ej implementerar)
en dual-provider-port: `IAiProvider`, `IAiProviderResolver.ResolveForUserAsync`
(systemnyckel→Bedrock EU vs BYOK→Anthropic Direct), `enum AiProviderKind
{ BedrockClaude, AnthropicDirect }`. EU-residency-åtagandet för systemnyckel
lever i **BUILD.md §9.6/§170–171 + CLAUDE.md §5.3** (anti-pattern: "systemnyckel
ska alltid EU-routas via Bedrock") + **privacy-policy subprocessor-lista §13.4**
("Anthropic — BYOK-flöde, frivilligt, US"). Ingen fristående Bedrock-EU-ADR
finns (startpromptens "ADR 0007 supersession" var moot).

Full AWS-exit (ADR 0050) gör Bedrock-vägen till en kvarvarande AWS-tether.
Klas-direktiv 2026-05-19: skippa AWS helt, skippa Bedrock, kör Anthropic
Direct API i Fas 4 + på VPS. Web-verifierat 2026-05: Anthropic Direct ≈ 10%
billigare än Bedrock EU (Sonnet $3/$15 vs $3,30/$16,50) men **US-only** på
self-serve/enterprise-tier; EU-residency endast via custom enterprise-avtal.

## Beslut

### Beslut 1 — Bedrock utgår; Anthropic Direct API för båda vägar

`AWSSDK.BedrockRuntime` och en `BedrockClaudeProvider`-adapter **byggs aldrig**.
Både systemnyckel-vägen och BYOK-vägen talar Anthropic Direct API (HTTP +
officiell `Anthropic` NuGet). Per dotnet-architect: detta är **ingen
arkitektur-ändring** — det är en provider-resolver-konfiguration inom den
redan korrekt designade §8-porten (delta = 1 konfig-rad + ett aldrig-byggt
adapter). `IAiProviderResolver` består: dispatch-axeln är credential/tenancy
(plattformens nyckel vs användarens BYOK-nyckel), **inte vendor** — den
gränsen är reell även när båda grenar talar samma vendor.

### Beslut 2 — US opt-in även för systemnyckel; ingen US-default

Systemnyckel-AI får **inte** vara en tyst US-default för alla användare.
Användaren måste aktivt opt-in:a till US-processing innan plattformens nyckel
skickar dennes data till Anthropic (paritet med befintligt BYOK-samtyckes­mönster).
Entydig senior-cto-advisor-dom mot GDPR Art. 25.2 (privacy-by-default) +
Saltzer–Schroeder least privilege: den minst ingripande behandlingen är
förvald. ~10% kostnadsfördel **väger inte** mot Art. 25 — kostnad är ingen
legitim grund att flytta default till mer ingripande behandling.

**Medveten produktkonsekvens (Klas-bekräftad):** med Bedrock helt borta finns
**ingen EU-residency-fallback**. Systemnyckel-AI blir **enbart opt-in** — en
användare som inte samtycker till US-processing får ingen systemnyckel-AI
alls (endast BYOK om egen nyckel finns). Denna UX-konsekvens ska stå explicit
i DPIA + samtyckesdesign.

### Beslut 3 — Fem icke-förhandlingsbara GDPR-villkor som Fas-4-grind

security-auditor (GDPR-veto, inga MVP-undantag) — alla fem kumulativa,
uppfyllda **innan en Fas-4-kodrad skrivs för US-systemnyckel-AI**:

1. **DPIA (Art. 35)** — blockerande. Storskalig systematisk CV+ansökningsdata
   (ev. Art. 9-känslig) + tredjelandsöverföring + AI-profilering.
2. **SCC modul 2 + dokumenterad Schrems II-TIA (CLOUD Act, EDPB Rec. 01/2020)
   + Anthropic-DPA + Microsoft-subprocessor-täckning** arkiverade.
   **DPF-status web-verifieras** (§9.5 — gissa ej).
3. **Privacy-policy + BUILD.md §13.4 omskriven & versionerad i `user_consents`
   INNAN flippen är live** — ingen falsk publicerad text under övergången.
4. **Art. 25:** US ej tyst — opt-in även systemnyckel (= Beslut 2).
5. **ADR 0049-interaktion namngiven i DPIA + TIA + ADR 0049-cross-ref/amendment**
   (se Beslut 4).

CTO bekräftar: ingen av 1–5 är override-bar (GDPR-veto, CLAUDE.md §12).
Override-utrymme fanns principiellt endast på villkor 4 (design ej legalitet)
— CTO:s dom är emot override där.

### Beslut 4 — ADR 0049-interaktion: decrypt-före-AI = klartext-PII över Atlanten

ADR 0049 fält-krypterar `cover_letter`, `application_notes.content`,
`follow_ups.note`, `resume_versions.content` (per-användar-DEK KMS-envelope,
crypto-erasure). Ett AI-anrop **måste dekryptera** dessa före prompt-konstruktion.
Med Anthropic Direct (US): decrypt i EU → **klartext-CV-PII över Atlanten till
US-jurisdiktion**. ADR 0049:s at-rest-skydd sträcker sig **inte** till
AI-transfer-vägen — det måste namnges som riskerad behandling i DPIA + TIA,
och ADR 0049 får en cross-ref/amendment som dokumenterar gränsen.
Sammanfaller med ADR 0050:s KMS-rehoming-blocker (AWS-exit tar bort KMS) —
båda kräver krypto-design-omgång före Fas 4 / migration.

## Konsekvenser

### Positiva

- Möjliggör ren AWS-exit (ADR 0050 Beslut 1) — ingen AWS-SDK-tether för ett
  enda anrop (Fowler Gateway-hygien; YAGNI — Bedrock-grenen får aldrig konsument).
- Enklare AI-lager: ett gateway-mönster (HTTP/Anthropic NuGet) i hela lagret
  istället för två (SigV4/IAM + HTTP).
- ~10% lägre token-kostnad än Bedrock EU.
- Provider-port (§8) bevisat korrekt designad — absorberar bytet utan
  arkitektur-ändring.

### Negativa

- **EU-residency-garantin för systemnyckel försvinner helt** — systemnyckel-AI
  blir enbart opt-in; icke-opt-in-användare får ingen systemnyckel-AI.
- DPIA-tyngd: CLOUD Act-TIA + SCC + DPA-arbete krävs före Fas 4 (ej kod —
  juridik/compliance-leverabel).
- Publicerat residency-löfte (§13.4, §170) dras tillbaka — kräver
  granskningstrail (denna ADR) + versionerad policy-uppdatering.

### Mitigering

- Fem GDPR-villkor (Beslut 3) som hård Fas-4-grind, spårad i
  current-work/steg-tracker (ej tech-debt.md — det är Fas-4-roadmap, ej debt).
- Opt-in-design (Beslut 2) håller default privacy-by-design trots US-bortfall
  av EU-alternativ.
- ADR 0049-cross-ref/amendment + DPIA-namngiven prompt-PII-minimering
  (ai-prompt-engineer-koordinering i Fas 4) som TIA-kompletterande åtgärd.

## Alternativ övervägda

- **Behåll Bedrock EU för systemnyckel (status quo-design):** Avvisad per
  Klas-direktiv + ADR 0050 ren exit. Bedrock EU = äkta EU-residency men
  AWS-tether + ~10% dyrare + död kod efter AWS-exit.
- **Hybrid (Bedrock EU kvar på AWS, övrigt migreras):** Avvisad — bevarar
  AWS-konto/IAM för ett anrop (ADR 0050 Beslut 1).
- **US-default för systemnyckel (kostnadsdrivet):** Avvisad — GDPR Art. 25.2
  + Saltzer–Schroeder; kostnad ≠ legitim grund för mer ingripande default
  (CTO Regel-4-avvisning).
- **"Konfig-justering, ingen ADR":** Avvisad — ändrar publicerat
  residency-löfte; Ford/Parsons/Kua: publicerade fitness-egenskaper kräver
  granskningstrail.
- **Bygg Bedrock-adapter "för säkerhets skull":** Avvisad — YAGNI; får aldrig
  konsument efter AWS-exit.

## Implementationsstatus

**Proposed.** Ingen kod skriven (AI-lagret är 0 rader, Fas 4). Byggbeslut
(adapter-implementation, opt-in-UX, samtyckesflöde, `AiProviderKind`-namngivning)
**defereras till Fas 4** per dotnet-architect Last-Responsible-Moment/YAGNI —
**ej TD** (greenfield-roadmap i rätt fas, §9.6; TD-listan är inte
dumpning-ställe). Fas 4 kräver egen strategisk Klas-GO + ren `/clear` (§9.2).
GDPR-villkoren (Beslut 3) + spec-amendments är Fas-4-**blockerande**, spårade
i current-work/steg-tracker.

## Validering

Uppskjuten till Fas 4: DPIA-genomförande, SCC/TIA/DPA-arkivering,
DPF-status-web-verifiering, versionerad policy-uppdatering — alla gröna innan
första AI-kodrad. Format-/cross-ref-validering: adr-keeper (Block 4).

## Relaterade beslut

- **ADR 0049** — TD-13 PII-fält-kryptering. **Cross-ref:** decrypt-före-AI
  exponerar klartext-PII över Atlanten (Beslut 4); ADR 0049:s at-rest-skydd
  täcker ej AI-transfer. Ev. amendment vid Fas-4-design.
- **ADR 0050** — AWS-exit. Denna ADR möjliggör Beslut 1 (ren exit).
  KMS-rehoming-blockern (ADR 0050 Öppen fråga) + decrypt-före-AI (Beslut 4)
  är samma krypto-design-omgång.
- **ADR 0005** — kostnadsskydd. Bedrock-deny-Budget-Action blir moot
  (Bedrock byggs aldrig). Relevans-skifte, ej supersession.
- **BUILD.md Bilaga B** — planerad `NNNN-bedrock-eu-for-system-key.md` fylls
  med inverterad slutsats. adr-keeper uppdaterar "Planerade ADRs".
- **Spec-amendment-karta (FLAGGAD, ej applicerad — Klas spec-edit-approve):**
  BUILD.md §139, §170–171, §212, §916, §938, §1103–1109, §1616; CLAUDE.md
  §5.3 (anti-patternraden "systemnyckel ska alltid EU-routas via Bedrock" blir
  felaktig) + §9.5; privacy-policy §13.4 + samtyckestext. **Radnummer
  agent-rapporterade — verifieras innan någon edit.**

## Referenser

- `docs/research/2026-05-19-bedrock-vs-anthropic-direct.md` — tre agent-domar
  (§4 architect, §5 security-auditor, §6 CTO), web-verifierade priser/residency
- security-auditor GDPR-dom 2026-05-19 (denna session) — 5 villkor, veto
- senior-cto-advisor §9.6-dom 2026-05-19 (denna session) — decision-maker
- dotnet-architect greenfield-design-dom 2026-05-19 (denna session)
- GDPR Art. 25.2 (privacy-by-default), Art. 35 (DPIA), Art. 44–46 (Kap. V);
  Schrems II (C-311/18); EDPB Rec. 01/2020 (TIA)
- Robert C. Martin, *Clean Architecture* (2017) kap. 11 (DIP), 22–23 (YAGNI);
  Saltzer & Schroeder (1975) least privilege; Ford/Parsons/Kua (2017)
  fitness functions; Fowler *PoEAA* (Gateway)
- ADR 0005 / 0049 / 0050 · CLAUDE.md §5.3, §9.2, §9.6, §12 · BUILD.md §8
