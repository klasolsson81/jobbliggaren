# `fas4-cv-kunskapsbank-sverige.md`

> **Versionsetikett:** Kunskapsbank v1.0 · Rubric v1.0 · Datum 2026-05-17
> **Scope:** Fas 4 – CV-modul i JobbPilot. Auktoritativ referens för AI-system och utvecklare.
> **Språk:** Svenska (tekniska termer på engelska där det är standard).
> **Källkonvention:** Inline-källor med URL och datum. Material från 2024 eller äldre, eller utan tydligt datum, markeras `[verifiera 2026]`.

---

## 1. Sammanfattning och användning

Denna fil är en **kunskapsbank** för det AI-system som genererar och bedömer CV i JobbPilots Fas 4. Den är **inte** en blogpost. Den konsumeras av (a) systemprompts för LLM-generering, (b) systemprompts för LLM-bedömning/rubric-evaluering, (c) utvecklare som implementerar UX-flödet, och (d) granskare som validerar att outputen följer svensk arbetsmarknadsnorm 2026.

**Kärnleveransen är CV-kvalitets-rubriken i avsnitt 2.** Den är *samma rubrik* för generering och bedömning — annars dömer systemet sitt eget output som dåligt. Rubriken är versionerad (v1.0, 2026-05-17), binär per kriterium (PASS/FAIL med citerad evidens), och har separata profiler för **ATS-optimerad** respektive **visuell** rendering där innehållskriterierna delas.

**Hur downstream-AI bör konsumera filen:**

1. **Generator-LLM** läser §2 + §3 + §6 som hård specifikation. Systemprompt instrueras att försöka uppnå PASS på varje kriterium med kritisk/hög vikt.
2. **Judge-LLM** (en **annan modell** än generator, se §10) använder samma §2-rubrik men med adversarial system-prompt. Output är strukturerad JSON: `{criterion_id, verdict, evidence_text, confidence}`.
3. **UX-systemet** följer §5 för flöden, defaults och drop-off-mitigering.
4. **Branschväljaren** triggar regelvarianter enligt §7 (offentlig sektor, akademi, tech, vård, skola).
5. **Anti-mönster-filter** i §6 fångar klyschor under generering och flaggar dem under bedömning.
6. **Antaganden** i §8 ska re-verifieras av Fas 4-teamet vid byggstart.

Filen följer **dataminimeringsprincipen** från IMY (Integritetsskyddsmyndigheten): rubriken förbjuder personnummer på CV (kritiskt fail) och behandlar foto som opt-in, inte standard.

---

## 2. CV-kvalitets-rubric (kärnan) — v1.0 · 2026-05-17

### 2.1 Designprinciper

Rubriken är byggd för att vara **maskinläsbar, binär per kriterium, och kalibrerbar mot mänskliga rekryterare**. Forskningen om LLM-as-a-judge (Pombal et al., arXiv:2604.06996, 2026; Wataoka et al., arXiv:2410.21819, 2024) visar att **skalor 1–10 är extremt biaskänsliga** men **binära PASS/FAIL med citerad evidens** är mer robusta. Därför är varje kriterium binärt, med eviden­skrav.

Varje kriterium har:

- **Kategori** (A Innehåll / B Struktur / C Språk / D ATS-parsbarhet / E Visuell kvalitet).
- **Vikt** (Kritisk / Hög / Medel / Låg). Vikterna är kalibrerade mot vad rekryterare/forskning säger faktiskt påverkar beslut (Ladders eye-tracking 2018; Jobscan State of the ATS 2025; ResumeWorded 4-faktor; svenska rekryteringsföretags publicerade screeningkriterier).
- **Pass-signal** (mätbar, citerbar i CV-texten).
- **Fail-signal** (mätbar, citerbar).
- **Profilgiltighet** (ATS / Visuell / Båda).

### 2.2 Rubric-tabell — Innehåll och substans (A)

Delas av båda profilerna.

| # | Kriterium | Vikt | ATS-pass-signal | ATS-fail-signal | Visuell-pass-signal | Visuell-fail-signal |
|---|---|---|---|---|---|---|
| **A1** | Mätbara resultat | Kritisk | ≥1 kvantifierad uppgift per roll de senaste 10 åren (siffra, %, valuta, antal, tid) | 0 siffror i hela arbetslivserfarenheten ELLER >50 % av punkterna saknar mätbarhet | Samma som ATS | Samma som ATS |
| **A2** | Action verbs i bullet-inledning | Hög | ≥80 % av punkterna börjar med starkt svenskt handlingsverb i preteritum (se §6.3) | <50 % har action verb i början, eller börjar med "Ansvarig för", "Arbetsuppgifter:", "Var med och" | Samma | Samma |
| **A3** | Relevans mot målroll | Kritisk | ≥60 % keyword-overlap mellan annons och CV (skill-, profil- och erfarenhetssektion); senaste 1–2 roller tematiskt relaterade | <30 % overlap; generiskt CV utan anpassning | Samma | Samma |
| **A4** | Tidsluckor (gaps) hanterade | Medel | Inga oförklarade gaps >6 mån, eller gaps förklaras explicit (föräldraledighet, studier, sjukskrivning) | ≥1 oförklarat gap >6 mån OCH inkonsekvent datering | Samma | Samma |
| **A5** | Karriärprogression synlig | Hög | Progression i ansvar/titlar över tid, ELLER medveten lateral rörelse motiverad i profil | Stagnation utan kontext; titel-degradering utan förklaring | Samma | Samma |
| **A6** | Konkretion vs vaghet | Hög | ≥70 % av punkterna innehåller konkret artefakt (verktyg, system, kund, projekt, leverans) | >50 % generiska punkter ("ansvarade för dagliga arbetsuppgifter") | Samma | Samma |
| **A7** | Anti-klyschor | Medel | <2 förekomster av tomma fraser (se §6.1-lista) utan styrkande exempel | ≥3 klyschor utan stöd; profiltext är >50 % personlighetsadjektiv | Samma | Samma |
| **A8** | Profil-/sammanfattningstext | Medel | 2–4 meningar, ~40–60 ord, innehåller roll/år/specialisering + 1 resultat. Ingen "Objective"-USA-stil | Saknas helt ELLER >100 ord ELLER ren adjektivlista ELLER "Objective: To obtain..." | Samma | Samma |
| **A9** | Soft skills underbyggda | Låg | Mjuka egenskaper nämns endast med konkret exempel ("ledde team om 8") | Adjektivlista utan exempel ("kommunikativ, driven, social") | Samma | Samma |
| **A10** | Utbildning korrekt | Hög | Examen, lärosäte, år, ev. inriktning. ECTS för internationellt. Relevanta certifikat listade | Saknad utbildning utan förklaring; ovanligt detaljerade gymnasiebetyg för senior | Samma | Samma |

### 2.3 Rubric-tabell — Struktur (B)

Delas av båda profilerna men implementation skiljer sig.

| # | Kriterium | Vikt | ATS-pass-signal | ATS-fail-signal | Visuell-pass-signal | Visuell-fail-signal |
|---|---|---|---|---|---|---|
| **B1** | Sektioner och ordning | Hög | Kontakt → (Profil) → Arbetslivserfarenhet → Utbildning → Kompetenser → Språk → Övrigt. Omvänt kronologiskt | Saknar erfarenhet/utbildning; kreativ ordning som döljer kärninfo | Samma sektionsordning men visuella avdelare OK | Samma |
| **B2** | Längd | Hög | Junior 0–3 år: 1 sida. Mid 3–10 år: 1–2 sidor. Senior 10+ år: max 2. Akademiskt: längre OK med publikationsbilaga | <0,5 sida ELLER ≥3 sidor (icke-akademiskt). ATS: <1 sida tappar keyword-bredd | Samma längd, men whitespace räknas inte som "tomt" | Samma men 2,5 sidor accepteras om designen är skanbar |
| **B3** | Kontaktuppgifter kompletta | Kritisk | Namn, telefon (svenskt format), professionell e-post, ort, LinkedIn-URL — **i klartext i huvuddokumentet**, inte i header/footer | Saknar e-post/telefon; kontakt enbart i sidhuvud; e-post som bild | Får placeras i designad header med ikoner, men data MÅSTE finnas som extraherbar text bredvid | Kontakt enbart som ikon utan textlabel |
| **B4** | Personnummer ej angivet | Kritisk | Inget personnummer (varken helt eller fyra sista). IMY-rek + Randstad-rek (https://www.randstad.se/karriartips/hitta-jobb/10-enkla-steg-till-ett-bra-cv) | Personnummer angivet — auto-fail | Samma | Samma |
| **B5** | Konsekvent formatering | Hög | Samma typsnitt (max 2), samma punktstil, samma rubriknivåer | Blandning av punkter, fonter, storlekar | Samma | Samma |
| **B6** | Datumformat konsekvent | Hög | Samma format genomgående. Säkrast för ATS: `MM/YYYY – MM/YYYY` eller `MM/YYYY – Nuvarande` (resumeadapter.com 2026) | Blandning "2022 – idag" + "jan-22 – mar 2024"; "'21"; säsong | Konsekvens räcker; "jan 2022 – mar 2024" OK | Inkonsekvens |
| **B7** | Kronologi tydlig | Hög | Omvänt kronologiskt; senaste först. Parallella roller markerade | Blandad kronologi; överlappande datum utan markering | Samma | Samma |
| **B8** | Filnamn | Låg | `CV_Förnamn_Efternamn.pdf` (Arbetsförmedlingen-rek.) | `cv.pdf`, `document(1).pdf` | Samma | Samma |

### 2.4 Rubric-tabell — Språk och ton (C)

Delas av båda profilerna.

| # | Kriterium | Vikt | ATS-pass-signal | ATS-fail-signal | Visuell-pass-signal | Visuell-fail-signal |
|---|---|---|---|---|---|---|
| **C1** | Stavning och grammatik | Kritisk | 0 stavfel; 0 grammatikfel i bullets. Maskinell kontroll mot svensk ordlista | ≥1 stavfel; ≥2 grammatikfel | Samma | Samma |
| **C2** | Ton (svensk norm) | Hög | Saklig, neutral, faktabaserad. Lagom utan amerikansk "sales pitch" | "Rockstar", "ninja", "stellar track record"; ELLER överdriven blygsamhet ("har lite erfarenhet av") | Samma | Samma |
| **C3** | Aktivt språk | Hög | ≥80 % aktiva verb, få passiveringar | >30 % passiv form ("ansvarades för", "utfördes av") | Samma | Samma |
| **C4** | Konsekvent perspektiv | Medel | Ingen pronomen (svensk standard, "Ledde team om 5") ELLER konsekvent 1:a person. Aldrig 3:e person | Blandning eller 3:e person ("Anna är en driven...") | Samma | Samma |
| **C5** | Språkkonsistens (sv/en) | Hög | Hela CV på ett språk. Engelska tekniska termer (Python, SaaS, B2B) OK i sv-CV | Blandning sv/en i bullets ("Worked med kunder för att deliver...") | Samma | Samma |
| **C6** | Förkortningar förklarade | Låg | Branschspecifika förkortningar skrivs ut första gången | Förkortningar utan kontext | Samma | Samma |

### 2.5 Rubric-tabell — ATS-parsbarhet (D) — endast ATS-profil

| # | Kriterium | Vikt | ATS-pass-signal | ATS-fail-signal |
|---|---|---|---|---|
| **D1** | Filformat | Kritisk | Textbaserad PDF (export från Word/Google Docs) eller `.docx`. Text ska kunna kopieras | Inscannad bild-PDF; `.pages`; `.jpg`; lösenordsskyddad |
| **D2** | Enspaltig layout | Kritisk | 1 kolumn, läses uppifrån och ned. Testar PASS i Workday, Greenhouse, Lever, Teamtailor (resumeadapter.com 2026) | 2- eller 3-spaltig, särskilt med textboxar; Workday/Taleo kraschar även med native columns |
| **D3** | Standardtypsnitt | Hög | Arial, Calibri, Helvetica, Roboto, Times New Roman, Georgia, Verdana. 10–12 pt brödtext | Custom-fonter; <9 pt; ikonfonter |
| **D4** | Inga ikoner/bilder för data | Kritisk | Telefon, e-post, ort som klartext. Ikoner får finnas men data måste finnas i textform | Telefonikon utan textnummer; e-post bara som ikonlänk |
| **D5** | Inga tabeller/textrutor | Hög | Linjär flow, inga celler eller textrutor | Tabellbaserad layout (cell-skifte i parser) |
| **D6** | Standardrubriker (svenska) | Hög | "Arbetslivserfarenhet"/"Erfarenhet", "Utbildning", "Kompetenser", "Språk", "Kontakt" — alt. engelsk standard om CV på engelska | "Min resa", "Saker jag är bra på", "Where I've been" |
| **D7** | Header/footer ej för kontaktinfo | Hög | Kontakt i main body | Namn/kontakt i sidhuvud — ignoreras av många ATS (jobscan.co 2026) |
| **D8** | Keyword-matchning mot annons | Kritisk | Match-rate ≥75 % (Jobscan-tröskel) eller ≥80 % (Teal). Hard skills syns i ≥2 av {profil, skills, erfarenhet} | <50 % match; ELLER keyword-stuffing (>3 onaturliga repetitioner) |
| **D9** | Filstorlek | Låg | <2 MB | >5 MB (komprimera bilder) |
| **D10** | Klickbara länkar | Låg | LinkedIn och portfolio som korta, klickbara URL:er | Tracking-URL:er; URL som bild |

### 2.6 Rubric-tabell — Visuell kvalitet (E) — endast visuell profil

| # | Kriterium | Vikt | Visuell-pass-signal | Visuell-fail-signal |
|---|---|---|---|---|
| **E1** | Hierarki | Kritisk | Tydlig nivåskillnad: namn > sektionsrubrik > rolltitel > företag/datum > bullets. Min 30 % storleksdiff mellan nivåer | Allt samma storlek; namn för litet; rolltitel försvinner |
| **E2** | Whitespace | Hög | Marginaler 1,5–2,5 cm; radavstånd 1,15–1,3; ≥8 pt mellan sektioner. F-mönster-skanbar | Marginaler <1 cm; tätpackat; ingen luft |
| **E3** | Typografisk konsekvens | Hög | Max 2 typsnitt (1 rubrik, 1 brödtext); max 3 textstilar (vanlig/fet/kursiv) | ≥3 typsnitt; blandning versaler/kapitäler |
| **E4** | Färgpalett | Medel | Max 2–3 färger inkl. svart/vit. Accentfärg i rubriker. WCAG AA-kontrast ≥4,5:1 | ≥4 färger; ljus färg på vit bakgrund |
| **E5** | Foto (om foto väljs) | Medel | **SE-default: utelämna foto.** Om opt-in: professionellt porträtt, neutral bakgrund, skarpt, ansikte ~60 % av bilden | Selfie; semesterbild; foto skickat till TNG eller annan anonymiserad process (kasseras) |
| **E6** | Skanbar struktur (F-mönster) | Kritisk | Titel/företag/period fångas i en F-svep. Datum tydligt höger. Bullets max 2 rader | Datum inbäddat i löptext; bullets 4+ rader; ingen ankarpunkt |
| **E7** | Bullet-design | Hög | Konsekventa symboler (• eller –); enhetlig indrag; vänsterjusterad | Center; höger-justerade bullets; emoji som bullets |
| **E8** | Sidbalans | Låg | Inget halvtomt sista-blad; ingen bullet bruten över sida | Stort tomrum sist; sektion delad olämpligt |

### 2.7 Score-sammansättning

**Binärt per kriterium (PASS=1, FAIL=0)** viktas inom kategori: Kritisk × 3, Hög × 2, Medel × 1, Låg × 0,5. Kategoriscore = (summa erhållna vikter) / (summa möjliga vikter), uttryckt i procent.

**Total ATS-score** = A·0,50 + B·0,20 + C·0,15 + D·0,15
**Total Visuell-score** = A·0,50 + B·0,15 + C·0,15 + E·0,20

**Tröskelvärden:**

- **<50 %**: Ej redo. Kandidat-CV som auto-rejectas i ATS hamnar ofta här.
- **50–69 %**: Behöver omarbetning.
- **70–84 %**: Konkurrenskraftigt.
- **85 %+**: Toppskikt.

**Kritiskt:** Visa **kategori-score som primär UX**, totalscore sekundärt. Lyft alltid "kritiska fail" (B4 personnummer, C1 stavfel, D1 fel filformat, A1 inga mätbara resultat) separat — de måste fixas oavsett totalpoäng. Detta motverkar Goodhart-effekten där användare optimerar mot siffran.

### 2.8 Rubric-versionering

- **Semantisk versionering** `rubric@major.minor.patch`. Major = kriterium tas in/ut. Minor = tröskel ändras. Patch = formulering.
- **Lagra `rubric_version` med varje bedömning** i databasen tillsammans med modell-version och prompt-version.
- **Backward compatibility 90 dagar** — N-1 versions körbara parallellt.
- **Changelog publikt** för användare som undrar varför scoren ändrats.
- **Kalibrerings-set** av 50–100 svenska CV manuellt bedömda av rekryterare följer versionen; mät Cohen's κ AI vs human kvartalsvis. Om κ < 0,7 → rulla tillbaka eller rekalibrera prompt.

---

## 3. Svenska normer vs internationella råd

Detta avsnitt listar **explicita konflikter** mellan generiska internationella/amerikanska CV-råd och svensk norm 2026. Generator-LLM och judge-LLM ska **prioritera SE-normen** när konflikt uppstår.

### 3.1 Konkret konfliktlista

| Internationellt råd | Svensk norm 2026 | Flagga för AI |
|---|---|---|
| "Always include a professional photo" | **FEL i SE.** Foto är opt-in, trenden går mot att utelämna p.g.a. diskrimineringsrisk. Academic Work (https://www.academicwork.se/artiklar/soka-jobb/cv-bild-tips--rekommendationer): "Våra experter tycker inte att du behöver ha bild på dig själv och baserar aldrig vårt urval på en bild". TNG har slopat fotoupload helt (https://www.tng.se/blogg/nytt-fran-tng/, 2025). | `foto_default = false`. Visa varning vid opt-in. |
| "Start with an Objective statement" | **Föråldrat i SE.** Ersätts med profiltext som matchar tjänsten. | Fail-signal i A8. |
| "References available upon request" | **Onödig rad** i SE. Skip eller endast "Lämnas på begäran" (Randstad). | Generera ej raden. |
| "Include GPA" | **FEL i SE.** Olika utbildningssystem. Ange examensnivå + ECTS (Unionen, https://www.unionen.se/medlemskapet/karriar-och-utveckling/byta-jobb/cv-pa-engelska). | Strip GPA om sv-CV. |
| "List every job since high school" | **Avråds.** Tumregel ~10 år tillbaka (CVkungen). | Klipp äldre roller för senior. |
| "Use creative/colorful design" | **Försiktigt.** Ren, professionell layout vinner; kreativ design bara för kreativa roller. | E4 max 2–3 färger. |
| "Self-promotional summary" | **Tona ned.** Lagom-norm. Säg vad du gjort, inte hur fantastisk du är. | C2 Fail-signal vid "rockstar"/"stellar". |
| "Include date of birth always" | **Avråds i SE.** Åldersdiskriminering börjar vid 40 (IFAU refererad av TNG). TNG har slutat registrera ålder. | Auto-strip födelsedatum/ålder. |
| "Marital status, number of children" | **Aldrig i SE.** Diskrimineringslagen 2008:567. | Auto-fail. |
| "Hand-written signature" | Aldrig. | – |
| "Include personal number / ID" | **ALDRIG i SE.** GDPR + IMY-rek. Personnummer behövs först efter anställningsbeslut. | Kritiskt fail B4. |
| Quantify aggressively + awards | **Acceptabelt 2026** men med svensk återhållsamhet. Skriv siffrorna sakligt, inte "Top 1 % sales performer 5 years running". | Tonjustering. |

### 3.2 Foto, personnummer, ålder, civilstånd, körkort — sammanfattat

| Uppgift | Norm SE 2026 | Källa |
|---|---|---|
| **Fullständigt personnummer** | Avråds — integritetsrisk, irrelevant för urval | CV.se; Randstad 2026; IMY |
| **De fyra sista** | Avråds | Randstad |
| **Födelsedatum/ålder** | Allt vanligare att utelämna | TNG; IFAU |
| **Civilstånd/familj** | Ska INTE finnas | CV.se; cvmall.se |
| **Kön** | Anges inte uttryckligen | – |
| **Körkort** | Tas med endast när relevant (säljare, hantverkare) — ange klass | cvmall.se |
| **Adress** | Endast ort räcker; full postadress avråds | CV.se; LiveCareer 2026 |
| **Foto** | Valfritt; trenden mot att utelämna | Academic Work; TNG; KI 2026-05-06 |
| **Nationalitet** | Endast vid säkerhetsklassad tjänst | – |
| **Religion/politik** | Aldrig | Diskrimineringslagen 2008:567 |

**IMY-vägledning sammanfattat** (https://www.imy.se/verksamhet/dataskydd/dataskydd-pa-olika-omraden/arbetsliv/rekryteringssystem-och-kompetensdatabaser/): "En arbetsgivare får i samband med rekrytering bara behandla sådana uppgifter som är nödvändiga för ändamålet." Samtycke som rättslig grund är problematiskt i arbetslivskontext. **Implikation:** systemet ska aldrig uppmana användaren att lägga in personnummer/känsliga uppgifter på CV.

**DO-data** (https://www.do.se/download/18.36cbb9ac1886717f72d416/1686638418878/rapport-rekrytera-utan-att-diskriminera.pdf, 2023 [verifiera 2026]): personer med arabiska/muslimska eller afrikanska namn måste skicka **dubbelt så många jobbansökningar** för positiv respons. 28 % av arbetslivsanmälningarna handlar om rekrytering. Anonymiserad rekrytering motiveras direkt av denna evidens.

### 3.3 Format, kronologi, längd

- **Omvänd kronologi är default i SE 2026** (CV.se; Academic Work; Barona, https://barona.se/for-jobbsokande/cv/kronologiskt-cv/). Kompetensbaserat/funktionellt CV används vid karriärbyte eller längre luckor (Unionen, https://www.unionen.se/filer/ovrigt/cv-mallar).
- **Längd**: Junior 1 sida, mid 1–2, senior max 2 (icke-akademiskt). Akademiskt CV längre OK med publikationsbilaga (KI Karriärservice, https://utbildning.ki.se/hur-skriver-man-ett-cv, uppdaterad 2026-05-06).

### 3.4 Mätbara resultat — har det normaliserats?

**Ja, 2026.** Quantified achievements är inte längre "för amerikanskt". Unionen: "fokusera på resultat och siffror" (https://www.unionen.se/story/opinion-stories/boosta-din-kunskap-2025, 2025). cvmall.se exempel: "'Arbetat med React' säger inget. 'Byggde om incheckningsflödet i React Native och minskade drop-off med 18 %' säger allt" (https://cvmall.se/cv-exempel/programmerare, 2026). **STAR-metoden med svensk saklig ton** är rekommenderad. Vad som *fortfarande inte funkar* i SE är amerikansk hybris ("Top 1 %", "rockstar").

### 3.5 Ton — formellt vs informellt

Formellt men inte stelt. Aktivt språk, handlingsverb. **Första person implicit** — bullets utan "jag", profiltext kan använda "jag". Tredje person ("Anna är...") undviks. KI 2026: "Skriv 'jag tog tillfället i akt' istället för 'jag erbjöds'".

### 3.6 Svenska vs engelska CV

**Tumregel:** Engelska om annonsen är på engelska; annars svenska (Unionen). Tech/startup: engelska har normaliserats även för rent svenska bolag med engelska som koncernspråk. Akademi: engelska ofta krav för forskartjänster. Offentlig sektor/vård: svenska är normen och ofta krav. Datumformat: ÅÅÅÅ-MM-DD i SE-CV; engelska CV anpassas till mottagarens land.

### 3.7 Personligt brev 2026 — fortfarande aktuellt?

**Är på väg ut i många processer** men inte dött. Statliga rekryteringar har i hög grad ersatt brev med urvalsfrågor i formulär (May Molin, Poolia, citerad i Ingenjören 2025-09-02, https://ingenjoren.se/2025/09/02/personliga-brevet-ar-pa-vag-bort-sa-har-soker-du-nu/). TNG har slopat det helt (https://www.tng.se/blogg/nytt-fran-tng/). I privata näringslivet förväntas det fortfarande ofta. Saco (https://www.saco.se/yrkesliv/jobb/jag-ska-soka-jobb/cv-och-personligt-brev/): "målet med brevet är inte att få jobbet utan att bli kallad på intervju".

**Längd**: max en A4 / 250–400 ord (Akavia; OwlApply; Arbetsförmedlingen). **Skillnad CV vs brev**: CV = historik/fakta. Brev = framtid/motivation/match. Brevet ska **komplettera, inte upprepa** CV (Academic Work).

**Implikation för JobbPilot:** Personligt brev är inte del av kärn-CV-rubriken men bör erbjudas som **separat artefakt** med egen mini-rubric. Storfeature för Fas 4+.

---

## 4. ATS-optimerat vs visuellt — där det krockar

### 4.1 Det svenska ATS-landskapet 2026

Sverige ligger något efter USA i AI-screening-automation men adoptionen ökar snabbt. Enligt **Experis/ManpowerGroup** använder 28 % av svenska företag AI i rekryteringen, 37 % planerar inom tre år (Tidningen Näringslivet 2024 [verifiera 2026]).

**Dominanta system:**

- **Teamtailor** (svenskt, Stockholm) — marknadsledare för SME och scale-ups. 216 anställda, omsättning 495 MSEK 2024 (bolagsfakta.se 2026). Kunder: Daniel Wellington, SATS, Academic Work m.fl. AI-funktioner (Co-pilot) drivs av OpenAI Enterprise; 40 % av kunderna har AI aktiverat (kollega.se, aug 2025, https://www.kollega.se/ai-rekrytering).
- **Varbi** (Trollhättan, del av Grade) — dominerar offentlig sektor: Arbetsförmedlingen, Lunds universitet, Stockholms universitet (sedan jan 2025), KTH, Uppsala, länsstyrelser, kommuner. Stödjer PDF, TXT, RTF, DOC, DOCX (max 10 MB).
- **ReachMee** (numera "Rekrytering by Talentech") — nordiskt, stora organisationer och myndigheter.
- **Jobylon** — Stockholm, enterprise/multi-brand. Kunder: Scandic m.fl.
- **Workday** — globala koncerner med svensk närvaro (Spotify, Ericsson, banker, telekom). Förvärvade Sana (svenskt AI-bolag) 2025.
- **Greenhouse, Lever** — svensk tech med US-investerare.
- **SAP SuccessFactors** — stora industri/koncernbolag.
- **iCIMS, Taleo** — multinationella, dåligt rykte för parsning.

### 4.2 Hur parsning faktiskt fungerar 2026

Branschstandarder för parsing är **RChilli, Sovren, Daxtra**. Teamtailor använder **OpenAI GPT-baserad parser** för avancerad extraktion ovanpå klassisk regex/segmentering. Detta innebär att 2026 är ATS i Sverige **hybridsystem**: regex för strukturerad data (mail, telefon, datum), embeddings + LLM för matchning, sammanfattning och fritext-bedömning.

**Praktisk konsekvens:** keyword-matchning är fortfarande relevant (BM25 slår dense retrieval på 9/18 BEIR-benchmarks, atlan.com 2026), men måste kombineras med **semantiskt rika beskrivningar** för embedding-matchning. Vit text på vit bakgrund är **förbjudet** och flaggas av moderna ATS.

### 4.3 Konfliktmatris — ATS vs Visuellt

| Designval | Ser bra ut visuellt | ATS-konsekvens 2026 | Konflikt? |
|---|---|---|---|
| Tvåkolumns-layout | Ja | Native columns: OK i Teamtailor/Greenhouse/Lever. **Trasigt i Workday/Taleo** även med native. Textbox-baserade kolumner: trasigt överallt | **Konflikt — kompromissa till enspaltigt för ATS-versionen** |
| Ikoner istället för "Telefon:" | Ja | Font Awesome-glyfer parsas som NULL/garbage; regex hittar inte numret utan label | **Konflikt — kräv klartext-label i ATS-versionen** |
| Foto i header | Ja (i vissa kontexter) | Text bakom bilden försvinner i textlager; SE-norm avråder dessutom | **Dubbel konflikt — utelämna foto i ATS-versionen, opt-in i visuell** |
| Färgade boxar/textrutor | Ja | Word-textrutor parsas inte i många system | **Stark konflikt — använd endast inline formatering i ATS** |
| Skill-staplar ("Python 90 %") | Ja | SVG/bilder utan textdata; "Python" parsas, "90 %" tappas separat | **Konflikt — ersätt med text: "Python (avancerad, 6 år)"** |
| Sidofält med kontakt | Ja | Sidofält = ofta textruta = inte parsad | **Konflikt — kontakt i huvudtexten, första raden** |
| Kreativa typsnitt | Ja | Fallback-fonter → renderingsfel eller bildflattening → text→bild | **Konflikt — Arial/Calibri/Helvetica för ATS** |
| Tabeller för utbildning | Ja | Cell-skifte: examensår parsas i fel rad | **Konflikt — radbaserad layout: "MSc Datavetenskap, KTH, 2018–2022"** |
| Kreativa rubriker ("Min resa") | Ja | Parsern segmenterar baserat på standardrubriker; kreativa rubriker viktas lägre | **Konflikt — standardrubriker i ATS, kreativitet via typografi** |
| Sidhuvud/sidfot för data | Ja | Många parsers ignorerar | **Stark konflikt — aldrig värdefull data i headers/footers** |

### 4.4 Teamtailor-specifika fakta

Eftersom Teamtailor är svenskt och marknadsledande är dess parsing-beteende särskilt viktigt:

- Auto-extraherar i basläget: **namn, e-post, telefon, LinkedIn-URL**. Övriga fält kräver Co-pilot.
- Co-pilot (OpenAI GPT-baserad) gör **resume summaries** (5 punkter, **exkluderar identifierande data** namn/e-post/kön för bias-reducering — bekräftelse av anonymiseringstrenden), **candidate screening** (rangordning mot kriterier), **candidate suggestions** (matchning mot databas), **Ask Co-pilot** (chat).
- **EU-datalokalisering** möjlig (Irland eller US West).
- **Hallucinationer dokumenterade** — produktchef David Wennergren bekräftar att Co-pilot kan hitta på info (kollega.se, aug 2025). Teamtailor har "valt bort modeller som hallucinerar mer".

**Implikation för JobbPilot:** Användare som söker via Teamtailor-driven karriärsida träffar 40 % AI-rangordning. ATS-versionen av CV ska därför vara optimerad för **både regex-parsing och LLM-summarization**. Klartext + semantiskt rika beskrivningar + standardrubriker.

### 4.5 Varbi/offentlig sektor — annorlunda spel

Varbi-flödet bygger på **strukturerade formulär + bilagor**, inte tung CV-parsning. Ansökan består av: ifyllt formulär (urvalsfrågor mot kravprofil) + CV + ev. personligt brev + bilagor. Vid akademiska tjänster: separat pedagogisk meritportfölj, publikationslista. **Matchningen mot skall-krav sker på formulärsvaren**, inte CV-parsing. Avidentifierat urval stöds som modul.

**Implikation:** För offentlig sektor i SE ska JobbPilot **inte överoptimera CV mot parser** — istället hjälpa användaren att svara strukturerat på Varbi-formulär (separat feature). CV:t ska vara läsbart av människa.

### 4.6 Praktisk "ATS-säker"-spec för JobbPilot-generator

ATS-versionen genereras med dessa hårda regler:

1. **Enspaltigt**.
2. **PDF, textbaserad** (export från eget renderingslag, aldrig print-to-PDF som ger bildlager).
3. **Arial, Calibri eller Helvetica**, 10–12 pt brödtext.
4. **Inga ikoner, bilder, foton, textrutor, tabeller, skill-staplar**.
5. **Kontaktinfo överst i main body**: "Telefon: +46 70 123 45 67", "E-post: namn@domain.se", "LinkedIn: linkedin.com/in/handle", "Ort: Stockholm".
6. **Standardrubriker svenska**: "Profil", "Arbetslivserfarenhet", "Utbildning", "Kompetenser", "Språk".
7. **Datumformat enhetligt**: `MM/YYYY – MM/YYYY` eller `MM/YYYY – Nuvarande`.
8. **Filnamn**: `CV_Förnamn_Efternamn.pdf`.
9. **Keywords verbatim** från annons i Kompetenser-sektionen + semantiskt rika beskrivningar i bullets.

Systemet ska generera **både ATS- och visuell version från samma JSON-källdata** så att innehållskriterierna är identiska och bara rendering skiljer.

---

## 5. De två flödena och snabb/fördjupad-split — tryckt mot UX-research

### 5.1 Sammanfattande omdöme om produktägarens vision

| Visionspunkt | Omdöme | Justering rekommenderas? |
|---|---|---|
| Tvådelad ingång (CV finns / inget CV) | **Stark.** Klassisk conditional disclosure. | Nej |
| Snabb-väg-default + fördjupad opt-in | **Stark.** Utnyttjar 80/20 + foot-in-the-door | Lägg till opt-in *efter* första genereringen |
| 3 val (annons/yrke/allmänt) | **Stark.** Inom Hick's law sweet spot | Specificera input-format (se 5.4) |
| "Behåll min design"-option | **Stark differentiator** | Hantera teknisk svårighet med fallback |
| Foto opt-in | **Justera** | Default ska vara **inget foto** för SE-marknad |
| ATS/visuellt/båda output-val | **OK men risk för förvirring** | Microcopy + möjlighet att byta efter generering |
| Spinner eller bakgrund | **Föråldrad dikotomi** | Använd streaming-UI som default |
| Inget 30-min-formulär | **Stark princip** | Konkret: max 5–7 steg, 2–3 frågor per steg i fördjupad |
| Konsekvent kvalitetsbegrepp (gen + bedöm samma rubric) | **Kritisk korrekt princip men risk för self-preference bias** | Använd olika modeller för gen vs bedömning |

### 5.2 Progressive disclosure i CV-byggaren

NN/g definierar progressive disclosure som att bara visa det som behövs nu (Nielsen 1995, fortsatt aktuell). Tre underkategorier är relevanta (LogRocket; UXPin 2026):

- **Staged disclosure** (linjär wizard) för "bygg från noll".
- **Conditional disclosure** för "har du CV?" och "vill du svara på fler frågor?".
- **Contextual disclosure** för redigeringsflöden ("Vill du lägga till språk?").

**Hård gräns:** max 5–7 steg i fördjupad väg, 2–3 frågor per steg på mobil (Anve 2026; Dashform 2026). NN/g varnar för **fler än två disclosure-nivåer** — användare tappar bort sig.

### 5.3 Drop-off — vad forskning visar

Baymard Institute (2024, 5 700+ checkout-sessioner — överförbart till formulär generellt):

- Optimum **6–8 fält** för låg friction.
- **81 % av mobilanvändare** överger formulär upplevda som "för långa" (10+ fält).
- Completion rate sjunker **3–7 % per fält över 8** på mobil.
- **Multi-step forms konverterar 14–86 % bättre** än single-page (Digital Applied 2026; Venture Harbour).
- **Inline-validering on-blur** ökar completion 16–22 %; on-keystroke *minskar* 8–12 %.
- **Progress bar** ökar väntningstålamod 3× — men LeadCapture.io visade att **borttagen progress bar från första steget ökade konvertering 133 %** (foot-in-the-door). Visa progress från steg 2.
- **Autosave + save-and-resume** är obligatoriskt för långa flöden (Resume.io, Teal har detta).

### 5.4 Mot annons / mot yrke / allmänt — UX-spec

**Designval rekommenderat:**

- **Tre stora kort** (inte dropdown — Hick's law: tre val OK men måste vara tydligt differentierade).
- **Default: "Allmänt"** är säker default om systemet inte vet annat. Om CV-data finns: föreslå "mot yrke" baserat på senaste titel.
- **Annons-input:** Hybrid URL-paste + textfält. URL → försök scrape (med ToS-respekt; Arbetsförmedlingens öppna API på `data.jobtechdev.se` är lagligt och bör vara förstavalet) → fallback till copy-paste.
- **Yrke-input:** Fritext + autocomplete från **Arbetsförmedlingens öppna taxonomi** (`jobtechdev.se`) som har ~15 000+ yrkesbenämningar mappade till SSYK nivå 4. Använd yrkesbenämningar i UI, lagra SSYK-kod i backend. **Använd aldrig SSYK-koder som direkt input** — användare känner inte till dem (SCB MIS 2012:1).

### 5.5 Smart förifyllning från PDF-upload — kritisk för trust

Visionens snabb-väg "AI fixar" har en **risk**: användaren ser inte vad som ändrades. Lösning:

1. **Bekräftelse-skärm efter parsing**: visa extraherad data uppdelad per sektion (kontakt, profil, erfarenhet, utbildning, skills) **innan** "fixa".
2. **Lågkonfidens-extraktioner markeras** med färgkod (grön = konfident, gul = osäker, röd = behöver granskning) — Altersquare 2024-mönster.
3. **Inline-edit** är överlägset separat skärm för datatäta sektioner (Teal, OpenResume gör detta — real-time preview).
4. **Originalfält bredvid extraherat** via toggle (split view blir trångt på mobil).
5. **Sektion-för-sektion-bekräftelse** vs "godkänn allt" minskar kognitiv belastning.

**Stark rekommendation:** Bekräftelse är obligatoriskt — det är där användaren bildar förtroende.

### 5.6 Screenshot-extraktion (OCR) av utbildning/erfarenhet

Visionen nämner screenshots — detta är en **stark differentiator för svensk marknad** där många har gymnasiebetyg som skannade PDF. Best practice:

- **Visa OCR-output i editerbart fält** direkt; användaren kan rätta innan AI strukturerar.
- **Konfidenspoäng per fält** från Tesseract/Google Vision/AWS Textract; tröskelvärde ~70 %.
- **Stöd flera bilder** (utbildningsintyg ofta flersidigt).
- **Fallback till manual input** om konfidens < tröskel — utan att skylla på användaren.

**Prior art att låna mönster från:** Apple Live Text (iOS), Google Lens, Microsoft Office Lens.

### 5.7 ATS / Visuellt / Båda — när i flödet?

**Hybrid-strategi:** Fråga *översiktligt* före generering ("Ska jag fokusera på att passera ATS, på snyggt utseende, eller båda?") med tooltip-förklaring, och låt användaren *byta vy* efter generering. Rezi tvingar ATS, Kickresume låter användaren välja (ClickUp 2026).

**Microcopy med konsekvens:** "Om du söker via stora bolags karriärsidor → ATS. Om du mailar direkt till kontakt → visuellt. Inte säker? Välj båda — vi genererar två versioner från samma innehåll."

### 5.8 Mall-val

**Antal:** 6–12 mallar med filter slår 30+ mallar utan filter (Paradox of Choice, Hick's law). Resume.io har 30+, Rezi har 7–10 (alla ATS-optimerade) — det senare är mer kuraterat och konverterar bättre för målgrupp som inte vill spendera tid.

**Visning:** Miniatyrer i grid (3–4 desktop, 2 mobil) + hover-preview. **Preview med användarens egna data** ifyllt är överlägset miniatyrer (Canva, Resume.io). Filter: bransch (tech, kreativ, akademisk, klassisk), stil, färg.

**Default-mall:** Den mest ATS-säkra enspaltiga.

### 5.9 "Behåll min design, förbättra innehåll" — stark differentiator

Tekniskt två angrepp:

1. **Parsa originaldokumentet med bevarat positionsdata** (pdf.js + bbox per textblock, à la OpenResume). Ersätt textnode, behåll style.
2. **Side-by-side editor**: visa original-PDF som referens, skriv ny version i editor.

**UX för förändringar — kritiskt:**

- **Inline diff (à la Google Docs "suggesting mode")** är guldstandard. Tiptap AI Toolkit, ProseMirror suggestion-mode, GitHub Copilot använder accept/reject per ändring + "accept all".
- **Färgkod**: grön = tillagt, röd = borttaget, gul = omskrivet.
- **Granularitet "smart mode"**: balans mellan block och inline-diff är mer läsbart än karaktärsnivå (Tiptap diff utility).
- **Sammanfattning ovanpå diff**: "AI gjorde 12 ändringar: 8 språkliga, 3 nyckelord, 1 ny formulering" → användaren väljer granularitetsnivå.

**Fallback obligatoriskt:** Om PDF-layout-bevarande misslyckas (vanligt vid komplexa layouter) → erbjud "vi kunde inte bevara layouten, vill du ändå se förslag på innehåll?".

### 5.10 Fotouppladdning + justering

**Tekniska komponenter:**

- **react-easy-crop** — populär, crop till cirkel/fyrkant, zoom, rotation.
- **Croppie / Cropper.js** som alternativ.
- **Bakgrundsborttagning**: Remove.bg API ($0.20/bild bulk) eller AWS Bedrock + SAM2 / u2net för billigare egen drift.

**UX-mönster (industristandard):** Upload → crop med overlay → optional bakgrundsborttagning → preview i CV-mall.

**Etisk linje — kritiskt för SE:**

- **Default = inget foto** för SE-marknad (TNG, Academic Work, Arbetsförmedlingen rek).
- **Foto är opt-in med varning**: "I Sverige är foto inte standard; vissa arbetsgivare använder anonym rekrytering och kan kassera CV med foto."
- **Bakgrundsbyte OK** (rensa stökig bakgrund). **Ansiktsretuschering inte OK** — etisk linje (skapar halo-effekt och diskriminerings­risk).
- **GDPR:** foto är personuppgift som inte är *nödvändig* för CV-funktionen → dataminimering.

### 5.11 Generering — streaming över spinner/bakgrund

Visionens binära "spinner eller bakgrund" är föråldrad. Best practice 2026 (Telerik 2026; Forum One 2025; NN/g):

| Väntetid | Mönster |
|---|---|
| <1 s | Ingen indikator |
| 1–10 s | Spinner eller skeleton |
| 10–60 s | Skeleton + kontextuell text ("Analyserar din erfarenhet…") **eller streaming** |
| >1 min | Async/queue + notification |

**Streaming-UI (à la ChatGPT)** är 2024–2026:s starkaste mönster för LLM-text:

- TTFT <1 s räcker → perceived latency försvinner.
- Användaren ser AI:n "tänka" → trust.
- Avbryt mid-generering → kostnadsbesparing.
- Streama text **in i skeleton-mall** sektion för sektion.

**När bryt ut till bakgrund?** Generering >30 s med timeout-risk; användaren ska kunna lämna fönstret; batch-operationer (5 versioner mot 5 jobb). **Notification:** in-app + email räcker för svensk konsumentprodukt. Push kräver app.

**Rekommendation:** Streaming-default + skeleton + contextual text + fallback email vid >60 s.

### 5.12 AI-feedback på uppladdat CV (score 67/100)

**Risk:** Låga scores skapar ångest utan handlingsplan (JobShinobi 2026; LandThisJob 2026: "ett CV som scorade 45/100 fick intervju inom 48 h").

**Designprinciper:**

- **Visa score med kontext**: "67/100 — bra start! Här är 3 saker som höjer den mest." Inte bara siffran.
- **Färgkod över värde**: röd/gul/grön → snabbare att förstå.
- **Per-sektion-feedback > samlad**: "Profil 8/10, Erfarenhet 5/10, Skills 7/10". Användaren agerar fokuserat.
- **Auto-fix + föreslå-och-godkänn**: Erbjud båda (ResumeWorded Magic Target, Jobscan Power Edit) men diff-vy obligatoriskt vid auto-fix.
- **Undvik keyword-stuffing-fällan**: rekryterare ser soft skills som keywords som red flag (LandThisJob 2026).

### 5.13 Drop-off-risker i visionen — rangordnade

Baserat på samlat evidensläge, de högsta drop-off-stegen:

1. **PDF-upload-bekräftelse** (om "AI fixar" sker utan synlig bekräftelse → tappad trust). *Mitigation:* tydlig review-skärm.
2. **Val mot annons/yrke/allmänt** (om ej differentierade → analysis paralysis). *Mitigation:* tre stora kort, outcome-framing.
3. **Generering-väntetid utan feedback** (spinner >10 s utan kontext). *Mitigation:* streaming + skeleton.
4. **Fördjupad väg om triggad för tidigt**. *Mitigation:* opt-in *efter* första output.
5. **Fotouppladdning om obligatorisk** (svenska användare ogillar). *Mitigation:* opt-in, tydligt "hoppa över".
6. **Mall-val** om för många utan filter. *Mitigation:* 6–12 mallar med filter.
7. **ATS/Visuellt-val om för tidigt utan förklaring**. *Mitigation:* microcopy + bytbar vy.
8. **Account-creation/email-vägg innan output** (Resume.io-mönster). *Mitigation:* visa output först, gate downloads.

### 5.14 Svensk marknad-specifikt

- **GDPR consent-fatigue stark i SE.** Minimera consent-popups, samla samtycke en gång, använd layered consent.
- **Lagom-mentalitet**: svenska användare skeptiska till AI-superlativ. Underlöfta-och-överleverera. Inte "ditt CV blir 10× bättre med AI" utan "vi lägger till 4 nyckelord som matchar annonsen".
- **Tilltal**: "du" (inte "ni"), informellt men professionellt. Undvik direktöversatt amerikansk sales-copy.
- **Foto inte default.**
- **Personnummer-guard**: om användaren matar in personnummer → varna ("detta behöver inte vara med, vi rekommenderar att stryka").
- **Datumformat**: stöd ÅÅÅÅ-MM (ISO) eller "jan 2023 – dec 2024".
- **Tvåspråkighet**: stöd både svenska och engelska från start; många svenskar söker internationellt.

---

## 6. Anti-klyschor (svenska) och bättre alternativ

### 6.1 Klyschor som triggar Fail-signal A7

Svenska rekryterare lyfter återkommande dessa som **röda flaggor**. OwlApply 2025: "Klichéer som 'Jag är en driven och resultatorienterad lagspelare' är omedelbart röda flaggor för svenska rekryterare". Shortcut: "Tala ur skägget. Det bästa är att helt enkelt sluta använda klyschor."

| Klyscha | Varför den inte fungerar | Bättre alternativ |
|---|---|---|
| **"Brinner för"** | Tom passion-signal som alla använder | Beskriv ett konkret projekt eller initiativ. "På fritiden underhåller jag open-source-biblioteket X med 4 000 nedladdningar/månad" |
| **"Driven lagspelare"** | Inget mätbart | "Ledde tvärfunktionellt team på 6 personer som levererade Y före deadline" |
| **"Prestigelös"** | Ironisk självmotsägelse | "Tog rollen som stödfunktion åt junior kollega under introduktionen och dokumenterade processen" |
| **"Ansvarstagande"** | Förväntat hos alla anställda | "Ansvarade självständigt för budget om 4,2 MSEK och rapporterade till styrelse kvartalsvis" |
| **"Social"** | Personlig egenskap, inte jobbrelevant | "Höll 12 kundpresentationer under 2024 med NPS-snitt 8,4/10" |
| **"Noggrann"** | Mätbart men inte mätt | "Korrekturläste och kvalitetssäkrade årsredovisningar för 18 enheter med noll rättelser i extern revision" |
| **"Stresstålig"** | Säg det inte — visa det | "Levererade migrationen av 28 system inom 6 veckor med samtidig daglig drift" |
| **"Flexibel"** | Säger inget om vad du faktiskt gör annorlunda | "Roterade mellan tre roller under omorganisation 2024 — från utvecklare via produktledare till tech lead" |
| **"Resultatorienterad"** | Vag — kvantifiera istället | Skriv siffrorna direkt |
| **"Lösningsorienterad"** | Floskel | "Identifierade flaskhals i deploypipelinen och förkortade build-tid från 18 till 4 min" |
| **"Tänker utanför boxen"** | Tomt | "Föreslog och drev pilot med RAG-baserad kundsupportbot som minskade ärendetid med 38 %" |
| **"Härmed söker jag tjänsten…"** (i personligt brev) | Trött standard-fras | Börja med en hook: starkaste resultat, koppling till företaget, eller relevant insikt |
| **"Passionerad"** | Översatt "passionate" — känns konstruerat | Specifikt: "Har följt och bidragit till open-source-projekt X i 3 år" |
| **"Hårt arbetande"** | Förväntas av alla | Konkret prestation som visar arbetsmoral |
| **"Self-starter"** / **"egen drivkraft"** | Klyscha | "Initierade och drev pilotprojekt Y från idé till lansering på 4 månader utan dedikerad budget" |

**Akavia-rådet:** "Försök att undvika vanliga klichéer som 'jag är en lagspelare' eller 'jag är en hårt arbetande person' och fokusera istället på att ge unika eller personliga exempel" (https://www.akavia.se/rad-och-stod/soka-jobb/jobbansokan/sa-skriver-du-personligt-brev/).

### 6.2 Konkreta CV-meningar — före/efter

**Säljare:**

❌ "Ansvarig för försäljning och kundkontakter. Var en lagspelare som alltid gav 110 % och tänkte utanför boxen."

✅ "Drev B2B-försäljning mot mid-market inom SaaS; ökade årlig nyförsäljning från 3,2 MSEK till 5,8 MSEK (+81 %) under 2023 genom att etablera 14 nya nyckelkunder."

**Profiltext utvecklare:**

❌ "Objective: To obtain a challenging position where I can utilize my skills. Driven, passionerad, lösningsorienterad teamspelare med stark arbetsmoral."

✅ "Civilingenjör med 7 års erfarenhet av datadriven produktutveckling inom fintech, främst Python och AWS. Senast lead för team om 4 utvecklare på Klarna där vi minskade onboarding-tiden för nya kunder från 48 till 12 timmar."

**Bullet, mjuk kompetens:**

❌ "Stark kommunikatör med utmärkta ledaregenskaper. Bidrog till att teamet nådde sina mål."

✅ "Ledde dagliga stand-ups för cross-funktionellt team om 8 personer (UX, dev, data) under 18 månader; presenterade kvartalsrapporter till ledningsgrupp och säkrade fortsatt finansiering om 4,2 MSEK för 2024."

### 6.3 50 starka svenska action verbs (preteritum)

**Ledarskap & ansvar:** ledde, drev, ansvarade för, koordinerade, samordnade, fattade beslut om, prioriterade, delegerade, mentorerade, coachade.

**Bygga & skapa:** byggde, etablerade, lanserade, grundade, utvecklade, designade, skapade, konceptualiserade, införde, implementerade, initierade.

**Förbättra & optimera:** ökade, förbättrade, optimerade, effektiviserade, accelererade, automatiserade, strömlinjeformade, höjde, dubblerade, halverade, reducerade.

**Leverera & genomföra:** levererade, genomförde, slutförde, verkställde, realiserade, sjösatte, rullade ut, driftsatte.

**Analys & beslut:** analyserade, utvärderade, kartlade, identifierade, definierade, validerade, prognostiserade, granskade.

**Sälj & relation:** sålde, förhandlade, slöt, vann, säkrade, omsatte, växte (omsättning).

**Kommunikation:** presenterade, förespråkade, utbildade, faciliterade, författade.

**Att undvika (svaga verb — Fail-signal A2/C3):** *var ansvarig för, var med och, hjälpte till med, arbetade med, deltog i, fick möjlighet att, hade hand om, jobbade med.*

---

## 7. Branschvariation

JobbPilot ska generera CV anpassade till bransch. Detta avsnitt definierar branschspecifika regelvarianter ovanpå basrubric §2.

### 7.1 Offentlig sektor (kommun, region, myndighet)

- **Mall-ansökningar vanliga.** Standardiserade Varbi/ReachMee-formulär.
- **Saklig grund styr.** LOA (1994:260) + regeringsformen: "Vid beslut om statliga anställningar ska avseende fästas endast vid sakliga grunder, såsom förtjänst och skicklighet" (ST, https://www.st.org/rad-och-stod/bra-att-veta-nar-du-soker-du-jobb-inom-staten).
- **Skall-krav är skall-krav.** Statlig arbetsgivare måste anlita kandidat som uppfyller annonsens krav.
- **Ansökningar är offentlig handling** — inkludera inget du inte vill ska bli offentligt.
- **Säkerhetsprövning** kan tillkomma.
- **Personligt brev** ofta ersatt med urvalsfrågor i formulär.
- **Meritförteckning** används som begrepp men är i praktiken samma som CV.
- **Anonymiserad rekrytering** vanligt (Varbi-modul).

**JobbPilot-implikation:** För "yrke = offentlig sektor" → generera CV som **fokuserar på matchning mot kravprofil**, lägg till sektion "Meritsammanställning mot kravprofil" som speglar annonsens skall-krav punkt för punkt. Lyft fram även **separat hjälp att fylla i Varbi-urvalsfrågor**.

### 7.2 Akademi

- **Meritportfölj är central.** Vid läraranställningar och befordran krävs ofta omfattande portfölj. LTH-exempel (https://www.lth.se/fileadmin/lth/anstallda/personal/Akademisk_meritportfoelj_STYR_2015319.pdf) [verifiera 2026]:
  - A: Försättsblad + personligt brev (max ~1200 ord)
  - B: CV
  - C: Publikationslista
  - D: Vetenskaplig meritportfölj
  - E: Pedagogisk meritportfölj
  - F: Ledarskap/administrativa uppdrag
  - G: Innovation/entreprenörskap/samverkan
  - H: Bilagda utvalda publikationer
- **Pedagogisk portfölj** är ett **eget dokument** med reflekterande avsnitt (GU PIL-enheten, https://www.pil.gu.se/resurser/att-skriva-en-pedagogisk-portfolj).
- **Publikationer** hanteras ofta i Prisma (för VR, Forte m.fl.).
- **Längd**: akademiska CV kan vara många sidor.
- **Engelska**: vanligt; ECTS istället för GPA.

**JobbPilot-implikation:** Akademiskt CV är **ett separat format** som kräver lokala variationer per lärosäte. Fas 4 bör erbjuda en akademisk grundmall; full meritportfölj är troligen **utanför Fas 4-scope** (kandidat till Fas 5/6).

### 7.3 Privat näringsliv (klassiskt)

- **Kortare, säljande CV.** 1–2 sidor.
- **Mätbara resultat förväntas.**
- **Personligt brev krävs ofta** men varierar — kolla annonsen.
- **Branding-element OK** (modern layout, viss färgsättning) men professionellt.

### 7.4 Tech/startup

- **Engelska default** även för rent svenska bolag med engelska som koncernspråk.
- **GitHub, portfolio, LinkedIn länkas obligatoriskt.** Senior nivå: GitHub-länk eller portfolio är förväntat.
- **Tech stack-lista central** — språk, ramverk, databaser, cloud.
- **Quantified achievements är hygienfaktor.**
- **Inget foto, inget personnummer, ingen civilstånd.**
- **Certifikat (AWS, GCP, Azure)** kan tippa över intervju.
- **ATS-vänlig PDF** — enkolumn, standardrubriker.

### 7.5 Vård

- **Legitimation och skyddad yrkestitel** är central. Sedan 1 juli 2023 är undersköterska skyddad yrkestitel (Vårdförbundet, https://www.vardforbundet.se/rad-och-stod/yrkesansvar/legitimation/).
- **Legitimationsnummer från Socialstyrelsen / HOSP-registret** ska anges.
- **Journalsystem ska anges**: Cosmic, TakeCare, Melior.
- **HLR-certifiering, specialiseringar** anges.
- **Specialistsjuksköterskeexamen** kräver skyddad specialistbeteckning.
- **Polisens belastningsregister** kontrolleras innan legitimation utfärdas.

### 7.6 Skola/lärare

- **Lärarlegitimation från Skolverket** är grundkrav.
- **Professionsprogrammet** med **meriteringsnivåer 1–4** infört 1 juli 2025 (Skolverket, https://www.skolverket.se/kompetensutveckling/kurser-och-utbildningar/professionsprogrammet/meritering/ansok-om-meritering-inom-professionsprogrammet).
- **Ämnesbehörigheter, åldersgrupper, fortbildning** ska anges tydligt.
- **VFU/lärarlyft** kan vara meriterande.

### 7.7 Branschvariations-matris för generator

| Bransch | Foto | Personligt brev | Längd | Språk | Speciella sektioner | Stil |
|---|---|---|---|---|---|---|
| Offentlig | Nej | Sällan, ersätts av formulär | 2 sidor | Svenska | Skall-krav-matchning | Saklig |
| Akademi | Sällan | Ja, motivationstext | 3+ med bilagor | Engelska eller svenska | Publikationer, pedagogisk meritportfölj | Saklig |
| Privat klassisk | Opt-in | Ofta ja | 1–2 sidor | Svenska | – | Säljande sakligt |
| Tech/startup | Nej | Sällan | 1–2 sidor | Engelska ofta | GitHub, tech stack | Konkret, datadriven |
| Vård | Opt-in | Ja | 1–2 sidor | Svenska | Legitimation, journalsystem | Formell |
| Skola | Opt-in | Ja | 1–2 sidor | Svenska | Lärarleg, ämnesbehörighet, meriteringsnivå | Formell |

---

## 8. Öppna frågor och saker Fas 4-teamet bör re-verifiera

Detta är **kvarvarande osäkerheter** som teamet bör adressera under sprint 0 av Fas 4.

1. **IMY-vägledning för rekrytering** — ingen ny dedikerad CV-vägledning publicerad 2025–2026 som verifierats. **Antagande**: principerna om dataminimering och förbud mot känsliga personuppgifter gäller fortsatt. Re-verifiera mot senaste IMY-publikation vid byggstart.
2. **DO årsrapport 2025/2026** — om publicerad, uppdatera diskriminerings­statistik.
3. **EU AI Act-implementation** — högrisk-klassificeringen för rekryterings-AI träder i kraft 2027. JobbPilot är **inte direkt** klassad som högrisk (det är ATS som *fattar beslut* som är det), men transparens-, dokumentations- och loggningskrav kan smita ner till leverantörer. Konsultera juridik.
4. **Personligt brev-trend** — Ingenjören-citatet om "staten har helt tagit bort kravet" (2025-09-02) är ett rekryterar-uttalande, inte officiellt regelverk. Verifiera mot Statens servicecenter/Arbetsgivarverket.
5. **TNG SEB-case** är från 2023–2024, bör verifieras om fortfarande relevant 2026.
6. **Arbetsförmedlingens öppna taxonomi (`jobtechdev.se`)** — verifiera API-stabilitet, rate limits, SLA innan beroende byggs.
7. **Teamtailor parser-precision** — Teamtailors egen dokumentation säger auto-extraktion av endast namn/e-post/telefon/LinkedIn. Mer avancerad extraktion kräver Co-pilot. Verifiera om detta gäller 2026 eftersom marknaden rör sig snabbt.
8. **Self-preference bias mitigation** — Pombal et al. (arXiv:2604.06996, 2026) är preprint. Verifiera replikering. **Antagande för v1.0**: använd Claude för generering, GPT-4 eller Gemini för bedömning, lägg till deterministisk regelmotor (regex, läsbarhet) ovanpå LLM-bedömning för objektiva kriterier.
9. **Branschspecifik validering** — engagera 2–3 svenska rekryterare per bransch (offentlig, tech, vård) för att kalibrera rubric mot praxis.
10. **Anonymiserad rekrytering-utbredning 2026** — TNG är prominent förespråkare men andelen svenska arbetsgivare som faktiskt anonymiserar är inte etablerad i oberoende statistik. Detta påverkar **foto-defaultens** styrka.
11. **Filformatsstöd för uppladdning** — JobbPilot bör stödja PDF (textbaserad), DOCX, ev. fotouppladdning (JPG/PNG) för screenshots. **DOC (gamla Word) avråds** p.g.a. encoding-risk på åäö.
12. **OCR-leverantörsval** — AWS Textract är default eftersom JobbPilot använder Bedrock; verifiera prislapp och svenska språkprestanda mot Google Vision och Tesseract.
13. **Streaming-infrastruktur** — Bedrock stödjer streaming för Claude och Mistral. Verifiera latency-mått för svenska region (eu-north-1 Stockholm vs eu-west-1 Irland) — påverkar streaming-UX-känsla.
14. **GDPR-dataretention för CV-data** — IMY:s rek: avidentifiera eller radera efter avslutad rekrytering. JobbPilot bör som **default radera CV-data efter X månader** med opt-in att behålla.
15. **Personnummer-guard implementation** — verifiera regex för svenska personnummer (`YYMMDD-XXXX` eller `YYYYMMDD-XXXX`) och samordningsnummer (`YYMMDD-XXXX` med dag+60). Strip vid inmatning.
16. **"Behåll min design"-feature** — tekniskt komplext (PDF-layout-bevarande). Verifiera scope: är det Fas 4 eller Fas 5? Fallback alltid till "föreslå innehåll, ny mall".
17. **Score-presentation** — A/B-testa kategoriscore vs totalscore mot drop-off och konvertering till "fixa-knapp".
18. **Foto bakgrundsborttagning** — om Remove.bg-API används, GDPR-DPA krävs (data lämnar EU?). AWS Bedrock SAM2 är säkrare alternativ men kräver mer eget arbete.
19. **Akademiskt CV** — bekräfta att det är **utanför Fas 4-scope** eller om en enklare variant ska stödjas (CV utan full meritportfölj).
20. **Personligt brev som separat artefakt** — Fas 4 eller senare? Kräver egen mini-rubric.

---

## 9. Källor

### 9.1 Auktoritativa svenska källor (myndigheter, fackförbund)

| URL | Titel | Organisation | Datum |
|---|---|---|---|
| https://arbetsformedlingen.se/other-languages/english-engelska/cv-application-and-interview/writing-a-cv | Writing a CV / Skriva CV | Arbetsförmedlingen | löpande, 2026 |
| https://arbetsformedlingen.se/other-languages/english-engelska/cv-application-and-interview/writing-a-personal-letter | Writing a Personal Letter | Arbetsförmedlingen | löpande, 2026 |
| https://www.saco.se/yrkesliv/jobb/jag-ska-soka-jobb/cv-och-personligt-brev/ | CV och personligt brev | Saco | [verifiera 2026] |
| https://www.unionen.se/filer/ovrigt/cv-mallar | CV-mall | Unionen | löpande |
| https://www.unionen.se/medlemskapet/karriar-och-utveckling/byta-jobb/cv-pa-engelska | Mall: CV på engelska | Unionen | löpande |
| https://www.unionen.se/story/opinion-stories/boosta-din-kunskap-2025 | Boosta din kunskap 2025 | Unionen | 2025 |
| https://www.akavia.se/rad-och-stod/soka-jobb/jobbansokan/sa-skriver-du-personligt-brev/ | Så skriver du personligt brev | Akavia | [verifiera 2026] |
| https://www.imy.se/verksamhet/dataskydd/dataskydd-pa-olika-omraden/arbetsliv/rekryteringssystem-och-kompetensdatabaser/ | Rekryteringssystem och kompetensdatabaser | IMY | löpande |
| https://www.imy.se/nyheter/sa-har-far-arbetsgivare-hantera-personuppgifter/ | Så här får arbetsgivare hantera personuppgifter | IMY | [verifiera 2026] |
| https://www.imy.se/verksamhet/dataskydd/dataskydd-pa-olika-omraden/arbetsliv/ | Behandling av personuppgifter i arbetslivet | IMY | löpande |
| https://www.do.se/for-arbetsgivare-och-utbildningsanordnare/aktiva-atgarder-for-arbetsgivare/kontinuerligt-arbete-mot-diskriminering-fyra-steg/rekrytering-utan-diskriminering | Rekrytering utan diskriminering med aktiva åtgärder | DO | löpande |
| https://www.do.se/download/18.36cbb9ac1886717f72d416/1686638418878/rapport-rekrytera-utan-att-diskriminera.pdf | Rapport 2023:5 Rekrytera utan att diskriminera | DO | 2023 [verifiera 2026] |
| https://www.skolverket.se/kompetensutveckling/kurser-och-utbildningar/professionsprogrammet/meritering/ansok-om-meritering-inom-professionsprogrammet | Professionsprogrammet — meritering | Skolverket | 2025 |
| https://legitimation.socialstyrelsen.se/ | Legitimation | Socialstyrelsen | löpande |
| https://www.vardforbundet.se/rad-och-stod/yrkesansvar/legitimation/ | Legitimation | Vårdförbundet | löpande |
| https://www.st.org/rad-och-stod/bra-att-veta-nar-du-soker-du-jobb-inom-staten | Vill du söka jobb inom staten? | Fackförbundet ST | löpande |

### 9.2 Universitetens karriärservice och akademiska källor

| URL | Titel | Organisation | Datum |
|---|---|---|---|
| https://utbildning.ki.se/hur-skriver-man-ett-cv | 10 tips på hur du skriver CV | KI Karriärservice | uppdaterad 2026-05-06 |
| https://ki.se/en/about-ki/jobs-at-ki/qualifications-portfolio-for-teachers-and-researchers | Meritportfölj för lärare och forskare | KI | löpande |
| https://www.lth.se/fileadmin/lth/anstallda/personal/Akademisk_meritportfoelj_STYR_2015319.pdf | Akademisk meritportfölj | LTH | [verifiera 2026] |
| https://www.pil.gu.se/resurser/att-skriva-en-pedagogisk-portfolj | Att skriva en pedagogisk portfölj | Göteborgs universitet PIL | löpande |
| https://www.miun.se/medarbetare/undervisning/larandestod/pedagogisk-utveckling/pedagogisk-meritering | Pedagogisk meritering | Mittuniversitetet | uppdaterad 2025 |
| https://www.du.se/sv/medarbetarwebb/utbilda-och-forska/kompetensutveckling/kompetensutveckling--for-dig-som-undervisar/pedagogisk-meritering/ | Pedagogisk meritering (nya regler 1 juli 2025) | Högskolan Dalarna | 2025 |
| https://prismasupport.research.se/forskare/soka-bidrag/cv-och-publikationer.html | CV och publikationer i Prisma | Prisma/VR | löpande |

### 9.3 Rekryteringsföretag och konsultbolag

| URL | Titel | Organisation | Datum |
|---|---|---|---|
| https://www.tng.se/fordomsfri-rekrytering/ | Fördomsfri rekrytering | TNG | löpande |
| https://www.tng.se/fordomsfri-rekrytering/anonymiserade-ansokningar-gav-seb-storre-mangfald/ | Anonyma ansökningar hos SEB:s trainee | TNG | [verifiera 2026] |
| https://www.tng.se/blogg/nytt-fran-tng/ | Nytt från TNG | TNG | 2025–2026 |
| https://www.tng.se/rekrytering/ | Rekrytering | TNG | löpande |
| https://www.tng.se/soka-jobb/ai-i-jobbansokan | AI i jobbansökan | TNG | 2025 |
| https://www.academicwork.se/artiklar/soka-jobb/cv-mall-exempel | 3 CV-mallar gratis | Academic Work | löpande |
| https://www.academicwork.se/artiklar/soka-jobb/cv-bild-tips--rekommendationer | CV-bild eller inte? | Academic Work | löpande |
| https://www.academicwork.se/artiklar/soka-jobb/tips-for-att-skriva-personligt-brev | Skriva personligt brev | Academic Work | löpande |
| https://www.wise.se/artiklar/kompetensbaserad-rekrytering/ | Kompetensbaserad rekrytering | Wise Professionals | löpande |
| https://www.randstad.se/karriartips/hitta-jobb/10-enkla-steg-till-ett-bra-cv/ | 10 enkla steg till ett bra CV | Randstad | 2026 |
| https://www.manpower.se/sv/jobbsokande/kom-igang/cv | Gratis CV-mallar | Manpower | löpande |
| https://barona.se/for-jobbsokande/cv/kronologiskt-cv/ | Kronologiskt CV Guide | Barona | löpande |
| https://www.homeofrecruitment.se/kunskapsbanken/rekrytera-utifr%C3%A5n-potential-utan-personligt-brev | Rekrytera utan personligt brev | Home of Recruitment | löpande |

### 9.4 Jobbplattformar och CV-tjänster

| URL | Titel | Organisation | Datum |
|---|---|---|---|
| https://www.monster.se/karriarradgivning/artikel/att-skriva-varldens-basta-cv | Att skriva ett CV: 15 tips | Monster.se | [verifiera 2026] |
| https://se.indeed.com/karriarrad/cv-personligt-brev/sa-skriver-du-ditt-cv-pa-engelska | Så skriver du ditt CV på engelska | Indeed.se | löpande |
| https://www.livecareer.se/cv/what-should-a-cv-look-like | Hur ska ett bra CV se ut 2026? | LiveCareer.se | 2026 |
| https://www.livecareer.se/cv/what-to-include-in-a-cv | Vad som ska inkluderas i ett CV | LiveCareer.se | 2026 |
| https://www.cv.se/tips/personuppgifter-cv | Personuppgifter i CV | CV.se | löpande |
| https://www.cv.se/tips/struktur-av-cv | Struktur av CV | CV.se | löpande |
| https://cvmall.se/cv-exempel/programmerare | CV programmerare 2026 | cvmall.se | 2026 |
| https://www.cv-mallen.se/bild-i-cv/ | Bild i CV 2026 | CV-mallen.se | 2026 |
| https://owlapply.com/sv/blogg/sa-skriver-du-personligt-brev-sverige | Så skriver du personligt brev Sverige | OwlApply | 2025 |
| https://ingenjoren.se/2025/09/02/personliga-brevet-ar-pa-vag-bort-sa-har-soker-du-nu/ | Personliga brevet är på väg bort | Ingenjören | 2025-09-02 |

### 9.5 ATS-leverantörer och teknisk dokumentation

| URL | Titel | Organisation | Datum |
|---|---|---|---|
| https://support.teamtailor.com (Co-pilot Privacy and Security, Resume summaries, AI features) | Teamtailor Support | Teamtailor | 2024–2025 |
| https://www.teamtailor.com/en/productnews/ask-co-pilot | Ask Co-pilot | Teamtailor | 2024 |
| https://partner.teamtailor.com | Teamtailor Partner API | Teamtailor | löpande |
| https://grade.com/rekrytering/varbi-rekryteringssystem/ | Varbi rekryteringssystem | Grade/Varbi | 2025 |
| https://www.varbi.com (via news.cision.com) | Varbi press 2023 (Arbetsförmedlingen) | Varbi | 2023 |
| https://www.lu.se/om-universitetet/jobba-hos-oss | Jobba hos oss — filformat | Lunds universitet | löpande |
| https://jobylon.com/blog/what-is-an-ats | What is an ATS | Jobylon | 2024 |
| https://www.workday.com/en-se | Workday Sverige | Workday | 2026 |
| https://www.businesswith.se/ats-system/ | ATS-system jämförelse | BusinessWith | 2026 |

### 9.6 Internationell ATS- och UX-forskning (flaggade som internationella)

| URL/referens | Titel | Organisation | Datum |
|---|---|---|---|
| https://www.nngroup.com (Progressive Disclosure, Cognitive Load in Forms, Designing for Long Waits) | Nielsen Norman Group artiklar | NN/g | 1995–2026 |
| https://baymard.com (Checkout Optimization: From 16 to 8 Fields; Minimize Form Fields) | Baymard Institute | Baymard | 2019, 2024 |
| arXiv:2410.21819 | Self-Preference Bias in LLM-as-a-Judge | Wataoka et al. | 2024 |
| arXiv:2410.02736 | Justice or Prejudice? Biases in LLM-as-a-Judge | Li et al. | 2024 |
| arXiv:2604.06996 | Self-Preference Bias in Rubric-Based Evaluation | Pombal, Rei, Martins | 2026 |
| arXiv:2504.03846 | Do LLM Evaluators Prefer Themselves for a Reason? | Ho et al. | 2025 |
| arXiv:2602.02219 | Position Bias in Rubric-Based LLM-as-a-Judge | – | 2026 |
| https://www.jobscan.co | State of the ATS; ATS parsing guides | Jobscan | 2025–2026 |
| https://www.tealhq.com | Teal CV checker | Teal HQ | 2026 |
| https://www.resumeworded.com | ResumeWorded 4-faktor-rubric | ResumeWorded | 2025–2026 |
| Ladders Inc. eye-tracking study (refererad i HR Dive, PRNewswire) | 7,4 s skanning, F-mönster | Ladders | 2018 [indikativ, ej exakt] |
| https://data.jobtechdev.se | JobTech öppna data (SSYK, yrkesbenämningar) | Arbetsförmedlingen JobTech | löpande |
| https://www.scb.se (MIS 2012:1) | SSYK 2012 | SCB | 2012 |
| https://www.tiptap.dev (Content AI: Diff utility) | Tiptap diff utility | Tiptap | 2026 |
| https://www.telerik.com (Loading UI/UX Patterns for AI) | Streaming UI patterns | Telerik / Progress | 2026 |
| https://www.kollega.se (AI-driven rekrytering växer) | AI-rekrytering kollega | Kollega | aug 2025 |
| https://www.kompetensforetagen.se | EU AI Act i rekrytering | Kompetensföretagen | dec 2025 |

### 9.7 Källkvalitetsanmärkning

- **Ladders 7,4 s-studien** är kommersiell och kritiserad metodologiskt (ERE, Spectacle Talent Partners). Använd som **indikativ**, ej exakt.
- **Jobscan/Teal-tröskelvärden (75 %, 80 %)** är produktrekommendationer, inte oberoende forskning.
- **ATS-leverantörers marknadsföringssiffror** ("10 000+ kunder", "70 000+ användare") är leverantörens egna och inte tredjepartsverifierade. Den faktiska svenska marknadsandelen för respektive ATS är inte oberoende rapporterad.
- **arXiv-papper om LLM-self-preference bias** är preprints (utom de äldre). Metoderna är replikerbara men slutsatser kan justeras.
- **Form-completion-siffror** som "300 % ökning" som cirkulerar i UX-bloggar härleds från ett fåtal primära källor (Venture Harbour, LeadCapture.io). Behandla som **direktionella, inte absoluta**. Baymards data är robustare (5 700+ sessioner primärforskning).
- **Svenska CV-tjänster (CV.se, cvmall.se, LiveCareer.se, CV-mallen.se)** har kommersiellt intresse av trendigt innehåll och behandlas som **sekundärkällor** jämfört med fack och myndigheter — men reflekterar väl rådande praxis 2025–2026.

---

**Slut på kunskapsbank v1.0 · 2026-05-17.** Re-verifiering planerad inom 6 månader eller vid större förändringar i IMY-vägledning, EU AI Act-implementation eller dominerande svenska ATS-systemens parsing-beteenden.