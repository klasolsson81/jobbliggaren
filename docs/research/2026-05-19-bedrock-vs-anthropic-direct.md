# Discovery: Bedrock EU vs Anthropic Direct API — AI-provider-strategi inför AWS-exit

**Datum:** 2026-05-19
**Status:** Discovery + agent-rond klar. STOPP 3 — Klas-beslut bekräftat (se §7), Block 4-grindar definierade.
**Kontext:** Block 3 i post-Fas-3 + pre-migration-discovery-sessionen. Block 1 (budget-höjning) skippad (Klas-beslut). Block 2 (hosting) avgjort: Hetzner CX32 + Vercel + Cloudflare, full AWS-exit juni 2026.
**Scope:** Ingen kod skriven. Inga spec-/ADR-filer ändrade. Ren discovery + tre agent-domar.

---

## 1. Avgörande sakläge (verifierat denna session)

- **AI-lagret är INTE byggt.** `Grep` för `IAiProvider|BedrockRuntime|AWSSDK.Bedrock|Anthropic` i `*.cs` = 0 träffar. Fas 4 (AI Layer) = `Planerad`. Detta är ett **greenfield Fas 4-designval**, ingen migrations-/refaktor-kostnad. Startpromptens "nuvarande: AWSSDK.BedrockRuntime, abstraktions-port som redan finns" är faktiskt fel.
- **BUILD.md §8 designar redan en dual-provider-port:** `IAiProvider`, `IAiProviderResolver.ResolveForUserAsync` (väljer systemnyckel→Bedrock EU vs BYOK→Anthropic Direct), `enum AiProviderKind { BedrockClaude, AnthropicDirect }`. §3.1: `AWSSDK.BedrockRuntime` 4.x (systemnyckel) + officiell `Anthropic` NuGet 12.x (BYOK). §9.5/§9.6 specar båda flödena.
- **Ingen fristående Bedrock-EU-ADR finns.** EU-AI-residency-åtagandet lever i **BUILD.md §9.6** ("Data stannar inom EU oavsett källregion") + **CLAUDE.md §5.3** (anti-pattern: "systemnyckel ska alltid EU-routas via Bedrock") + **privacy-policy subprocessor-lista §13.4** ("Anthropic — BYOK-flöde, frivilligt, US").

### Startprompt-fel som korrigeras (relevanta för Block 4)

| Startprompt | Verklighet |
|---|---|
| "ADR 0007 — Bedrock EU via inference profiles" | ADR 0007 = `branch-protection-fas0`. Ingen Bedrock-EU-ADR existerar. |
| "ADR 0005 — cost-protection-and-launch-gating" | ADR 0005 = `go-to-market-strategy` (innehåller kostnadsskyddet). |
| "ADR 0019 — direct-push-to-main" | ADR 0019 = `solo-direct-push-to-main`. |
| "ny ADR sannolikt 0033 (deployment-migration)" | ADR 0033 = `migrate-cli-mode-dispatch` (REDAN TAGEN). Serien går till 0049. **Nästa lediga = 0050.** |

---

## 2. Pricing (web-verifierat 2026-05, efter pris-justering 2026-04-01)

| Modell (per 1M tokens, in/out) | Bedrock EU (cross-region +~10%) | Anthropic Direct API |
|---|---|---|
| Claude Sonnet 4.6 | $3,30 / $16,50 | $3,00 / $15,00 |
| Claude Haiku 4.5 | $1,10 / $5,50 | $1,00 / $5,00 |
| Claude Opus 4.7 | $5,50 / $27,50 | $5,00 / $25,00 |
| Prompt caching | upp till 90% off cached input | upp till 90% off cached input |
| Batch | 50% off | 50% off |

**Anthropic Direct ≈ 10% billigare** än Bedrock EU (Bedrock EU bär cross-region-premien; Bedrock standard-region == Anthropic Direct-pris). Källor: §8.

---

## 3. GDPR / data-residency (web-verifierat 2026-05)

| | Bedrock EU (inference profile) | Anthropic Direct API |
|---|---|---|
| Data-residency | Äkta EU (eu-north-1 call, EU-profil, data stannar EU) | **US-only** på self-serve/enterprise-tier. EU-residency endast via custom enterprise-avtal. |
| Transfer-mekanism | Ingen tredjelandsöverföring (EU-intern) | Restricted transfer Kap. V — kräver SCC modul 2 + Schrems II-TIA |
| DPA | Bärs av AWS DPA | Separat Anthropic-DPA (by-reference på self-serve) + Microsoft-subprocessor (in-scope sedan 2026-01-07) |
| Jurisdiktion | EU | US — CLOUD Act / FISA 702-exponering (flaggat i källor) |

---

## 4. dotnet-architect-dom (dispositiv)

- **Ej arkitektur-ändring.** Port-konfig inom redan korrekt §8-design. Delta om systemnyckel→Anthropic Direct: `IAiProvider`/`IAiProviderResolver`/`AiProviderKind` = 0 ändringar; 1 konfig-rad (`SystemProvider`); `BedrockClaudeProvider` + `AWSSDK.BedrockRuntime` **byggs aldrig**; `AnthropicDirectProvider` byggs ändå för BYOK och återanvänds.
- **Resolver-dispatchen består** — axeln är credential/tenancy-gräns (plattform vs BYOK), inte vendor. `AiProviderKind`-enum-namngivning blir Fas 4-fråga.
- **Ops-hygien:** Anthropic Direct entydigt renare på icke-AWS-box (ingen moln-SDK-tether för ett enda anrop; ren `IHttpClientFactory` + API-nyckel). Att inte bygga Bedrock-grenen = YAGNI korrekt.
- **MEN:** flytt av systemnyckel till US-routing rör **CLAUDE.md §5.3 + BUILD.md §171-spärr** → spec-amendment + ADR + Klas-GO, ej konfig-justering.
- **Byggbeslut defereras till Fas 4** (Last Responsible Moment / YAGNI). Strategisk riktning kan Klas sätta nu. Ej TD (§9.6 — roadmap, ej debt).

## 5. security-auditor-dom (VETO-makt GDPR, inga MVP-undantag)

US-systemnyckel-AI **BLOCKERAD som tyst default-för-alla** — INTE juridisk omöjlighet. Villkorat tillåten endast under **kumulativ stack, alla fem, innan en Fas 4-kodrad skrivs för US-systemnyckel:**

1. **DPIA (Art. 35)** — blockerande. Storskalig systematisk CV+ansökningsdata (ev. Art. 9-känslig) + tredjelandsöverföring + AI-profilering passerar tröskeln entydigt.
2. **SCC modul 2 + dokumenterad Schrems II-TIA (CLOUD Act, EDPB Rec. 01/2020) + Anthropic-DPA + Microsoft-subprocessor-täckning** arkiverade. **DPF-status web-verifieras** (§9.5 — gissa ej).
3. **Privacy-policy + BUILD.md §13.4 omskriven & versionerad i `user_consents` INNAN flippen är live** — ingen falsk publicerad text under övergången.
4. **Art. 25 privacy-by-default:** US-default får ej vara tyst. Renast: **US opt-in även för systemnyckel** (paritet BYOK).
5. **ADR 0049-interaktion:** decrypt-före-AI = klartext-CV-PII över Atlanten — fält-krypteringen (TD-13 per-användar-DEK KMS-envelope) neutraliseras vid AI-tidpunkten. Måste namnges i DPIA+TIA + ADR 0049-cross-ref/amendment. **adr-keeper-trigger.**

Saknas något av 1–5 → GDPR-blocker, inga MVP-undantag.

## 6. senior-cto-advisor-dom (decision-maker)

- **Klas:s riktning (Anthropic Direct, ingen Bedrock) är förenlig med Clean Architecture + båda agent-domarna och godkänns som strategisk riktning.** Den ändrar dock ett **publicerat residency-löfte** → **§9.6 strategiskt fas-skifte → Klas-STOPP** (ej konfig-val, ej CC-direktbeslut).
- **Auditorns 5 villkor står fast som icke-förhandlingsbar Fas 4-grind.** CTO override:ar inte GDPR-veto (CLAUDE.md §12). Principiellt override-utrymme finns endast på villkor 4 (design-val, ej legalitet) — och CTO:s dom är emot override där.
- **US opt-in även för systemnyckel — entydig dom. Ingen US-default.** GDPR Art. 25.2 + Saltzer–Schroeder least privilege. ~10% kostnadsfördel väger INTE mot Art. 25 (kostnad ≠ legitim grund för mer ingripande default).
- **Medveten kostnad Klas ska se klart:** med Bedrock helt borta finns **ingen EU-residency-fallback** för systemnyckel-AI. Systemnyckel-AI blir **enbart opt-in** — icke-opt-in-användare får **ingen** systemnyckel-AI alls. Den UX-konsekvensen ska stå explicit i DPIA + samtyckesdesign.
- **Avvisat (Regel 4):** "konfig-justering ej ADR" (ändrar publicerat löfte — granskningstrail krävs); US-default kostnadsdrivet (Art. 25); bygga Bedrock-adapter "för säkerhets skull" (YAGNI, får aldrig konsument).

---

## 7. Klas-beslut (uttalat denna session) + Block 4-scope

**Klas verbatim:** "jag vill skippa AWS helt, skippa bedrock. jag vill köra Anthropic Direct API — både i FAS 4, samt när vi byter VPS."

**Konsekvens (CTO-klassad §9.6 strategiskt fas-skifte — kräver Klas-STOPP-bekräftelse på punkterna i STOPP 3):**

**Block 4 skapar (denna session, efter STOPP 3-GO):**
- **ADR 0050 — Deployment/infra-migration** (AWS-exit → Hetzner CX32 + Vercel + Cloudflare; Block 2 formaliserat). Status: Proposed.
- **ADR 0051 — AI-provider-strategi** (Bedrock utgår; Anthropic Direct systemnyckel + BYOK; US opt-in även systemnyckel; supersederar EU-routing-åtagandet §170/§5.3; cross-ref ADR 0049). Status: Proposed.
- **Spec-amendment-flaggning** (BUILD.md §139/§170–171/§212/§916/§938/§1103–1109/§1616 + CLAUDE.md §5.3 rad 275 + §9.5 rad 447 + privacy-policy §13.4). **Kräver Klas-GO + spec-edit-approve-mekanism (Klas kör hooken själv) — CC self-godkänner aldrig (memory).** Block 4 *flaggar*, applicerar ej spec-edits utan Klas-mekanism.
- Verbatim ADR-prosa levereras av webb-Claude (memory-direktiv) — CC begär källtext via STOPP innan fil-write.

**Block-4-blockerande GDPR-leverabler (innan Fas 4 öppnar — spåras i current-work/steg-tracker, EJ tech-debt.md):**
- DPIA Art. 35; SCC modul 2 + Schrems II-TIA (CLOUD Act) + Anthropic-DPA + Microsoft-subprocessor + DPF-status; versionerad privacy-policy/§13.4; ADR 0049-interaktion namngiven.

**Defereras till Fas 4 (ej TD — greenfield-roadmap i rätt fas, §9.6):** Anthropic-adapter-bygge, opt-in-UX/samtyckesflöde-implementation, `AiProviderKind`-enum-namngivning.

---

## 8. Källor (web-verifierat 2026-05-19)

- [Amazon Bedrock Pricing – AWS](https://aws.amazon.com/bedrock/pricing/)
- [Claude API Pricing – Anthropic](https://platform.claude.com/docs/en/about-claude/pricing)
- [AWS Bedrock Pricing 2026 (TokenMix)](https://tokenmix.ai/blog/aws-bedrock-pricing)
- [Anthropic Claude API EU Alternative — CLOUD Act / GDPR Art.46 (sota.io)](https://sota.io/blog/anthropic-claude-api-eu-alternative-gdpr-cloud-act-2026)
- [Anthropic vs OpenAI: GDPR Compliance 2026 (aipolicydesk)](https://www.aipolicydesk.com/blog/anthropic-vs-openai-gdpr-compliance-2026)
- [Claude (Anthropic) GDPR Compliance Guide (WAIMAKERS)](https://www.waimakers.com/en/resources/gdpr-compliance/claude-anthropic)
- [Claude Enterprise: EU Data Residency & GDPR (Compound Law)](https://compound.law/en-DE/tools/claude-enterprise/)
