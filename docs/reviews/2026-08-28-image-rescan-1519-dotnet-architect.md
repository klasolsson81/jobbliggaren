# dotnet-architect — PR #1545 (`fix/image-rescan-1519`), #1519

**Datum:** 2026-08-28
**Skop:** IaC/CI (CLAUDE.md §9.2, ADR 0036-precedens). Ingen C#-kod i PR:en.
**Status:** Behöver åtgärdas — **0 Kritiskt, 4 Viktigt, 5 Nice-to-have**
**Skala:** Kritiskt/Viktigt/Nice-to-have → Blocker/Major/Minor i den ordningen (§9.6).

> Transkriberad av den anropande sessionen; `dotnet-architect` är read-only och skriver inga filer.

Grundarkitekturen (separat workflow, härledd bildlista, tre utfall) är rätt och ingen av de tre
skulle ändras. Fynden ligger i periferin: den enda oskyddade strängen, det omätta sista ledet,
och ett täckningspåstående som är starkare än mekanismen.

## Viktigt (= Major)

**V1. Script injection via `created`** — `rescan-images.yml:147`, orsak i `resolve-published-digests.sh:163`

`echo "built:   ${{ matrix.image.created }}"` interpolerar registry-levererad data direkt in i ett
`run:`-skalblock. `created` är det enda fältet resolvern inte formkontrollerar (bara `-n` och
`!= "null"`). En `$(...)` i värdet kör i skalet; en **TAB** i det förskjuter fältet tyst genom
`split("\t")` — radbrytning fångas av radräkningen, TAB gör det inte. Filen bygger i övrigt hela
sin trovärdighet på "formen kontrolleras, inte litas på"; detta är undantaget.

Åtgärd: `env:`-mappning i steget + en formkontroll på `created` bredvid digest-kontrollen.

**V2. Det sista ledet är det enda omätta, och repots merge-modell kan bryta det** — `rescan-images.yml:37-43`

*"THE READER IS THE SCHEDULED-RUN NOTIFICATION ... That is the one reader this mechanism has."*
Allt uppströms är mätt till byten; terminalen vilar på ett doc-citat. Och `release-images.yml:6-8`
mäter **själv** att automerge mergar som en **GitHub App** ("app-triggered events start no workflow
runs", counterfactual #1107 mot #1108). GitHub avgör "who created the workflow" på committen som
införde filen på default-grenen; här är det App:ens squash-commit (author bevaras som Klas,
committer är App:en). Vilken attributionen följer är **odokumenterat**, och `permissions:
contents: read` gör att inget annat led kan larma. Ett detektorled som ingen läser är precis den
gröna no-op filen finns för att stänga, en nivå upp.

Åtgärd: mät en gång efter merge. Faller det: gör utdata oberoende av attribution (ett
`issues: write`-steg som öppnar/uppdaterar en issue vid rött — det bryter inte
least-privilege-argumentet, som gäller `packages: write`).

**V3. "the artefact the box runs" är FALSKT under en pinnad `IMAGE_TAG`** — `resolve-published-digests.sh:4`, `docs/runbooks/vps-deploy-stack.md`

`deploy/docker-compose.yml` refererar `${IMAGE_TAG:-latest}` på alla fem, och **samma runbook**,
rad 244 och 440, föreskriver `IMAGE_TAG=sha-<short>` som rollback-procedur. Under en rollback
skannar detektorn `latest` — en **nyare** image än den som körs. Felriktningen är osäker: grön
skanning medan lådan kör en äldre, mer sannolikt sårbar artefakt, och de två tystnaderna är
oskiljbara. En operatör mitt i en rollback är exakt den läsare runbook-stycket skrevs för.

Åtgärd: **stryk påståendet, lägg inte till mekanism** — CI kan inte läsa lådans `.env`, så det
finns ingen kodfix. Stängs mekaniskt enligt §9.6.

**V4. Anti-vakuitetsdisciplinen slutar vid resolverns filgräns** — `rescan-images.yml` som helhet

Fixtursviten pinnar att *skriptet* läser den riktiga `release-images.yml`, men ingenting pinnar
konsumenten. Två hål: (1) radera `rescan-images.yml` och alla fallen förblir gröna — hela
täckningen borta utan ett rött hus någonstans; (2) huvudet säger *"THE PARAMETERS ARE IDENTICAL
TO BOTH EXISTING SCANS, and the parity IS the claim"* — men ingenting mäter pariteten. Ändra
`severity` i `build.yml` och meningen blir tyst falsk. Husmönstret för exakt detta är en vakt med
fixturer, inte en kommentar.

## Nice-to-have (= Minor)

**N1.** `jq`-steget validerar bara `length > 0`; radformen garanteras enbart av producenten, över
en serialiseringsgräns. `.[1] == null` renderar som tom `image-ref`, och `trivy image ""` är inte
garanterat rött.

**N2.** Allt-eller-inget i resolvern mot `fail-fast: false` en nivå ner — två nivåer, motsatt
policy, oskrivet varför. Motiveringen "a partial list still looks like a complete one" bär inte
riktigt, eftersom jobbet *faller*.

**N3.** `resolve`-jobbet kollapsar exit 1 och 2 utan annotation, medan `release-images.yml:203-207`
medvetet gör tvärtom för samma husregel. Eftersom notifieringen är enda läsaren är det just
annotationen som når hen. `| tee` gör dessutom `$?` till pipens status — den klass repot mätt två
gånger.

**N4.** Huvudet säger *"adding a sixth image does not fail for the wrong reason"*, men
`real_names`-assertionen kräver exakt fem namn, så en sjätte **faller** vid PR-tid. Två artefakter
kodar två kontrakt och bara det ena är skrivet. §5 `Comments:` — faktiskt felaktigt påstående.

**N5.** `trivy-action` hämtar sin vulndatabas från `ghcr.io/aquasecurity/trivy-db` anonymt;
GHCR-rate-limit är den mest sannolika källan till ett rött hus som *inte* är en advisory, och
`resolve`-jobbets login gäller inte `scan`-jobbets trivy-container. **Mät innan du löser**;
`TRIVY_USERNAME`/`TRIVY_PASSWORD` är den dokumenterade mitigeringen och kostar ingen ny behörighet.

## Svar på de sex frågorna

1. **Separationen — ja, och kopplingen pekar rätt.** Least privilege är *strukturell*, inte
   deklarativ. Felisoleringsargumentet är skarpare än det formulerades: inuti `release-images.yml`
   skulle Trivy-steget ligga bakom `if: already == 'false'`, så en röd skanning av den gamla
   digesten skulle antingen inte köra alls eller fälla jobbet före Push. Behåll.
2. **DRY mot koppling — rätt avvägning, med en precisering.** "Ingen sjätte kopia" är inte riktigt
   sant: `MIN_LEGS=5` deklarerar kardinaliteten och `.test.sh` deklarerar namnen — men det är inte
   en brist, ett orakel måste vara oberoende av det det mäter. Kopplingen är på YAML-*stavning*,
   inte YAML-*mening*, vilket är skörare än ett `yq`-anrop — men `yq` skulle kosta lokal körbarhet
   på Git Bash, och räknekontrollen konverterar skörheten till ett högljutt rött. Byt inte.
3. **Kontraktet — robust på producentsidan, otäckt vid sömmen.** Två fall: en TAB i `created`
   (V1), och drift eftersom mottagaren inte kontrollerar något (N1). Multi-platform prövades och
   avfärdades: `.Image` blir en map, `{{json .Image.Created}}` renderar `null`, vilket refuseras.
   Fail-loud, korrekt.
4. **1/2-gränsen — rätt satt.** Husstilen bekräftas av `release-images.yml:198-207`; det ledet som
   saknas är annotationen (N3). Styrka värd att notera: golvöverträdelsen är inte bara ett
   05:05-fenomen — `real_repo`-fallet gör den röd vid PR-tid i det blockerande `scripts`-jobbet.
5. **Observe-only-gränsen — rätt, och strukturellt starkare än kommentaren påstår.** `needs:`
   fungerar inte över workflow-filgränser, så `rescan-images` *kan* inte läggas i `ci.needs`. Enda
   vägen in går via branch protection — och ett scheduled workflow kör aldrig på en PR, så den
   required-checken skulle blockera varje PR för alltid inom en timme. Deadlocken är omedelbar och
   synlig, inte tyst.
6. **Fritt angrepp.** Asymmetrin: PR:en mäter *inputsidan* till byten och lämnar *outputsidan* helt
   omätt. Disciplinen bör nå hela vägen till läsaren, inte bara till stdout.

## Referenser

- CLAUDE.md §9.2 (obligatorisk på IaC), §9.6 (V3 och N4 stängs mekaniskt genom strykning)
- AGENTS.md §5 `Comments:` (faktiskt felaktig kommentar är en defekt), §12 (ingen STOPP-klass här)
- `release-images.yml:6-8, 133-140, 193-207, 248-256`
- `docs/runbooks/vps-deploy-stack.md:244, 440` + `deploy/docker-compose.yml:236-492` + `deploy/.env.example:192`
