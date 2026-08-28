import { z } from "zod";
import { type JobAdSortBy } from "./job-ads";
import { taxonomyLabelSchema } from "./taxonomy";
import { SAVED_SEARCH_SORT_ORDER } from "./saved-searches";

/**
 * ADR 0060 — RecentJobSearches (auto-fångade sökningar). Spegelt backend
 * `RecentJobSearchDto` (`Jobbliggaren.Application.RecentJobSearches.Queries`).
 * Skild från SavedSearch (manuell-spara, ADR 0039) — auto-capture-semantik
 * via post-handler-pipeline-behavior.
 *
 * Listan är cap=20 per JobSeeker (RecentJobSearch.MaxPerSeeker) — Capturer
 * evictar äldsta LastViewedAt vid overflow. `currentCount` = live-räknat
 * antal matchande job-ads just nu; `newCount` = `max(0, currentCount - lastSeenCount)`
 * driver "(N nya)"-affordance i hero-chip.
 *
 * SortBy serialiseras som heltal (samma konvention som SavedSearchDto;
 * SAVED_SEARCH_SORT_ORDER är auktoritativ ordinal-tabell).
 */
const sortByFromWire = z
  .union([z.number().int(), z.string()])
  .transform((v, ctx): JobAdSortBy => {
    if (typeof v === "number") {
      const name = SAVED_SEARCH_SORT_ORDER[v];
      if (name) return name;
      ctx.addIssue({ code: "custom", message: `Okänt SortBy-index: ${v}` });
      return z.NEVER;
    }
    const matched = SAVED_SEARCH_SORT_ORDER.find((name) => name === v);
    if (matched) return matched;
    ctx.addIssue({ code: "custom", message: `Okänt SortBy: ${v}` });
    return z.NEVER;
  });

/**
 * #1430 — labeln är STRUKTUR, inte prosa. Backend härleder VILKEN dimension som namnger
 * raden och HUR delarna hänger ihop; orden ligger i `messages/{sv,en}/jobads.json` och
 * fogas av `buildRecentSearchLabel`. En färdig sträng kunde bara vara på ett språk, och
 * den nådde en engelsk användare ordagrant på tre ytor.
 *
 * Enums når wire:n som NAMN (backend `JsonStringEnumConverter`), aldrig som ordinaler —
 * pinnat ände-till-ände i `RecentSearchesTests`.
 */
// Diskriminerad union, inte ett löst objekt: DTO:ns kontrakt säger att `text` är null EXAKT
// när delen är `Remote`, och en spegel som är lösare än originalet på just den villkorade
// punkten speglar inte kontraktet (ADR 0060 Beslut 9). Utan unionen parsar en `Named` utan
// text grönt och renderas som en tom sträng mitt i labeln — samma tysta fel som `parts`-
// refine:n nedan finns för att stoppa.
export const recentSearchLabelPartSchema = z.discriminatedUnion("kind", [
  z.object({
    kind: z.literal("Named"),
    text: z.string(),
    moreCount: z.number().int().nonnegative(),
  }),
  // "Remote" bär inget namn: den är ett ORD, och vilket ord beror på locale OCH position.
  // Positionen läses ur `parts`-ordningen, så delen behöver ingen egen flagga.
  z.object({
    kind: z.literal("Remote"),
    text: z.null(),
    moreCount: z.literal(0),
  }),
]);

export const recentSearchLabelSchema = z
  .object({
    kind: z.enum(["Query", "OccupationField", "Dimensions", "All"]),
    join: z.enum(["None", "Disjunction", "Conjunction"]),
    parts: z.array(recentSearchLabelPartSchema),
  })
  // Högljutt före tyst fel, samma doktrin som `remote` nedan: varje gren utom `All` skjuter
  // minst en del, så en tom `parts` är ett kontraktsbrott och inte ett tomt läge. Utan den
  // här grinden hade komponeraren tvingats välja mellan en tom rubrik och att påstå "alla
  // annonser" om ett tillstånd den inte känner — ett falskt påstående. Nu faller parsen i
  // stället och ytan degraderar till `{kind:"error"}`.
  .refine((label) => label.kind === "All" || label.parts.length > 0, {
    message: "En label som inte är 'All' måste bära minst en del",
    path: ["parts"],
  })
  // `None` betyder att ingenting fogas. Släpps den igenom med flera delar faller de till
  // konjunktionsgrenen och renderas som om axlarna hölle samtidigt — ett falskt påstående om
  // sökpredikatet, alltså exakt den felklass `Join` infördes för att förhindra.
  .refine((label) => label.join !== "None" || label.parts.length <= 1, {
    message: "Join 'None' fogar ingenting och är oförenlig med flera delar",
    path: ["join"],
  });
export type RecentSearchLabel = z.infer<typeof recentSearchLabelSchema>;
export type RecentSearchLabelPart = z.infer<typeof recentSearchLabelPartSchema>;

export const recentJobSearchDtoSchema = z.object({
  id: z.string(),
  q: z.string().nullable(),
  // ADR 0067 Fas E2a — yrke-dimensionen är yrkesgrupp (ssyk-level-4), ej
  // occupation-name. Backend `RecentJobSearchDto` bär `occupationGroupList`
  // (C2-reverse-lookup-migrerade ids); FE konsumerar yrkesgrupp-fältet.
  // Fas E2b: municipality-dimensionen (Län→Kommun-kaskaden) konsumeras —
  // backend-fälten fanns sedan C2.
  occupationGroupList: z.array(z.string()),
  municipalityList: z.array(z.string()),
  regionList: z.array(z.string()),
  // ADR 0067 Beslut 6 (Fas B2) — Klass 2: anställningsform + omfattning. Råa
  // concept-id-listor (UTAN labels — taxonomi-reverse-lookup för Klass 2 är
  // Fas E PR-4-concern). Konsumeras för replay (buildHrefFor) så "Kör igen"
  // bär Klass 2-filtret. Backend `RecentJobSearchDto` bär dem sedan B2/#60.
  employmentTypeList: z.array(z.string()),
  worktimeExtentList: z.array(z.string()),
  // #1407 (#551 punkt 4) — distans-axeln. OBLIGATORISK, inte `.default(false)`, av
  // två skäl som båda är husets egna: varje RÅTT dimensionsfält här är required (bara
  // de tre `*Labels` defaultar), och högljutt-före-tyst-fel är ratificerat (ADR 0067
  // rad 41; CTO 2026-06-13 "hellre ingen siffra än falsk (0)"). En default hade gjort
  // ett saknat wire-fält oskiljbart från ett falskt — det här felet, ett lager ut.
  //
  // Det ger ett verkligt skew-fönster, och det är #1238: publiceringen är en
  // fem-cells-matris utan fan-in, så `IMAGE_TAG`-defaulten `latest` kan resolva till
  // nytt `web` mot gammalt `api` (`deploy/systemd/jobbliggaren-reconcile.timer` bär
  // härledningen). Kostnaden är mätt och avgränsad: `responseToResult` fångar
  // parse-felet, ytan degraderar till `{kind:"error"}`, och nästa reconcile läker det.
  remote: z.boolean(),
  occupationGroupLabels: z.array(taxonomyLabelSchema).default([]),
  municipalityLabels: z.array(taxonomyLabelSchema).default([]),
  regionLabels: z.array(taxonomyLabelSchema).default([]),
  sortBy: sortByFromWire,
  label: recentSearchLabelSchema,
  currentCount: z.number().int().nonnegative(),
  newCount: z.number().int().nonnegative(),
  lastViewedAt: z.string(),
});
export type RecentJobSearchDto = z.infer<typeof recentJobSearchDtoSchema>;

// ListRecentSearches returnerar ren array (paritet med ListSavedSearches —
// cap=20 betyder få rader, ingen paginering behövs).
export const listRecentSearchesResultSchema = z.array(recentJobSearchDtoSchema);
export type ListRecentSearchesResult = z.infer<
  typeof listRecentSearchesResultSchema
>;
