# Code-review: Fas 1 Block A — sub-block A2 (JobSeeker profil-edit-yta)

**Status:** Approve med Minor-noter
**Granskat:** 2026-05-10
**Auktoritet:** CLAUDE.md §4 (TypeScript/Next.js) + §5.2 (FE anti-patterns)
**Scope:** 6 nya + 1 utökad fil under `web/jobbpilot-web/src/`

## Sammanfattning

A2-implementationen är välkomponerad, följer JobbPilots etablerade FE-mönster
(server-only fetch-helper, server action med `ActionResult`-discriminerad
union, RHF + Zod-formulär) och är pixelmässigt konsistent med A1. Inga
Blockers, inga Major. Två Minor och en Nit. TypeScript-konventioner och
civic-utility-tonen är intakta. Test-coverage (9 schema-tester) är
proportionerlig till scope.

## Fynd

### Minor 1 — Dubbel Zod-validering är defensiv overhead

`me-profile-form.tsx:82-92` kör `updateMyProfileSchema.safeParse(values)` på
klientsidan **innan** `updateMyProfileAction` (som också `safeParse`:ar på
servern, `me.ts:28`). RHF + `register("language")` + `<select>` med två
fasta options garanterar att `values` redan matchar schemat. Klient-parse
ger ingen ytterligare säkerhet — servern är auktoritet och valideras
ändå.

**Föreslagen åtgärd (in-block):** ta bort klient-`safeParse`:n och
skicka `values` direkt till action. `as UpdateMyProfileInput`-casten på
rad 96 försvinner då också (svar på fråga 2: casten är teknisk korrekt
idag men hade varit onödig utan dubbel-parse).

Alternativt: behåll om Klas vill ha defense-in-depth, men dokumentera
varför i kommentar.

### Minor 2 — `normalizeLanguage` sväljer tyst data-inkonsistens

`me-profile-form.tsx:45-47` mappar allt utom `"en"` till `"sv"`. Om
backend någon gång returnerar `"fi"` (felaktig data, migrationsfel,
manipulerad DB-rad) visas svenska utan att vare sig användare eller
loggning märker något. För en civic-utility med GDPR-fokus är *silent
coercion* svagare än *fail-loud*.

**Föreslagen åtgärd:** lämna kvar normaliseringen som default men logga
warning via en framtida client-logger när `language` inte matchar
`"sv"|"en"`. Idag finns ingen sådan logger — acceptabelt att vänta.
Svar på fråga 4: nuvarande val OK, men inte typed error (skulle
brytapage-renderingen för en kosmetisk avvikelse).

### Nit — Inline-Tailwind på `<select>` duplicerar Input-styling

`me-profile-form.tsx:130` har en lång inline-className som speglar
`<Input>`-komponentens stilar. Risk att de divergerar över tid.

**Föreslagen åtgärd:** inte nu. När en andra `<select>` dyker upp i
kodbasen — extrahera till `components/ui/select-native.tsx`. YAGNI
fram tills dess. (Samma rule-of-three-resonemang som du applicerar
på a11y-helpern — konsekvent.)

## Svar på dina specifika frågor

1. **§4.1 + §4.4** — OK. `MeProfileForm` PascalCase, `me-profile-form.tsx`
   kebab-case filnamn, svensk route `/mig`, engelska kod-symboler. Matchar
   §4.2 + §4.4.
2. **§5.2 anti-patterns** — inga brott. `as UpdateMyProfileInput` är tekniskt
   onödig om Minor 1 åtgärdas; annars motiverad och inte i `as any`-kategorin.
3. **Scope-disciplin / a11y-helper** — håller med. Två konsumenter (resume +
   me) räcker inte för extraktion. Rule of three gäller. Lokal kopia är
   billigare än prematur abstraktion.
4. **Type-safety / normalizeLanguage** — se Minor 2. Nuvarande val OK.
5. **Edge cases:**
   - (a) `null`-rendering utan submit-knapp — OK, `role="alert"` är satt,
     copy är civic-utility-ton.
   - (b) `revalidatePath("/mig")` — *inte* redundant. `cache: "no-store"`
     gäller bara den specifika fetchen i `getMyProfile`; `revalidatePath`
     invaliderar Next-Router-cache så att nästa navigation till `/mig`
     re-renderar Server Component. Behåll.
   - (c) Alltid-4-fält — bekräftat. Backend partial update toleras, ingen
     null-risk.
6. **API-pattern** — `lib/api/me.ts` följer befintlig konvention. `lib/api/`
   innehåller redan `applications.ts` + `resumes.ts`. Rätt mapp, rätt
   `"server-only"`-direktiv, rätt `React.cache`-wrap.

## Approve-status

**Approve** — får mergas/pushas direkt. Minor 1 rekommenderas som
in-block-fix (5 raders borttag, förenklar och tar bort enda `as`-casten).
Minor 2 + Nit kan vänta.
