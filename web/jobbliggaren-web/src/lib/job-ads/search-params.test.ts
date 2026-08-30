import { describe, it, expect } from "vitest";
import {
  buildJobbHref,
  buildPageHref,
  parseEmployerParam,
  withCommitFlag,
  COMMIT_PARAM,
  COMMIT_VALUE,
  type JobbUrlState,
  type JobbRawSearchParams,
  parseQParam,
  serializeJobbAxis,
  JOBB_AXIS_SEPARATOR,
} from "./search-params";

const empty: JobbUrlState = {
  q: "",
  occupationGroup: [],
  region: [],
  municipality: [],
  remote: false,
  employmentType: [],
  worktimeExtent: [],
  matchGrades: [],
  sortBy: "PublishedAtDesc",
};

describe("withCommitFlag (E2j commit-intent-signal)", () => {
  it("adderar ?commit=true på en href utan query", () => {
    // Värdet är "true", inte "1" — ASP.NET bool-binding tar inte "1".
    expect(withCommitFlag("/jobb")).toBe(`/jobb?${COMMIT_PARAM}=${COMMIT_VALUE}`);
    expect(COMMIT_VALUE).toBe("true");
  });

  it("adderar &commit=true på en href som redan har query", () => {
    expect(withCommitFlag("/jobb?q=volvo")).toBe(
      `/jobb?q=volvo&${COMMIT_PARAM}=${COMMIT_VALUE}`,
    );
  });

  it("commit-flaggan ingår ALDRIG i buildJobbHref (utanför JobbUrlState)", () => {
    // Invariant (CTO VAL 5 väg 2): commit är en transient signal, inte ett
    // tillstånd — buildJobbHref emitterar den aldrig.
    expect(buildJobbHref({ ...empty, q: "volvo" })).toBe("/jobb?q=volvo");
    expect(buildJobbHref(empty)).toBe("/jobb");
  });
});

describe("buildJobbHref Klass 2 (employmentType + worktimeExtent)", () => {
  it("skriver employmentType som ETT param med värdena joinade", () => {
    expect(
      buildJobbHref({ ...empty, employmentType: ["et1", "et2"] }),
    ).toBe("/jobb?employmentType=et1.et2");
  });

  it("appendar worktimeExtent (radio → 0–1 element)", () => {
    expect(buildJobbHref({ ...empty, worktimeExtent: ["heltid"] })).toBe(
      "/jobb?worktimeExtent=heltid",
    );
  });

  it("ordning: dimensioner → employmentType → worktimeExtent → q", () => {
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        occupationGroup: ["og1"],
        region: ["r1"],
        employmentType: ["et1"],
        worktimeExtent: ["wt1"],
      }),
    ).toBe(
      "/jobb?occupationGroup=og1&region=r1&employmentType=et1&worktimeExtent=wt1&q=volvo",
    );
  });

  it("tomma Klass-2-arrayer ger inga params", () => {
    expect(buildJobbHref(empty)).toBe("/jobb");
  });
});

describe("buildJobbHref STEG 5 (matchGrades — grade-filter)", () => {
  it("skriver matchGrades som ETT param med enum-namnen joinade", () => {
    expect(
      buildJobbHref({ ...empty, matchGrades: ["Strong", "Good"] }),
    ).toBe("/jobb?matchGrades=Strong.Good");
  });

  it("tom matchGrades-lista ger inget param (Av = noll grader)", () => {
    expect(buildJobbHref({ ...empty, matchGrades: [] })).toBe("/jobb");
  });

  it("ordning: Klass-2-dimensioner → matchGrades → q (stabil URL-form)", () => {
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        occupationGroup: ["og1"],
        region: ["r1"],
        employmentType: ["et1"],
        worktimeExtent: ["wt1"],
        matchGrades: ["Basic", "Good", "Strong"],
      }),
    ).toBe(
      "/jobb?occupationGroup=og1&region=r1&employmentType=et1&worktimeExtent=wt1&matchGrades=Basic.Good.Strong&q=volvo",
    );
  });

  it("round-trip: buildJobbHref → parse bevarar grad-listan I ORDNING", () => {
    // Wire-kontraktets round-trip: graderna överlever serialisering→parse.
    // Läses nu via ETT param som splittas, inte via getAll — och ordningen
    // ingår i assertionen, för `sameList`/`sameUrlState` jämför element för
    // element, så en sorterande serialiserare hade gjort ett mängd-lika par
    // olika (CTO-bind 2026-08-01: sortera INTE).
    const href = buildJobbHref({
      ...empty,
      matchGrades: ["Strong", "Basic"],
    });
    const qs = href.slice(href.indexOf("?") + 1);
    const raw = new URLSearchParams(qs).getAll("matchGrades");
    expect(raw).toEqual(["Strong.Basic"]);
    expect(raw.flatMap((v) => v.split(JOBB_AXIS_SEPARATOR))).toEqual([
      "Strong",
      "Basic",
    ]);
  });
});

describe("buildJobbHref issue #292 (matchning huvudbrytare)", () => {
  it("matchningOff=true emitterar ?matchning=off", () => {
    expect(buildJobbHref({ ...empty, matchningOff: true })).toBe(
      "/jobb?matchning=off",
    );
  });

  it("matchningOff=false (PÅ) emitterar INTET param (default PÅ = frånvaro)", () => {
    expect(buildJobbHref({ ...empty, matchningOff: false })).toBe("/jobb");
  });

  it("matchningOff utelämnad (undefined) emitterar INTET param", () => {
    // `empty` saknar matchningOff helt → samma som PÅ (frånvaro).
    expect(buildJobbHref(empty)).toBe("/jobb");
  });

  it("ordning: matchGrades → matchning → q (stabil URL-form)", () => {
    // matchningOff är distinkt från matchGrades (CTO-bind: ingen off-sentinel i
    // matchGrades). Båda kan samexistera i URL:en endast i PÅ-läget — i av-läget
    // tömmer toolbaren matchGrades, men buildJobbHref serialiserar oavsett.
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        matchGrades: ["Strong"],
        matchningOff: true,
      }),
    ).toBe("/jobb?matchGrades=Strong&matchning=off&q=volvo");
  });
});

describe("buildJobbHref #300 PR-5 (relaterade — Visa relaterade också)", () => {
  it("includeRelated=true emitterar ?relaterade=on", () => {
    expect(buildJobbHref({ ...empty, includeRelated: true })).toBe(
      "/jobb?relaterade=on",
    );
  });

  it("includeRelated=false (AV) emitterar INTET param (default AV = frånvaro)", () => {
    expect(buildJobbHref({ ...empty, includeRelated: false })).toBe("/jobb");
  });

  it("includeRelated utelämnad (undefined) emitterar INTET param", () => {
    // `empty` saknar includeRelated helt → samma som AV (frånvaro, ren URL).
    expect(buildJobbHref(empty)).toBe("/jobb");
  });

  it("ordning: matchGrades → matchning → relaterade → q (stabil URL-form)", () => {
    // relaterade placeras intill matchnings-axelns övriga params (efter matchning,
    // före q) så delningsbara URL:er får stabil form.
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        matchGrades: ["Related", "Strong"],
        matchningOff: true,
        includeRelated: true,
      }),
    ).toBe(
      "/jobb?matchGrades=Related.Strong&matchning=off&relaterade=on&q=volvo",
    );
  });
});

describe("buildJobbHref #454 PR-0 (employer — arbetsgivar-filtret)", () => {
  it("employer emitterar ?employer=<orgnr> (singel-värde)", () => {
    expect(buildJobbHref({ ...empty, employer: ["5560125790"] })).toBe(
      "/jobb?employer=5560125790",
    );
  });

  it("employer utelämnad/undefined ger inget param (ren URL)", () => {
    expect(buildJobbHref(empty)).toBe("/jobb");
    expect(buildJobbHref({ ...empty, employer: [] })).toBe("/jobb");
  });

  it("ordning: Klass-2-dimensioner → employer → matchGrades → q (stabil URL-form)", () => {
    // employer placeras efter Klass-2-dimensionerna, före matchGrades —
    // param-bevarande-kontraktet: en yta som bygger URL:en får inte tappa den.
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        occupationGroup: ["og1"],
        employmentType: ["et1"],
        employer: ["5560125790"],
        matchGrades: ["Strong"],
        hideApplied: true,
      }),
    ).toBe(
      "/jobb?occupationGroup=og1&employmentType=et1&employer=5560125790&matchGrades=Strong&doljAnsokta=on&q=volvo",
    );
  });

  it("round-trip: buildJobbHref → URLSearchParams bevarar employer", () => {
    const href = buildJobbHref({ ...empty, employer: ["5560125790"] });
    const qs = href.slice(href.indexOf("?") + 1);
    expect(new URLSearchParams(qs).get("employer")).toBe("5560125790");
  });
});

describe("parseEmployerParam (#454 PR-0 — SPOT-gaten, delad page ↔ buildPageHref)", () => {
  it("accepterar exakt 10 siffror (trimmat)", () => {
    expect(parseEmployerParam("5560125790")).toEqual(["5560125790"]);
    expect(parseEmployerParam(" 5560125790 ")).toEqual(["5560125790"]);
  });

  it("droppar felformat tyst (drop-unknown — backend skulle annars 400:a)", () => {
    expect(parseEmployerParam("556012-5790")).toEqual([]); // bindestreck
    expect(parseEmployerParam("55601257")).toEqual([]); // för kort
    expect(parseEmployerParam("55601257901")).toEqual([]); // för långt
    expect(parseEmployerParam("556012579a")).toEqual([]); // icke-siffra
    expect(parseEmployerParam("55601\n25790")).toEqual([]); // inbäddad newline-injektion
    expect(parseEmployerParam("")).toEqual([]);
    expect(parseEmployerParam(undefined)).toEqual([]);
  });

  it("bär HELA axeln — både den joinade och den upprepade formen", () => {
    // The axis is a list since Oversikt links its sums straight to the ads of every watched
    // company at once. Backend has bound `string[]` all along (ADR 0087 D6).
    expect(parseEmployerParam("5560125790.5560360793")).toEqual([
      "5560125790",
      "5560360793",
    ]);
    expect(parseEmployerParam(["5560125790", "5560360793"])).toEqual([
      "5560125790",
      "5560360793",
    ]);
    expect(parseEmployerParam([])).toEqual([]);
  });

  it("droppar ett felformat värde men behåller resten (drop-unknown per värde)", () => {
    // Not "the first value decides": a manipulated URL must not be able to suppress a legal
    // filter by prefixing junk, and it must not 400 the list query either.
    expect(parseEmployerParam(["nonsens", "5560360793"])).toEqual(["5560360793"]);
    expect(parseEmployerParam("nonsens.5560360793")).toEqual(["5560360793"]);
  });

  it("dedupar och bevarar ordningen", () => {
    // Order is load-bearing: `sameList` compares element-wise and `sameUrlState` is the hero
    // mirror field's own-roundtrip detector, so a set-equal pair in another order would
    // compare unequal and re-serialise the field forever.
    expect(parseEmployerParam("5560360793.5560125790.5560360793")).toEqual([
      "5560360793",
      "5560125790",
    ]);
  });
});

describe("buildJobbHref #383 → förenklat (Dölj ansökta)", () => {
  it("hideApplied=true emitterar ?doljAnsokta=on", () => {
    expect(buildJobbHref({ ...empty, hideApplied: true })).toBe(
      "/jobb?doljAnsokta=on",
    );
  });

  it("hideApplied falsk/utelämnad ger inget param (ren URL)", () => {
    expect(buildJobbHref(empty)).toBe("/jobb");
    expect(buildJobbHref({ ...empty, hideApplied: false })).toBe("/jobb");
  });

  it("ordning: relaterade → dölj ansökta → q (stabil URL-form)", () => {
    // "Dölj ansökta" placeras efter matchnings-axelns params, före q.
    expect(
      buildJobbHref({
        ...empty,
        q: "volvo",
        includeRelated: true,
        hideApplied: true,
      }),
    ).toBe("/jobb?relaterade=on&doljAnsokta=on&q=volvo");
  });
});

describe("parseQParam (#823 klampen + #847 arity — SPOT-parsern, delad page ↔ buildPageHref)", () => {
  // SPOT-parsern som BÅDA URL-vägarna på /jobb måste dela: page.tsx vid entry och
  // buildPageHref när den bygger pagineringslänkar. Divergerade de re-emitterade
  // sidlänkarna ett q som sidan självt ignorerar — en URL som påstår ett sök som inte körs.
  it("droppar en söktext under backendens minimum", () => {
    expect(parseQParam("a")).toBeUndefined();
    expect(parseQParam(" a ")).toBeUndefined();
  });

  it("normaliserar (trimmar) så båda URL-vägarna emitterar samma q", () => {
    // Utan detta kör sidan "ab" medan pagineringslänken bär "+ab+".
    expect(parseQParam(" ab ")).toBe("ab");
  });

  it("behåller allt som backend faktiskt accepterar", () => {
    expect(parseQParam("ab")).toBe("ab");
    // Regeln gäller HELA strängen, aldrig per ord — "a bc" är 4 tecken och giltigt.
    expect(parseQParam("a bc")).toBe("a bc");
    expect(parseQParam("backend")).toBe("backend");
  });

  it("lämnar frånvaro av söktext orörd", () => {
    expect(parseQParam(undefined)).toBeUndefined();
  });

  it("#847: upprepad param (string[]) → första värdet, ingen krasch", () => {
    // FÖRE fixen: `q.trim is not a function` (mätt) → teknisk-fel-kortet på
    // /jobb?q=a&q=b. `q` är enkelvärt (EN söktext), så arity-koerceringen är
    // första-värdet — samma som parseEmployerParam, INTE toStringList.
    expect(parseQParam(["backend", "frontend"])).toBe("backend");
    expect(parseQParam(["ab"])).toBe("ab");
  });

  it("#847: klampen gäller det koercerade värdet, inte det råa", () => {
    // Första värdet är under minimum ⇒ ingen söktext. Att i stället plocka
    // element 1 hade varit en gissning om avsikt (se parseQParam-doccen).
    expect(parseQParam(["a", "backend"])).toBeUndefined();
    expect(parseQParam(["", "backend"])).toBeUndefined();
    expect(parseQParam([" ab ", "frontend"])).toBe("ab");
  });

  it("#847: tom array = frånvaro av söktext", () => {
    expect(parseQParam([])).toBeUndefined();
  });
});

/**
 * #846 — flyttad hit från `components/job-ads/jobb-results-page-href.test.ts`
 * tillsammans med `buildPageHref`. I sin gamla hemvist importerade testet från
 * en Server-Component-modul, vilket drog hela dess graf in i jsdom och bara
 * fungerade tack vare att `vitest.config.ts` aliasar `server-only` till en shim.
 *
 * Q-klamps-fallen är #823:s ursprungliga vakt, ordagrant. Param-bevarande-fallet
 * är NYTT: varje rad i buildPageHref bär en kommentar om att den finns för att
 * ett sida-2-klick annars tappar sitt filter (E2b/Klass 2/STEG 5/#454), men
 * ingen av dem var testad — och e2e:n når dem aldrig, eftersom en tom annons-DB
 * inte renderar någon paginering alls.
 */
describe("buildPageHref (#823 q-klampen, #846 hemvisten)", () => {
  // Annotering, inte `as`: en assertion hade stängt av kontrollen permanent — läggs ett
  // required-fält till fortsätter filen kompilera medan buildern tar en annan gren.
  const params: JobbRawSearchParams = {};

  it("droppar ett q under backendens minimum ur sidlänken", () => {
    const href = buildPageHref({ ...params, q: "a" }, 2, 20);
    expect(href).not.toContain("q=");
    expect(href).toContain("page=2");
  });

  it("behåller ett giltigt q — och normaliserar det som page.tsx gör", () => {
    expect(buildPageHref({ ...params, q: "backend" }, 2, 20)).toContain(
      "q=backend"
    );
    // Trimmad paritet: annars kör sidan "ab" medan länken bär "+ab+".
    expect(buildPageHref({ ...params, q: " ab " }, 2, 20)).toContain("q=ab");
  });

  it("#847: ett upprepat ?q= kraschar inte länkbyggaren", () => {
    // MÄTT före fixen: `TypeError: q.trim is not a function` — buildPageHref var det
    // andra q-konsumerande stället (page.tsx kraschade först i praktiken, men båda
    // vägarna bar defekten, vilket är varför koerceringen bor i den delade parsern).
    const href = buildPageHref({ ...params, q: ["backend", "frontend"] }, 2, 20);
    expect(href).toContain("q=backend");
    expect(href).not.toContain("frontend");
  });

  it("#847: ett upprepat q vars första värde är under minimum droppas", () => {
    const href = buildPageHref({ ...params, q: ["a", "backend"] }, 2, 20);
    expect(href).not.toContain("q=");
    expect(href).toContain("page=2");
  });

  // NAMNET beskriver vad testet TÄCKER, inte hur många fält typen har (den bär
  // fjorton; buildPageHref läser tretton — aldrig `page`, som `targetPage` ersätter;
  // `pageSize` har sitt eget test nedan). Det är INTE en
  // fullständighetsgaranti för /jobb: `matchning` läses av page.tsx men saknas i
  // `JobbRawSearchParams`, så `?matchning=off` tappas vid ett sida-2-klick
  // (pre-existerande — se ⚠-noten på typen). Ett test som hette "varje
  // dimension" hade lovat mer än det asserterar, vilket är precis den
  // test-fiktion CLAUDE.md §7 förbjuder.
  it("bär vidare varje param den läser utom page, som targetPage ersätter", () => {
    const href = buildPageHref(
      {
        occupationGroup: ["2512"],
        region: ["01"],
        municipality: ["0180", "0181"],
        employmentType: ["1"],
        worktimeExtent: ["2"],
        matchGrades: ["Strong", "Good"],
        relaterade: "on",
        doljAnsokta: "on",
        baraMatchade: "on",
        employer: ["5565021000"],
        q: "backend",
        sortBy: "Relevance",
      },
      3,
      20
    );
    expect(href).toContain("page=3");
    expect(href).toContain("occupationGroup=2512");
    expect(href).toContain("region=01");
    expect(href).toContain("municipality=0180.0181");
    expect(href).toContain("employmentType=1");
    expect(href).toContain("worktimeExtent=2");
    expect(href).toContain("matchGrades=Strong.Good");
    expect(href).toContain("relaterade=on");
    expect(href).toContain("doljAnsokta=on");
    expect(href).toContain("baraMatchade=on");
    expect(href).toContain("employer=5565021000");
    expect(href).toContain("q=backend");
    expect(href).toContain("sortBy=Relevance");
    // Exakt form utöver närvaro-assertionerna: en `toContain` kan bara fånga att
    // något SAKNAS, aldrig att något oavsett lagts TILL. Denna pinnar också
    // param-ordningen, som är kontrakt för delningsbara URL:er.
    expect(href).toBe(
      "/jobb?page=3&sortBy=Relevance&occupationGroup=2512&region=01" +
        "&municipality=0180.0181&employmentType=1&worktimeExtent=2" +
        "&matchGrades=Strong.Good&relaterade=on&doljAnsokta=on" +
        "&baraMatchade=on&employer=5565021000&q=backend"
    );
  });

  it("utelämnar default-sorten och sida 1 så delningsbara URL:er förblir rena", () => {
    // Samma default-utelämning som buildJobbHref — och `page` sätts aldrig för sida 1.
    expect(buildPageHref({ sortBy: "PublishedAtDesc" }, 1, 20)).toBe("/jobb");
  });

  it("skriver bara ut pageSize när den avviker från sidans default", () => {
    expect(buildPageHref({ pageSize: "20" }, 2, 20)).not.toContain("pageSize");
    expect(buildPageHref({ pageSize: "50" }, 2, 20)).toContain("pageSize=50");
  });
});

describe("URL axis serialisation (2026-08-01 — the router-cache collision)", () => {
  /**
   * The property the whole change exists for. Next's client router cache
   * collapses REPEATED query keys to the last value, so under the old contract
   * two DIFFERENT applied states hashed to ONE cache entry and the navigation
   * between them was served from cache: no fetch, no re-render, controls
   * disagreeing with the URL. Measured on the running surface before the fix.
   */
  function collapse(search: string): string {
    const out = new Map<string, string>();
    for (const [k, v] of new URLSearchParams(search)) out.set(k, v);
    return [...out].map(([k, v]) => `${k}=${v}`).join("&");
  }

  it("two applied states that COLLIDED under the repeated form now have different cache keys", () => {
    // The exact transition measured as broken: three employment types, remove
    // the one that is not last. Under the repeated form both sides collapsed to
    // `employmentType=<last>`; under the joined form they cannot.
    const before = buildJobbHref({ ...empty, employmentType: ["a", "b", "c"] });
    const after = buildJobbHref({ ...empty, employmentType: ["b", "c"] });
    const q = (href: string) => href.slice(href.indexOf("?"));

    expect(collapse(q(before))).not.toBe(collapse(q(after)));

    // ...and the counterfactual, so this test cannot pass for the wrong reason:
    // the SAME pair written the old way DOES collide. Without this the assertion
    // above is satisfied by almost any encoding and would stay green if the
    // collision came back by another route.
    expect(collapse("?employmentType=a&employmentType=b&employmentType=c")).toBe(
      collapse("?employmentType=b&employmentType=c"),
    );
  });

  it("keeps order — it does NOT sort, because sameUrlState compares element-wise", () => {
    expect(serializeJobbAxis(["c", "a", "b"])).toBe("c.a.b");
  });

  it("drops an EMPTY value so the joined form never carries a trailing separator", () => {
    // A trailing `.` is the classic way a pasted link breaks: auto-linkers in
    // Slack, Outlook and most clients read a terminal period as sentence
    // punctuation and chop it, handing the recipient a silently truncated URL
    // (design-reviewer, #1144). Not reachable today, but neither is the
    // separator case the adjacent filter guards.
    expect(serializeJobbAxis(["a", ""])).toBe("a");
    expect(serializeJobbAxis(["", "b"])).toBe("b");
    expect(serializeJobbAxis(["a", "", "b"])).toBe("a.b");
    expect(buildJobbHref({ ...empty, region: ["r1", ""] })).toBe("/jobb?region=r1");
  });

  it("drops a value containing the separator rather than emitting an ambiguous one", () => {
    // Defence in depth behind the corpus test: such a value would parse back as
    // two, silently widening the filter with the chip still showing. Dropping
    // follows the route's drop-unknown discipline; it must never throw, because
    // this runs inside a Server Component render.
    expect(serializeJobbAxis(["ok1", `bad${JOBB_AXIS_SEPARATOR}value`, "ok2"])).toBe(
      "ok1.ok2",
    );
    expect(() => serializeJobbAxis([`x${JOBB_AXIS_SEPARATOR}y`])).not.toThrow();
  });

  it("omits an axis whose every value was dropped, instead of writing an empty param", () => {
    expect(buildJobbHref({ ...empty, region: [`a${JOBB_AXIS_SEPARATOR}b`] })).toBe(
      "/jobb",
    );
  });

  describe("back-compat: a link shared before the change still works", () => {
    it("buildPageHref reads the REPEATED form and re-emits it joined (self-healing)", () => {
      const href = buildPageHref(
        { municipality: ["0180", "0181"], sortBy: "PublishedAtDesc" },
        2,
        20,
      );
      expect(href).toContain("municipality=0180.0181");
      expect(new URLSearchParams(href.slice(href.indexOf("?") + 1)).getAll("municipality"))
        .toEqual(["0180.0181"]);
    });

    it("buildPageHref reads the JOINED form identically", () => {
      const fromJoined = buildPageHref(
        { municipality: ["0180.0181"], sortBy: "PublishedAtDesc" },
        2,
        20,
      );
      const fromRepeated = buildPageHref(
        { municipality: ["0180", "0181"], sortBy: "PublishedAtDesc" },
        2,
        20,
      );
      // A reader cannot tell which form produced the state — that is what makes
      // the migration free: no redirect, no rewrite of existing links.
      expect(fromJoined).toBe(fromRepeated);
    });

    it("a mixed arrival (both forms at once) parses to the union", () => {
      const href = buildPageHref(
        { municipality: ["0180.0181", "0182"], sortBy: "PublishedAtDesc" },
        2,
        20,
      );
      expect(href).toContain("municipality=0180.0181.0182");
    });
  });
});

describe("the separator against the grammar the backend enforces", () => {
  /**
   * `SearchCriteria.ConceptIdPattern` (`src/Jobbliggaren.Domain/SavedSearches/
   * SearchCriteria.cs`) is `^[A-Za-z0-9_-]{1,32}\z`, applied by every query
   * validator. Joining an axis is only unambiguous while the separator lies
   * OUTSIDE that charset.
   *
   * This is the half the frontend owns, and it cannot move to the .NET side: a
   * backend corpus test cannot catch a bad separator CHOICE, because `-` is
   * legal in the pattern — switching `JOBB_AXIS_SEPARATOR` to `-` would leave
   * every backend guard green while breaking every shared /jobb link. The
   * corpus-obeys-the-charset half lives in `TaxonomyConceptIdGrammarTests`
   * (`Jobbliggaren.Application.UnitTests`), which asserts the shipped corpus
   * through the query validator a /jobb search actually hits.
   */
  const CONCEPT_ID_CHARSET = /^[A-Za-z0-9_-]$/;

  it("the separator is a character no legal conceptId can contain", () => {
    expect(CONCEPT_ID_CHARSET.test(JOBB_AXIS_SEPARATOR)).toBe(false);
  });

  it("counterfactual: the sibling surface's separator WOULD be ambiguous here", () => {
    // Not hypothetical. `/foretag/sok` joins on `-` safely because SCB codes are
    // digits only; here `-` is inside the conceptId charset, so reusing it would
    // split real ids. This is why the two surfaces differ.
    expect(CONCEPT_ID_CHARSET.test("-")).toBe(true);
  });
});

describe("#551 punkt 4 — Distans som ort-axel i URL:en", () => {
  const params: JobbRawSearchParams = {};

  it("remote=true emitterar ?distans=on — svenskt namn, sentinel-värde", () => {
    expect(buildJobbHref({ ...empty, remote: true })).toBe("/jobb?distans=on");
  });

  it("remote=false emitterar INTET param (default AV = frånvaro, ren URL)", () => {
    expect(buildJobbHref({ ...empty, remote: false })).toBe("/jobb");
  });

  it("remote utelämnad är byte-identisk med remote=false", () => {
    expect(buildJobbHref(empty)).toBe(buildJobbHref({ ...empty, remote: false }));
  });

  // Distans BREDDAR ort-dimensionen (backend unionerar kommun ∨ län ∨ remote),
  // den ersätter den inte — så de två id-axlarna måste överleva i samma URL.
  it("distans reser BREDVID kommun/län, aldrig i stället för dem", () => {
    const href = buildJobbHref({
      ...empty,
      region: ["CifL_Rzy_Mku"],
      municipality: ["AvNB_uwa_6n6"],
      remote: true,
    });
    expect(href).toContain("region=CifL_Rzy_Mku");
    expect(href).toContain("municipality=AvNB_uwa_6n6");
    expect(href).toContain("distans=on");
  });

  // DEN här är fällan filen själv dokumenterar för varje boolesk axel före den:
  // utan bevarandet i buildPageHref tappar sida-2-klicket facetten TYST, och
  // användaren får sida 2 av ett annat filter än det hon står i.
  it("sida-2-länken BEVARAR distans (samma felklass som relaterade/baraMatchade)", () => {
    expect(buildPageHref({ ...params, distans: "on" }, 2, 20)).toContain(
      "distans=on",
    );
  });

  it("sida-2-länken bär INTE distans när facetten är av", () => {
    expect(buildPageHref(params, 2, 20)).not.toContain("distans");
  });

  // Endast on-värdet parsas — samma drop-unknown-disciplin som page-validatorn,
  // så en handredigerad URL aldrig smyger in ett annat sanningsvärde.
  it("ett annat värde än 'on' bevaras INTE (drop-unknown)", () => {
    expect(buildPageHref({ ...params, distans: "true" }, 2, 20)).not.toContain(
      "distans",
    );
    expect(buildPageHref({ ...params, distans: "1" }, 2, 20)).not.toContain(
      "distans",
    );
  });
});
