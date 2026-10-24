import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
import svMessages from "../../../messages/sv";
import { PublicJobAd } from "./public-job-ad";
import { safeRedirectPath } from "@/lib/auth/safe-redirect";
import { publicJobAdTitle } from "@/lib/job-ads/public-job-ad-title";
import { publicJobAdUrl } from "@/lib/job-ads/public-job-ad-url";
import { shareTargetLinks } from "@/lib/job-ads/share-job-ad-links";
import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";
const GROUP = "Grundutbildade sjuksköterskor";

const AD: PublicJobAdDto = {
  id: ID,
  title: "Sjuksköterska till vårdcentral",
  companyName: "Region Exempel",
  description: "Om tjänsten\n\nVi söker dig som vill arbeta med primärvård.\n\n- Legitimation\n- Körkort",
  url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
  publishedAt: "2026-10-01T08:00:00Z",
  expiresAt: "2026-11-01T08:00:00Z",
  municipalityName: "Stockholm",
  regionName: "Stockholms län",
  occupationGroupName: GROUP,
  employmentTypeConceptId: "gro4_cWF_6D7", // Vikariat
  worktimeExtentConceptId: "6YE1_gAC_R2G", // Heltid
};

const jsonLdScripts = (container: HTMLElement) =>
  [...container.querySelectorAll('script[type="application/ld+json"]')] as HTMLScriptElement[];

const factsOf = (container: HTMLElement) =>
  [...container.querySelectorAll(".jp-modal__facts .jp-modal__fact")].map((f) => f.textContent?.replace(/\s+/g, " ").trim());

/** The block after the article: the parent of its heading. */
function explainersOf(heading: string): HTMLElement {
  const block = screen.getByRole("heading", { level: 2, name: heading }).parentElement;
  if (block === null) throw new Error("the heading has no parent");
  return block;
}

describe("PublicJobAd", () => {
  it("is one landmark with one h1 (the ad's title), the company and both dates", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const main = container.querySelector("main");
    expect(main).toHaveAttribute("id", "main");
    expect(main).toHaveAttribute("tabindex", "-1");
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(screen.getByRole("heading", { level: 1, name: AD.title })).toBeInTheDocument();
    expect(screen.getByText("Region Exempel")).toBeInTheDocument();
    expect(container.querySelector(".jp-modal__meta")).toHaveTextContent(
      /Sista ansökningsdag \d+ .+ 2026\s*Publicerad \d+ .+ 2026/
    );
  });

  it("shows the whole description in the first render: headings, paragraphs and lists, with no excerpt and no toggle", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    expect(screen.getByRole("heading", { level: 2, name: "Om tjänsten" })).toBeInTheDocument();
    expect(screen.getByText(/Vi söker dig som vill arbeta med primärvård/)).toBeInTheDocument();
    const description = container.querySelector<HTMLElement>(".jp-modal__description");
    expect(within(description!).getAllByRole("listitem").map((li) => li.textContent)).toEqual(["Legitimation", "Körkort"]);

    expect(container.querySelector("[data-collapsed]")).toBeNull();
    expect(container.querySelector("[aria-expanded]")).toBeNull();
    expect(screen.getAllByRole("button")).toEqual([
      screen.getByRole("button", { name: "Kopiera länken" }),
      screen.getByRole("button", { name: "Fler sätt att dela" }),
    ]);
  });

  it("renders the description as text, never as HTML (ADR 0160 D9.6); the only script is the JSON-LD data block", () => {
    const hostile = '<img src=x onerror="alert(1)"><script>alert(2)</script> Fet <b>text</b>';
    const { container } = render(<PublicJobAd jobAd={{ ...AD, description: hostile }} />);

    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector(".jp-modal__description b")).toBeNull();
    expect(container.textContent).toContain("<script>alert(2)</script>");
    const scripts = [...container.querySelectorAll("script")];
    expect(scripts.map((s) => s.getAttribute("type"))).toEqual(["application/ld+json"]);
    expect(scripts[0]?.innerHTML).not.toContain("<");
  });

  it("renders the title and the company as text too", () => {
    const { container } = render(
      <PublicJobAd jobAd={{ ...AD, title: "<b>Titel</b>", companyName: "<i>Bolag</i>" }} />
    );

    expect(container.querySelector("h1 b")).toBeNull();
    expect(container.querySelector(".jp-modal__company i")).toBeNull();
  });

  it("carries the link out once, as the page's one primary action, in the head under its own heading (ADR 0183)", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    expect(container.querySelectorAll(".jp-btn--primary")).toHaveLength(1);
    const link = screen.getByRole("link", { name: "Öppna annonsen hos Arbetsförmedlingen" });
    expect(link).toHaveAttribute("href", AD.url);
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    expect(link).toHaveClass("jp-btn--primary");
    expect(link.closest("header")).not.toBeNull();
    const section = link.closest("section");
    expect(section).toHaveAccessibleName("Sök jobbet");
    expect(within(section as HTMLElement).getByRole("heading", { level: 2, name: "Sök jobbet" })).toBeInTheDocument();
    expect(container.querySelector(".jp-modal__foot")?.querySelector(`a[href="${AD.url}"]`)).toBeNull();
  });

  it("uses the plain label when the address is not Arbetsförmedlingen's", () => {
    render(<PublicJobAd jobAd={{ ...AD, url: "https://example.com/jobb/1" }} />);

    expect(screen.getByRole("link", { name: "Öppna annonsen" })).toHaveAttribute("href", "https://example.com/jobb/1");
    expect(screen.queryByText(/hos Arbetsförmedlingen/)).toBeNull();
  });

  it("links off the site only to the ad's own address and the share row's four targets, built from the canonical (ADR 0183)", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const offSite = [...container.querySelectorAll<HTMLAnchorElement>("a[href]")]
      .map((a) => a.getAttribute("href") ?? "")
      .filter((href) => !href.startsWith("/"));
    const targets = shareTargetLinks({ title: publicJobAdTitle(AD), url: publicJobAdUrl(ID) }).map((link) => link.href);
    expect(offSite).toEqual([AD.url, ...targets]);
    expect(offSite.join(" ")).not.toMatch(/utm_/);
    for (const a of container.querySelectorAll<HTMLAnchorElement>('a[href^="https://"]:not([href^="https://arbetsformedlingen"])')) {
      expect(a.getAttribute("rel")?.split(" ").sort()).toEqual(["nofollow", "noopener", "noreferrer"]);
    }
    expect(container.querySelector("script[src]")).toBeNull();
    expect(container.querySelector("iframe, img[src^='http']")).toBeNull();
  });

  // Declared unreachable: JobAd refuses an empty address on write and the import skips such a record, so this only
  // pins that the read side degrades to no link out at all (no primary, no apply heading) if that ever breaks.
  it("renders no link out and no apply heading if an address ever arrives empty", () => {
    const { container } = render(<PublicJobAd jobAd={{ ...AD, url: "" }} />);

    expect(container.querySelector(".jp-btn--primary")).toBeNull();
    expect(screen.queryByRole("link", { name: /Öppna annonsen/ })).toBeNull();
    expect(screen.queryByRole("heading", { name: "Sök jobbet" })).toBeNull();
  });

  it("invites the visitor in the footer to sign in and come back to this ad, by a plain nofollow link (ADR 0183)", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const footer = container.querySelector(".jp-modal__foot") as HTMLElement;
    expect(footer).toHaveAttribute("data-nosnippet");
    expect(
      within(footer).getByRole("heading", { level: 2, name: "Spara annonsen och håll ordning på din ansökan" })
    ).toBeInTheDocument();
    const link = within(footer).getByRole("link", { name: "Logga in eller skapa konto" });
    expect(link).toHaveAttribute("rel", "nofollow");
    expect(link).toHaveClass("jp-btn--secondary");
    expect(link).not.toHaveAttribute("target");
    const href = link.getAttribute("href") ?? "";
    expect(href).toBe(`/logga-in?next=%2Fjobb%2F${ID}`);
    const next = new URL(href, "https://jobbliggaren.se").searchParams.get("next");
    expect(next).toBe(`/jobb/${ID}`);
    expect(safeRedirectPath(next)).toBe(`/jobb/${ID}`);
  });

  it("keeps the invitation when the address is empty: it does not depend on the link out", () => {
    const { container } = render(<PublicJobAd jobAd={{ ...AD, url: "" }} />);

    const footer = container.querySelector(".jp-modal__foot") as HTMLElement;
    expect(within(footer).getByRole("link", { name: "Logga in eller skapa konto" })).toBeInTheDocument();
  });

  it("offers the share row after the description and the notice, in the card's body, under its own h2", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const row = screen.getByRole("region", { name: "Dela annonsen" });
    expect(row).toHaveAttribute("data-nosnippet");
    expect(within(row).getByRole("heading", { level: 2, name: "Dela annonsen" })).toBeInTheDocument();
    expect(row.previousElementSibling).toHaveClass("jp-recruiter-notice");
    expect(row.parentElement).toHaveClass("jp-modal__body");
    expect(container.querySelector(".jp-modal__foot")?.contains(row)).toBe(false);
    expect(within(row).getByRole("link", { name: "Dela via E-post" }).getAttribute("href")).toBe(
      `mailto:?subject=${encodeURIComponent(publicJobAdTitle(AD))}&body=${encodeURIComponent(publicJobAdUrl(ID))}`
    );
  });

  it("links the recruiter notice after the description, without any contact block", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const notice = container.querySelector(".jp-recruiter-notice");
    expect(notice).not.toBeNull();
    const link = within(notice as HTMLElement).getByRole("link");
    expect(link).toHaveAttribute("href", "/kontaktperson-i-annons");
    expect(link).toHaveTextContent("Så behandlar vi uppgifter om kontaktpersoner i annonser");
    expect(notice).not.toHaveTextContent(/Är du kontaktperson/);
    expect(notice?.previousElementSibling?.tagName).toBe("SECTION");
    const outsideShareRow = [...container.querySelectorAll('a[href^="mailto:"], a[href^="tel:"]')].filter(
      (a) => a.closest('[aria-labelledby="jp-share-title"]') === null
    );
    expect(outsideShareRow).toEqual([]);
  });

  it("offers nothing personal: no save, follow, applied, match or previous-applications", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const card = container.querySelector("article") as HTMLElement;
    const personal = card.cloneNode(true) as HTMLElement;
    personal.querySelector(".jp-modal__foot")?.remove();
    for (const label of [/Spara/, /Bevaka/, /Har ansökt/, /matchar/i, /tidigare ansökan/]) {
      expect(personal).not.toHaveTextContent(label);
    }
    expect(screen.getAllByRole("button")).toEqual([
      screen.getByRole("button", { name: "Kopiera länken" }),
      screen.getByRole("button", { name: "Fler sätt att dela" }),
    ]);
  });

  it("is the borderless-page variant of the modal envelope, with its own edge: no shadow, no animation, no inline style", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const envelope = container.querySelector("article");
    expect(envelope).toHaveClass("jp-modal", "jp-modal--page", "jp-modal--public");
    expect(container.querySelector("[style]")).toBeNull();
  });

  it("shows an ad with no deadline without a deadline row", () => {
    const { container } = render(<PublicJobAd jobAd={{ ...AD, expiresAt: null }} />);

    expect(container.querySelector(".jp-modal__meta")).not.toHaveTextContent("Sista ansökningsdag");
    expect(container.querySelector(".jp-modal__meta")).toHaveTextContent("Publicerad");
  });

  it("shows no internal id", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);
    expect(container.textContent).not.toContain(ID);
  });

  it("takes the cookie bounce's marker off the address bar and keeps the rest of the address and the history state", () => {
    window.history.pushState({ keep: "state" }, "", `/jobb/${ID}?vidare=1&a=1#beskrivning`);
    expect(window.location.search).toBe("?vidare=1&a=1");

    render(<PublicJobAd jobAd={AD} />);

    expect(window.location.search).toBe("?a=1");
    expect(window.location.pathname).toBe(`/jobb/${ID}`);
    expect(window.location.hash).toBe("#beskrivning");
    expect(window.history.state).toEqual({ keep: "state" });
  });

  it("leaves an address without the marker alone", () => {
    window.history.pushState(null, "", `/jobb/${ID}?a=1`);
    const replace = vi.spyOn(window.history, "replaceState");

    render(<PublicJobAd jobAd={AD} />);

    expect(replace).not.toHaveBeenCalled();
    replace.mockRestore();
  });
});

describe("PublicJobAd: the facts row (ADR 0171)", () => {
  it("shows kommun before län, then the employment type and extent as the catalogue names them, then the occupation group, each with its axis for a screen reader", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const facts = container.querySelector(".jp-modal__facts");
    expect(facts).not.toBeNull();
    expect(factsOf(container)).toEqual([
      "Ort Stockholm, Stockholms län",
      "Anställning Vikariat · Heltid",
      `Yrkesgrupp ${GROUP}`,
    ]);
    expect(facts!.querySelectorAll(".sr-only")).toHaveLength(3);
    expect(facts!.querySelectorAll("svg[aria-hidden='true']")).toHaveLength(3);
  });

  it("marks the group's value Swedish, and only the value: the axis name follows the page's language (WCAG 3.1.2)", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const marked = [...container.querySelectorAll("[lang]")];
    expect(marked).toHaveLength(1);
    expect(marked[0]).toHaveAttribute("lang", "sv");
    expect(marked[0]?.textContent).toBe(GROUP);
    expect(marked[0]?.closest(".jp-modal__fact")).toBe([...container.querySelectorAll(".jp-modal__fact")].at(-1));
    expect(marked[0]?.previousElementSibling).toHaveClass("sr-only");
    expect(marked[0]?.previousElementSibling?.textContent).toBe("Yrkesgrupp ");
  });

  it("keeps the value marked Swedish on the English page, under the English axis name", () => {
    const { container } = render(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <PublicJobAd jobAd={AD} />
      </NextIntlClientProvider>
    );

    expect(factsOf(container).at(-1)).toBe(`Occupational group ${GROUP}`);
    expect(container.querySelector('.jp-modal__facts [lang="sv"]')?.textContent).toBe(GROUP);
  });

  it.each([
    ["null", null],
    ["absent", undefined],
  ])("leaves the group out when its name is %s, and the row is then the one it was before the group", (_label, name) => {
    const { container } = render(<PublicJobAd jobAd={{ ...AD, occupationGroupName: name }} />);

    expect(factsOf(container)).toEqual(["Ort Stockholm, Stockholms län", "Anställning Vikariat · Heltid"]);
    expect(container.querySelector("[lang]")).toBeNull();
    expect(container.querySelector(".jp-modal__facts")).not.toHaveTextContent("Yrkesgrupp");
  });

  it("shows the group alone when nothing else is known", () => {
    const { container } = render(
      <PublicJobAd
        jobAd={{ ...AD, municipalityName: null, regionName: null, employmentTypeConceptId: null, worktimeExtentConceptId: null }}
      />
    );

    expect(factsOf(container)).toEqual([`Yrkesgrupp ${GROUP}`]);
  });

  it("shows the kommun alone when the län is not resolved, and the län alone when the kommun is not", () => {
    const { container: a } = render(<PublicJobAd jobAd={{ ...AD, regionName: null }} />);
    expect(a.querySelector(".jp-modal__fact")).toHaveTextContent(/^Ort Stockholm$/);

    const { container: b } = render(<PublicJobAd jobAd={{ ...AD, municipalityName: null }} />);
    expect(b.querySelector(".jp-modal__fact")).toHaveTextContent(/^Ort Stockholms län$/);
  });

  it("leaves out an employment id the catalogue does not know, and the whole row when nothing is known", () => {
    const { container: a } = render(<PublicJobAd jobAd={{ ...AD, employmentTypeConceptId: "drifted_id" }} />);
    expect([...a.querySelectorAll(".jp-modal__fact")][1]).toHaveTextContent(/Anställning Heltid$/);
    expect(a.textContent).not.toContain("drifted_id");

    const { container: b } = render(
      <PublicJobAd
        jobAd={{
          ...AD,
          municipalityName: null,
          regionName: null,
          occupationGroupName: null,
          employmentTypeConceptId: null,
          worktimeExtentConceptId: null,
        }}
      />
    );
    expect(b.querySelector(".jp-modal__facts")).toBeNull();
  });

  it("never shows a concept id", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);
    for (const id of ["gro4_cWF_6D7", "6YE1_gAC_R2G"]) {
      expect(container.textContent).not.toContain(id);
    }
  });
});

describe("PublicJobAd: the JobPosting data block (ADR 0171)", () => {
  it("carries exactly one JSON-LD script for an eligible ad, parsable, with no literal `<`", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const scripts = jsonLdScripts(container);
    expect(scripts).toHaveLength(1);
    expect(scripts[0]?.innerHTML).not.toContain("<");
    const posting = JSON.parse(scripts[0]?.innerHTML ?? "") as Record<string, unknown>;
    expect(posting["@type"]).toBe("JobPosting");
    expect(posting.title).toBe(AD.title);
    expect(posting.validThrough).toBe("2026-11-01T08:00:00.000Z");
  });

  it("asserts nothing the page does not show: title, company, kommun, län and every employment label are in the rendered text", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);
    // The article holds everything a visitor sees; the data block is its sibling, so its own JSON cannot
    // satisfy these assertions.
    const text = container.querySelector("article")?.textContent ?? "";
    const posting = JSON.parse(jsonLdScripts(container)[0]?.innerHTML ?? "") as {
      title: string;
      hiringOrganization: { name: string };
      jobLocation: { address: { addressLocality: string; addressRegion: string } };
      employmentType: string[];
    };

    expect(text).toContain(posting.title);
    expect(text).toContain(posting.hiringOrganization.name);
    expect(text).toContain(posting.jobLocation.address.addressLocality);
    expect(text).toContain(posting.jobLocation.address.addressRegion);
    // FULL_TIME ↔ Heltid, TEMPORARY ↔ Vikariat: the visible labels of the ids the values came from.
    expect(posting.employmentType).toEqual(["FULL_TIME", "TEMPORARY"]);
    expect(text).toContain("Heltid");
    expect(text).toContain("Vikariat");
  });

  it("carries no JSON-LD for an ineligible ad (no kommun), and the page is otherwise the same", () => {
    const { container: eligible } = render(<PublicJobAd jobAd={AD} />);
    const { container: ineligible } = render(<PublicJobAd jobAd={{ ...AD, municipalityName: null }} />);

    expect(jsonLdScripts(ineligible)).toHaveLength(0);
    expect(ineligible.querySelector("h1")?.textContent).toBe(eligible.querySelector("h1")?.textContent);
    expect(ineligible.querySelector(".jp-modal__description")?.textContent).toBe(
      eligible.querySelector(".jp-modal__description")?.textContent
    );
  });

  it("carries no JSON-LD for work abroad", () => {
    const { container } = render(<PublicJobAd jobAd={{ ...AD, employmentTypeConceptId: "9Wuo_2Yb_36E" }} />);
    expect(jsonLdScripts(container)).toHaveLength(0);
    expect(container.textContent).toContain("Arbete utomlands");
  });

  it("never carries the occupation group the facts row shows (#2203)", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const block = jsonLdScripts(container)[0]?.innerHTML ?? "";
    expect(block).not.toBe("");
    expect(block).not.toContain(GROUP);
    expect(block).not.toContain("occupationalCategory");
  });
});

describe("PublicJobAd: the explainer links after the article (#2203)", () => {
  it("sit after the article and outside it, inside main, under their own h2 with no landmark", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const block = explainersOf("Relaterade sidor");
    const article = container.querySelector("article");
    expect(article).not.toBeNull();
    expect(block.closest("article")).toBeNull();
    expect(block.closest("main")).toBe(container.querySelector("main"));
    expect(article!.compareDocumentPosition(block) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(block.querySelector("h2")).toHaveClass("jp-eyebrow");
    expect(block.closest("section, nav, aside, [role]")).toBeNull();
  });

  it("stand in the card's column, given in px so it cannot drift from the card's 760 px", () => {
    render(<PublicJobAd jobAd={AD} />);

    const column = explainersOf("Relaterade sidor").parentElement;
    expect(column?.parentElement?.tagName).toBe("MAIN");
    expect(column).toHaveClass("mx-auto", "w-full", "max-w-[760px]");
  });

  it("link CV review first and matching second, by the footer's own names for the same two pages (WCAG 3.2.4)", () => {
    render(<PublicJobAd jobAd={AD} />);

    const links = within(explainersOf("Relaterade sidor")).getAllByRole("link");
    expect(links.map((a) => a.getAttribute("href"))).toEqual(["/cv-granskning", "/matchning"]);
    expect(links.map((a) => a.textContent)).toEqual([
      svMessages.landing.common.cvReviewLink,
      svMessages.landing.footer.support.howMatching,
    ]);
    for (const link of links) expect(link).toHaveAttribute("data-information-link");
  });

  it("give each link its own id, distinct from the notice's, and the page no duplicate id", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const links = within(explainersOf("Relaterade sidor")).getAllByRole("link");
    expect(links.map((a) => a.id)).toEqual([`information-job-${ID}-cv-review`, `information-job-${ID}-matching`]);
    const ids = [...container.querySelectorAll("[id]")].map((el) => el.id);
    expect(new Set(ids).size).toBe(ids.length);
    expect(ids).toContain(`information-job-${ID}`);
  });

  it("carry no lead sentence and no button, so the apply link stays the page's one primary action", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const block = explainersOf("Relaterade sidor");
    expect(block.querySelector("p")).toBeNull();
    expect(block.querySelector(".jp-btn")).toBeNull();
    expect(container.querySelectorAll(".jp-btn--primary")).toHaveLength(1);
  });

  it("read in English on the English page", () => {
    render(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <PublicJobAd jobAd={AD} />
      </NextIntlClientProvider>
    );

    const links = within(explainersOf("Related pages")).getAllByRole("link");
    expect(links.map((a) => a.textContent)).toEqual([
      enMessages.landing.common.cvReviewLink,
      enMessages.landing.footer.support.howMatching,
    ]);
  });
});

describe("PublicJobAd: the related section after the article (ADR 0182)", () => {
  const RELATED_SENTINEL = "Relaterad Sentinelsson Enskild Firma";
  const row = (n: number, overrides: Partial<Record<string, string | null>> = {}) => ({
    id: `0b0b0b0b-1111-4222-8333-${String(n).padStart(12, "0")}`,
    title: `Undersköterska ${n}`,
    companyName: n === 1 ? RELATED_SENTINEL : `Arbetsgivare ${n} AB`,
    municipalityName: "Solna",
    regionName: "Stockholms län",
    publishedAt: "2026-10-02T08:00:00Z",
    ...overrides,
  });
  const FIVE = [1, 2, 3, 4, 5].map((n) => row(n));

  const sectionOf = (container: HTMLElement) =>
    container.querySelector<HTMLElement>(`section[data-nosnippet][aria-labelledby="jp-related-title"]`);

  it("is absent with no rows, and the page is then the page without the prop", () => {
    const { container: without } = render(<PublicJobAd jobAd={AD} />);
    const { container: empty } = render(<PublicJobAd jobAd={AD} related={[]} />);

    expect(sectionOf(empty)).toBeNull();
    expect(empty.querySelector(`section[aria-labelledby="jp-related-title"]`)).toBeNull();
    expect(screen.queryAllByRole("heading", { name: "Andra jobb i samma yrkesgrupp" })).toHaveLength(0);
    expect(empty.innerHTML).toBe(without.innerHTML);
  });

  it.each([1, 5])("with %i rows is one nosnippet section named by its h2, with a plain h3 link per row", (count) => {
    const { container } = render(<PublicJobAd jobAd={AD} related={FIVE.slice(0, count)} />);

    const sections = container.querySelectorAll(`section[data-nosnippet][aria-labelledby="jp-related-title"]`);
    expect(sections).toHaveLength(1);
    const section = sections[0] as HTMLElement;
    const heading = within(section).getByRole("heading", { level: 2, name: "Andra jobb i samma yrkesgrupp" });
    expect(heading).toHaveClass("jp-eyebrow");
    expect(section).toHaveAttribute("aria-labelledby", heading.id);
    expect(within(section).getByRole("list")).toHaveAttribute("aria-labelledby", heading.id);

    const titles = within(section).getAllByRole("heading", { level: 3 });
    expect(titles.map((h) => h.textContent)).toEqual(FIVE.slice(0, count).map((r) => r.title));
    const links = within(section).getAllByRole("link");
    expect(links.map((a) => a.getAttribute("href"))).toEqual(FIVE.slice(0, count).map((r) => `/jobb/${r.id}`));
    for (const link of links) {
      expect(link).not.toHaveAttribute("data-information-link");
      for (const describedBy of (link.getAttribute("aria-describedby") ?? "").split(" "))
        expect(container.querySelector(`#${CSS.escape(describedBy)}`)).not.toBeNull();
    }
  });

  it("names no group and no count in its heading", () => {
    const { container } = render(<PublicJobAd jobAd={AD} related={FIVE} />);

    const heading = within(sectionOf(container)!).getByRole("heading", { level: 2 });
    expect(heading.textContent).toBe(svMessages.publicjob.related.heading);
    expect(heading.textContent).not.toContain(GROUP);
    expect(heading.textContent).not.toMatch(/\d/);
  });

  it("sits after the article and before the explainer links, outside the article, in the card's column", () => {
    const { container } = render(<PublicJobAd jobAd={AD} related={FIVE} />);

    const article = container.querySelector("article.jp-modal")!;
    const section = sectionOf(container)!;
    const explainers = explainersOf("Relaterade sidor");
    expect(section.closest("article.jp-modal")).toBeNull();
    expect(article.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(section.compareDocumentPosition(explainers) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(section.parentElement).toBe(explainers.parentElement);
    expect(section.parentElement).toHaveClass("mx-auto", "w-full", "max-w-[760px]");
  });

  it("gives every row its own ids, so the page has no duplicate id and keeps one primary action", () => {
    const { container } = render(<PublicJobAd jobAd={AD} related={FIVE} />);

    const ids = [...container.querySelectorAll("[id]")].map((el) => el.id);
    expect(new Set(ids).size).toBe(ids.length);
    expect(ids.filter((id) => id.startsWith("related-"))).toHaveLength(FIVE.length * 2);
    expect(container.querySelectorAll(".jp-btn--primary")).toHaveLength(1);
  });

  it("puts nothing of another ad into the JobPosting data block", () => {
    const { container } = render(<PublicJobAd jobAd={AD} related={FIVE} />);

    const block = jsonLdScripts(container)[0]?.innerHTML ?? "";
    expect(block).not.toBe("");
    expect(block).not.toContain(RELATED_SENTINEL);
    expect(block).not.toContain("Undersköterska");
  });

  it("reads in English on the English page", () => {
    render(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <PublicJobAd jobAd={AD} related={FIVE} />
      </NextIntlClientProvider>
    );

    expect(
      screen.getByRole("heading", { level: 2, name: enMessages.publicjob.related.heading })
    ).toBeInTheDocument();
  });
});
