import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";

import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

export async function generateMetadata(): Promise<Metadata> {
  return notFoundMetadata();
}

interface Props {
  params: Promise<{ id: string }>;
}

/**
 * /cv/[id] — WYSIWYG-redigering av ett SPARAT CV, RETIRED (pausad, inte raderad
 * — #1373, Klas-direktiv 2026-08-17 ordagrant: "även redigeringen ska vara
 * 'pausad' under MVP. Det enda som ska funka är att ladda upp CV och granska CV
 * (få förslag) men inte skapa nytt CV och inte heller kunna redigera uppladdat
 * CV, varken som funktion eller vid granskning."). MVP:ns CV-yta är därmed
 * exakt två saker: LADDA UPP och GRANSKA.
 *
 * Routen behålls och returnerar 404 på route-nivå, så en gissad, bokmärkt eller
 * autocompletead URL inte kan nå ett fungerande redigeringsformulär.
 *
 * Medvetet notFound(), INTE permanentRedirect: redigeringen är pausad, inte
 * ersatt — ingenting tar över dess funktion, så en 308 hade påstått en flytt som
 * aldrig skett OCH cachats permanent av webbläsare, vilket låst ute besökare
 * även efter att redigeringen återvänder. Samma mekanism och samma skäl som
 * `cv/ny/page.tsx` (skapa-vägen, #1061), `cv/[id]/mall/page.tsx`
 * (mallbyggaren) och `cv/granska/[parsedId]/forbattra/page.tsx` (åtgärda-lagret).
 *
 * Session-grinden körs FÖRE 404:n: en utloggad besökare landar på /logga-in,
 * aldrig på en 404 som avslöjar att routen finns. Route-existens är ingen
 * auth-orakel åt något håll.
 *
 * ⚠ RADERING OCH NAMNBYTE FÖLJDE INTE MED I PAUSEN, och det är den viktigaste
 * posten här. `DeleteResumeDialog` och `RenameResumeForm` låg på den här sidan.
 * Radering är INTE redigering: att grinda routen utan att flytta dem hade
 * strandat användarens enda finkorniga raderingsväg, och därmed också den enda
 * kvarvarande vägen att ÅTERKALLA personnummer-samtycket för ett redan sparat
 * CV (originalfilen lagras på samtycke — se `Domain/Resumes/Files/ResumeFile.cs`).
 * GDPR Art. 7(3) kräver att en återkallelse är LIKA LÄTT som samtycket var att
 * ge; kontoradering (lösenord + 30 dagars väntan) uppfyller inte det, och en
 * e-postadress gör det inte heller för just samtycken. Båda kontrollerna ligger
 * därför nu på CV-kortet i hubben (`components/resumes/resume-card.tsx`), där de
 * hör hemma på egna meriter: radering är en BIBLIOTEKS-operation, och hubben bär
 * redan samma mönster för det andra CV-artefakten (`DiscardDraftButton`).
 * Namnbytet behölls på Klas beslut 2026-08-17 — namnet är etikett-metadata
 * (`resumes.name`, plaintext) och inte CV-innehåll (DEK-krypterat), och utan det
 * går två CV importerade samma dag inte att skilja åt (den genererade etiketten
 * är "Importerat CV <ÅÅÅÅ-MM-DD>", bara datum, ingen tid).
 *
 * Kvar i trädet, inert och orört (billig återgång slår städning — samma
 * precedens som mallbyggaren och skapa-vägen): `resume-content-form.tsx` med
 * sitt enhetstest, `lib/forms/resume-path-routing.ts`,
 * `lib/actions/resumes.ts:updateMasterContentAction` och i18n-nycklarna
 * `resumes.card.edit` samt `pages.cv.detail.*` (`updatedAt`, `loadErrorTitle`,
 * `errorBody` — verifierat konsumentlösa efter denna ändring; övriga
 * `detail.`-träffar i `src/` tillhör ansökningar, jobbannonser och gästytan).
 * Backend-ytan `PUT /api/v1/resumes/{id}/master` →
 * `UpdateMasterContentCommand` blir onåbar av denna ändring; den retireras
 * tillsammans med `POST /api/v1/resumes` i #1371 (samma fil, samma skäl, samma
 * testfixturer — `ResumesEndpointsTests` övar båda i ETT test). Endast
 * Api-mappningen: handlern stannar, för #650:s personnummer-tripwire bygger sin
 * subject-set över Application-assemblyn och namnger handlern i hårdkodade
 * ankare.
 *
 * ORÖRT: /cv/granska/* och /cv/[id]/granska — granskaren ÄR produkten efter
 * CV-pivoten, och den är läs-bara av konstruktion. Mätt 2026-08-17: de bär noll
 * CV-innehållsredigerare. Den enda skrivningen där är anmärkningsstatus
 * (Öppen/Löst/Ignorerad), som är granskarens egen ledger och inte rör
 * `ResumeContent`.
 */
export default async function CvDetailPage(_props: Props) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  notFound();
}
