import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CompanyBrowseList } from "./company-browse-list";
import type { CompanyBrowse, CriterionReference } from "@/lib/dto/company-criteria";

// CompanyFollowButton pulls in the server-action module; mock it so the client island renders in jsdom.
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: vi.fn(),
  unfollowCompanyAction: vi.fn(),
}));

const REFERENCE: CriterionReference = {
  sniVersion: "2025",
  kommunVersion: "2025",
  sni: [],
  lan: [],
};

const LEGAL_ORGNR = "5592804784";

const LEGAL: CompanyBrowse = {
  organizationNumber: LEGAL_ORGNR,
  isProtectedIdentity: false,
  name: "Acme Bygg AB",
  seatMunicipalityCode: "0180",
  seatMunicipalityName: "Stockholm",
  sniCodes: [],
};

/**
 * The shape `CompanyBrowseDto.FromRow` emits for a personnummer-shaped org.nr (ADR 0087 D8(c)): the
 * number is dropped and the flag is set, never both. The dev register holds zero such rows — ADR
 * 0091 keeps sole traders out at ingest — but the mask exists for paths that do not exist yet, so a
 * fixture is the honest way to reach it.
 */
const PROTECTED: CompanyBrowse = {
  organizationNumber: null,
  isProtectedIdentity: true,
  name: "Skyddad Firma",
  seatMunicipalityCode: "0180",
  seatMunicipalityName: "Stockholm",
  sniCodes: [],
};

describe("CompanyBrowseList — #560 PR-C follow-column gate", () => {
  it("renders no follow column when followStateByOrgNr is omitted (bevakningar/[id] parity)", () => {
    render(<CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />);

    expect(
      screen.queryByRole("columnheader", { name: "Bevaka" })
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("renders a follow button per non-masked row when followStateByOrgNr is provided", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />
    );

    expect(
      screen.getByRole("columnheader", { name: "Bevaka" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Bevaka Acme Bygg AB" })
    ).toBeInTheDocument();
  });

  it("reflects an existing follow as 'Bevakar'", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, "cw-1"]]);
    render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />
    );

    expect(
      screen.getByRole("button", { name: "Bevakar Acme Bygg AB" })
    ).toBeInTheDocument();
  });

  it("never renders a follow button for a masked/sole-prop row (no org.nr key → not followable)", () => {
    render(
      <CompanyBrowseList
        items={[PROTECTED]}
        reference={REFERENCE}
        followStateByOrgNr={new Map()}
      />
    );

    // The column exists, but the protected row carries no follow affordance (ADR 0087 D8(c)) — no button,
    // and a screen-reader-only "Kan inte bevakas" in place of it.
    expect(
      screen.getByRole("columnheader", { name: "Bevaka" })
    ).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByText("Kan inte bevakas")).toBeInTheDocument();
  });
});

/**
 * The accessible name is CALLER-SPECIFIC, and that is the whole point of the prop.
 *
 * The defaults ("Företag som matchar din bevakning") belong to the criterion browse. Rendered on
 * `/foretag/sok`, which answers a SEARCH, they are simply false — and they were false there for as
 * long as both surfaces shared this component, because nothing sighted showed it.
 */
describe("CompanyBrowseList — the table's accessible name", () => {
  it("defaults to the criterion-browse wording when no labels are given", () => {
    render(<CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />);
    expect(
      screen.getByRole("table", { name: "Företag som matchar bevakningen" }),
    ).toBeInTheDocument();
  });

  it("uses the caller's wording when given, for BOTH the name and the caption", () => {
    render(
      <CompanyBrowseList
        items={[LEGAL]}
        reference={REFERENCE}
        labels={{ tableAria: "Företag som matchar sökningen", tableCaption: "Sökträffar." }}
      />,
    );
    const table = screen.getByRole("table", { name: "Företag som matchar sökningen" });
    expect(table).toBeInTheDocument();
    // The caption is sr-only, so it is asserted through the accessible description rather than sight.
    expect(table.querySelector("caption")).toHaveTextContent("Sökträffar.");
    // ...and the default must be GONE, not merely joined by the override.
    expect(
      screen.queryByRole("table", { name: "Företag som matchar bevakningen" }),
    ).not.toBeInTheDocument();
  });
});

/**
 * Under `table-layout: fixed` the `<colgroup>` IS the column geometry, so it has to stay in lockstep
 * with the header row: one `<col>` too few and every column after the gap silently takes the wrong
 * width — on /foretag/sok that means the org.nr answer and the browse below it stop lining up, which
 * is the defect this geometry exists to fix.
 *
 * jsdom computes no layout, so the widths themselves are verified rendered (Chromium, see the PR).
 * What jsdom CAN pin is the structural contract, and that is the half a future column addition
 * breaks: the follow column is conditional, so the two must agree in BOTH shapes.
 */
/** The `<col>` classes in document order — the geometry contract, not merely its arity. */
function colClasses(container: HTMLElement): string[] {
  return [...container.querySelectorAll("colgroup > col")].map((c) => c.className);
}

describe("CompanyBrowseList — the declared column geometry", () => {
  it("declares the five columns in order, matching the header row", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    const { container } = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />,
    );

    // ORDER, not just count. Swapping two <col>s keeps every count identical and silently swaps two
    // column widths, so counting alone would call that geometry correct.
    expect(colClasses(container)).toEqual([
      "jp-companyBrowse__col--name",
      "jp-companyBrowse__col--orgnr",
      "jp-companyBrowse__col--seat",
      "jp-companyBrowse__col--sni",
      "jp-companyBrowse__col--follow",
    ]);
    // The header row IN ORDER too, and scoped to the first <tr>. Pinning the colgroup's order while
    // counting the headers is the same arity gap one row lower: swapping two <th>s labels a 145px
    // column "Branscher" and a 280px column "Säteskommun" with every count in this file still 5.
    expect(
      [...container.querySelectorAll("thead tr:first-child > th")].map((th) => th.textContent),
    ).toEqual(["Företag", "Org.nr", "Säteskommun", "Branscher", "Bevaka"]);
    expect(container.querySelectorAll("tbody tr:first-child > td")).toHaveLength(5);
  });

  it("drops the follow column and its <col> together (bevakningar/[id] parity)", () => {
    const { container } = render(<CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />);

    expect(colClasses(container)).toEqual([
      "jp-companyBrowse__col--name",
      "jp-companyBrowse__col--orgnr",
      "jp-companyBrowse__col--seat",
      "jp-companyBrowse__col--sni",
    ]);
    expect(
      [...container.querySelectorAll("thead tr:first-child > th")].map((th) => th.textContent),
    ).toEqual(["Företag", "Org.nr", "Säteskommun", "Branscher"]);
    expect(container.querySelectorAll("tbody tr:first-child > td")).toHaveLength(4);
  });

  /**
   * The colgroup is inert without the class that turns fixed layout on, and the CSS guard cannot
   * catch its removal: once production stops naming `jp-companyBrowse--withFollow`, the only
   * remaining reference is this test file, and the guard downgrades a test-only reference to
   * advisory rather than failing (`guard-css.mjs` — it cannot tell a positive assertion from a
   * negative one). So the classes are pinned here, in BOTH polarities, or they are pinned nowhere.
   */
  it("carries the fixed-layout class in both shapes, and the wider floor only with the follow column", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    const withFollow = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} followStateByOrgNr={map} />,
    ).container.querySelector("table");
    // `jp-table` too: this PR rewrote the className expression, and dropping the ledger styling
    // leaves guard:css green (the element still resolves via jp-companyBrowse) and every other
    // assertion here green.
    expect(withFollow).toHaveClass("jp-table", "jp-companyBrowse", "jp-companyBrowse--withFollow");

    const withoutFollow = render(
      <CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />,
    ).container.querySelector("table");
    expect(withoutFollow).toHaveClass("jp-table", "jp-companyBrowse");
    expect(withoutFollow).not.toHaveClass("jp-companyBrowse--withFollow");
  });

  /**
   * The minimum width this geometry declares — 820px without the follow column, 980px with it — is
   * wider than a 768px viewport's content box, and for two of the three surfaces that is NEW.
   * Measured on origin/main at 768 in a 704px container: the org.nr answer fit exactly at 704px and
   * the criterion browse fit exactly at 704px, while the streamed results table already scrolled at
   * 827px — which is the very disagreement this PR exists to end. So for those two this wrapper is
   * now the only thing between a declared minimum and a horizontally scrolling PAGE, which is the
   * containment the change was accepted on. `guard:css` sweeps `jp-*` names and cannot see a
   * Tailwind utility leave.
   */
  it("keeps the table inside its own scroll container", () => {
    const { container } = render(<CompanyBrowseList items={[LEGAL]} reference={REFERENCE} />);
    const table = container.querySelector("table");
    if (!table) throw new Error("no <table> rendered");
    // Anchored to the table's own parent, not to a position in the container.
    expect(table.parentElement).toHaveClass("overflow-x-auto");
  });

  /**
   * Fixed layout removes the escape valve auto layout provided: a column can no longer grow to fit
   * its content, so whether a cell WRAPS is now load-bearing. Every `whitespace` decision below was
   * forced by the geometry, and two of them are reverts waiting to happen — `whitespace-nowrap`
   * stood on the seat and follow cells before this change, so re-adding it reads as a cleanup.
   */
  it("wraps every cell that fixed layout can no longer widen", () => {
    const map = new Map<string, string | null>([[LEGAL_ORGNR, null]]);
    const { container } = render(
      // An SNI code, so the Branscher cell this test makes a wrap claim about actually holds text.
      // The reference snapshot carries no names, so `resolveSniNames` falls back to the raw code.
      <CompanyBrowseList
        items={[{ ...LEGAL, sniCodes: ["41200"] }]}
        reference={REFERENCE}
        followStateByOrgNr={map}
      />,
    );
    const all = [...container.querySelectorAll("tbody tr:first-child > td")];
    // Fail closed on a missing cell rather than letting `undefined` satisfy a negated assertion.
    const cell = (i: number): HTMLElement => {
      const td = all[i];
      if (!td) throw new Error(`no <td> at index ${i} — the row has ${all.length}`);
      return td as HTMLElement;
    };

    // Content first: cell(0) and cell(2) carry BYTE-IDENTICAL class attributes
    // ("wrap-break-word text-text-primary"), so they are the one pair no class assertion can tell
    // apart — swapping the two <td>s alone left every other assertion in this file green (measured).
    // In production that puts a 42-character company name in the 145px column.
    expect(cell(0)).toHaveTextContent("Acme Bygg AB");
    // A 42-character company name overflows into Org.nr at the table's minimum width without this.
    expect(cell(0)).toHaveClass("wrap-break-word");
    // The org.nr NUMBER may not break; the cell may, so the "Skyddad identitet" badge can wrap
    // instead of overflowing into Säteskommun.
    expect(cell(1)).not.toHaveClass("whitespace-nowrap");
    expect(cell(1).querySelector(".whitespace-nowrap")).toHaveTextContent("559280-4784");
    // "Ej svensk hemortskommun" (23 837 rows) wraps rather than painting across Branscher.
    expect(cell(2)).toHaveTextContent("Stockholm");
    expect(cell(2)).not.toHaveClass("whitespace-nowrap");
    expect(cell(2)).toHaveClass("wrap-break-word");
    // Anchor by content before negating anything about it, same as cell(1) and cell(4).
    expect(cell(3)).toHaveTextContent("41200");
    expect(cell(3)).not.toHaveClass("whitespace-nowrap");
    // Index is a POSITION, not an identity: swapping the last two <td>s alone leaves this negation
    // passing on the Branscher cell. Anchor it by content before negating anything about it.
    expect(cell(4)).toContainElement(screen.getByRole("button", { name: "Bevaka Acme Bygg AB" }));
    // The follow cell also holds the failed-follow error, which must wrap — see CompanyFollowButton.
    expect(cell(4)).not.toHaveClass("whitespace-nowrap");

    // `white-space` INHERITS — the production comment on the follow cell says so — so "this cell does
    // not carry nowrap" is only half the claim. A class on the row, the tbody or the table reaches
    // every cell below it and is invisible to every negation above; the badge test closes the
    // DESCENDANT direction and left this one open. jsdom loads no Tailwind (computed white-space
    // reads ""), so the ancestor chain is asserted as SHAPE, not as computed style.
    const ancestors: HTMLElement[] = [];
    for (let el = cell(0).parentElement; el && el !== container; el = el.parentElement) {
      ancestors.push(el);
    }
    // Fail closed: an empty chain would satisfy every negation below.
    expect(ancestors.map((el) => el.tagName)).toEqual(["TR", "TBODY", "TABLE", "DIV"]);
    for (const el of ancestors) expect(el).not.toHaveClass("whitespace-nowrap");
  });

  /**
   * The badge is the REASON nowrap left the org.nr cell, so it is the half that has to be pinned.
   * "Skyddad identitet" sets `font-sans` inside a `font-mono` cell and clears 175px by 14px — a font
   * fallback eats that, and under fixed layout the cell then overflows into Säteskommun instead of
   * growing. Asserting only that the NUMBER carries nowrap leaves the badge free to take the class
   * back, which every other assertion in this file survives (measured, not argued).
   */
  it("leaves the protected-identity badge wrappable — the reason nowrap left the cell", () => {
    const { container } = render(
      <CompanyBrowseList items={[PROTECTED]} reference={REFERENCE} followStateByOrgNr={new Map()} />,
    );
    const orgNrCell = container.querySelectorAll("tbody tr:first-child > td")[1];
    if (!orgNrCell) throw new Error("no org.nr cell — the row rendered fewer than two <td>s");

    // Anchor the cell by its content BEFORE negating anything about that content.
    expect(orgNrCell).toContainElement(screen.getByText("Skyddad identitet"));
    expect(orgNrCell).not.toHaveClass("whitespace-nowrap");
    // The load-bearing one: nothing BETWEEN the cell and the badge may forbid the break either.
    expect(orgNrCell.querySelectorAll(".whitespace-nowrap")).toHaveLength(0);
  });
});

/**
 * `table-layout: fixed` is the whole mechanism, and no gate in this repo covers its removal. jsdom
 * applies no CSS, so a layout assertion is impossible; `guard:css` reports an element only when NO
 * `jp-*` name on it resolves, and this table also carries `jp-table`, which always will. Deleting
 * the rule therefore leaves vitest, guard:css, tsc and eslint all green while the two tables on
 * /foretag/sok go back to disagreeing about every column start. So it is pinned as TEXT, which is
 * the only thing that can see it. Precedent for reading a repo file from a test:
 * `src/i18n/client-namespace-payload.test.ts`.
 *
 * KNOWN LIMIT, stated rather than implied: text assertions prove a declaration EXISTS, not that it
 * WINS. A later, more specific rule setting `table-layout: auto` would pass every check here. The
 * source-order test below covers the one cascade case this geometry actually depends on (two rules
 * of equal specificity); a specificity override is out of reach for this instrument.
 */
describe("CompanyBrowseList — the stylesheet still declares the geometry", () => {
  // Same resolution form as `src/i18n/client-namespace-payload.test.ts`: a `new URL(rel, base)`
  // against `import.meta.url` is not a file: URL under this vitest config and throws.
  //
  // Comments are stripped FIRST, so nothing below can be satisfied by prose ABOUT the geometry —
  // this stylesheet's comments name every selector and quote every width it declares.
  const css = readFileSync(
    resolve(dirname(fileURLToPath(import.meta.url)), "../../app/globals.css"),
    "utf8",
  ).replace(/\/\*[\s\S]*?\*\//g, " ");

  const BASE_MIN_WIDTH = /\.jp-companyBrowse\s*\{[^}]*min-width:\s*calc\(([^)]*)\)/;
  const FOLLOW_MIN_WIDTH = /\.jp-companyBrowse--withFollow\s*\{[^}]*min-width:\s*calc\(([^)]*)\)/;

  /**
   * The one number in this geometry resting on no measurement (CTO bind 2026-07-28), so it is the
   * one number this file STATES rather than derives: changing it is a product decision and should
   * have to be said out loud here too.
   */
  const NAME_FLOOR_PX = 220;

  /** Every width the `__col--*` rules declare, in stylesheet order. */
  function declaredColumns(): { name: string; px: number }[] {
    const cols: { name: string; px: number }[] = [];
    for (const m of css.matchAll(/\.jp-companyBrowse__col--([A-Za-z]+)\s*\{([^}]*)\}/g)) {
      const name = m[1];
      const body = m[2];
      if (name === undefined || body === undefined) continue;
      const px = /(?:^|[;\s])width:\s*(\d+)px/.exec(body)?.[1];
      if (px === undefined) continue;
      cols.push({ name, px: Number(px) });
    }
    return cols;
  }

  /**
   * The `+`-separated px operands of a rule's `min-width: calc(...)`. THROWS rather than returning
   * an empty list: a deleted or rewritten min-width must not be able to satisfy a comparison below.
   */
  function minWidthOperands(rule: RegExp): number[] {
    const m = rule.exec(css);
    if (!m) throw new Error(`no \`min-width: calc(...)\` matched ${rule}`);
    // Every token must BE a px literal. The looser `Number(...)` form did not throw on an empty
    // operand — `"".split("+")` is `[""]`, and `Number("")` is a finite 0 — so `calc()` returned
    // `[0]` where the docblock promised a throw. No assertion passed vacuously on it (both compare
    // against 4 and 5 elements), but the guard did not do what it said.
    const tokens = (m[1] ?? "").split("+").map((t) => t.trim());
    if (!tokens.every((t) => /^\d+px$/.test(t))) {
      throw new Error(`min-width: calc(${m[1]}) is not a sum of px literals`);
    }
    return tokens.map((t) => Number(t.replace(/px$/, "")));
  }

  it("declares fixed layout for the class the table carries, and for no other", () => {
    expect(css).toMatch(/\.jp-companyBrowse\s*\{[^}]*table-layout:\s*fixed/);
    // The scoping is half the decision: seven other `.jp-table` consumers keep auto layout, sized by
    // their own rows on purpose. Every `table-layout` declaration is read by SELECTOR rather than
    // matching one hand-picked rule shape — a `.jp-table { table-layout: … }` negation pins exactly
    // one hoist form and leaves three open, all measured green: a GROUPED selector
    // (`.jp-table, .jp-companyBrowse { … }`), a space before the colon, and an uppercase property.
    // A count would be wrong instead: `.jp-apptable` declares fixed layout and always has.
    const layoutSelectors = [...css.matchAll(/([^{}]*)\{[^}]*table-layout\s*:/gi)].map((m) =>
      (m[1] ?? "").trim(),
    );
    // Fail closed: zero declarations would satisfy the loop below vacuously.
    expect(layoutSelectors.length).toBeGreaterThan(0);
    for (const selector of layoutSelectors) {
      expect(selector.split(",").map((s) => s.trim())).not.toContain(".jp-table");
    }
  });

  it("gives every column a declared width except the one that absorbs the remainder", () => {
    // Fail closed: an empty match set would make every sum below `0 + 220` and satisfy nothing.
    expect(declaredColumns().map((c) => c.name)).toEqual(["orgnr", "seat", "sni", "follow"]);
    // The name column is width-less ON PURPOSE — it takes the remainder, which is what makes the
    // declared widths sum exactly at every table width. A width here ends that silently.
    expect(css).not.toMatch(/\.jp-companyBrowse__col--name\s*\{[^}]*width:/);
  });

  it("keeps both minimum widths equal to the columns they claim to sum", () => {
    // DERIVED from the stylesheet, not restated against it. The `calc()` is a hand-typed copy of
    // four numbers that live in four other rules, and nothing makes the two agree on its own:
    // re-measuring a column in its own rule while the calc keeps the old operand sinks the name
    // column below its floor with every gate green. That is not hypothetical — the comment beside
    // `--sni` names 380px as the LOW point of the height curve, so `--sni: 380px` is the edit a
    // future reader is invited to make, and it would leave the name column at 120px against 220.
    //
    // Checking the calc AGAINST the widths is what makes a coordinated re-measurement need no edit
    // here, and an uncoordinated one red. Only the name floor is stated.
    const px = new Map(declaredColumns().map((c) => [c.name, c.px] as const));
    const w = (name: string): number => {
      const value = px.get(name);
      if (value === undefined) {
        throw new Error(`no width declared for .jp-companyBrowse__col--${name}`);
      }
      return value;
    };

    expect(minWidthOperands(BASE_MIN_WIDTH)).toEqual([
      w("orgnr"),
      w("seat"),
      w("sni"),
      NAME_FLOOR_PX,
    ]);
    expect(minWidthOperands(FOLLOW_MIN_WIDTH)).toEqual([
      w("orgnr"),
      w("seat"),
      w("sni"),
      w("follow"),
      NAME_FLOOR_PX,
    ]);
  });

  it("declares the wider floor after the narrower one, which is the only reason it wins", () => {
    // One class each, so the two rules TIE on specificity and source order breaks it. Sorting this
    // block alphabetically — or any formatter that reorders rules — hands the five-column table the
    // four-column minimum, and its name column collapses to 60px.
    const base = css.search(/\.jp-companyBrowse\s*\{/);
    const withFollow = css.search(/\.jp-companyBrowse--withFollow\s*\{/);
    expect(base).toBeGreaterThan(-1);
    expect(withFollow).toBeGreaterThan(base);
  });
});
