import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { PublicJobAd } from "./public-job-ad";
import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";

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
  employmentTypeConceptId: "gro4_cWF_6D7", // Vikariat
  worktimeExtentConceptId: "6YE1_gAC_R2G", // Heltid
};

const jsonLdScripts = (container: HTMLElement) =>
  [...container.querySelectorAll('script[type="application/ld+json"]')] as HTMLScriptElement[];

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
    expect(screen.getAllByRole("listitem").map((li) => li.textContent)).toEqual(["Legitimation", "Körkort"]);

    expect(container.querySelector("[data-collapsed]")).toBeNull();
    expect(container.querySelector("[aria-expanded]")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
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

  it("carries the link out twice, as a secondary action under the dates and as the one primary action in the footer", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    expect(container.querySelectorAll(".jp-btn--primary")).toHaveLength(1);
    const links = screen.getAllByRole("link", { name: "Öppna annonsen hos Arbetsförmedlingen" });
    expect(links).toHaveLength(2);
    for (const link of links) {
      expect(link).toHaveAttribute("href", AD.url);
      expect(link).toHaveAttribute("target", "_blank");
      expect(link).toHaveAttribute("rel", "noopener noreferrer");
    }
    expect(links[0]).toHaveClass("jp-btn--secondary");
    expect(links[0]?.closest("header")).not.toBeNull();
    expect(links[1]).toHaveClass("jp-btn--primary");
    expect(links[1]?.closest(".jp-modal__foot")).not.toBeNull();
  });

  it("uses the plain label on both when the address is not Arbetsförmedlingen's", () => {
    render(<PublicJobAd jobAd={{ ...AD, url: "https://example.com/jobb/1" }} />);

    const links = screen.getAllByRole("link", { name: "Öppna annonsen" });
    expect(links).toHaveLength(2);
    for (const link of links) expect(link).toHaveAttribute("href", "https://example.com/jobb/1");
    expect(screen.queryByText(/hos Arbetsförmedlingen/)).toBeNull();
  });

  // Declared unreachable: JobAd refuses an empty address on write and the import skips such a record, so this only
  // pins that the read side degrades to no link at all (no button, no footer, no head row) if that ever breaks.
  it("renders no link, no footer and no head row if an address ever arrives empty", () => {
    const { container } = render(<PublicJobAd jobAd={{ ...AD, url: "" }} />);

    expect(container.querySelector(".jp-btn")).toBeNull();
    expect(container.querySelector(".jp-modal__foot")).toBeNull();
    expect(container.querySelector("header p.mt-4")).toBeNull();
  });

  it("links the recruiter notice after the description, without any contact block", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const notice = container.querySelector(".jp-recruiter-notice");
    expect(notice).not.toBeNull();
    expect(within(notice as HTMLElement).getByRole("link")).toHaveAttribute("href", "/kontaktperson-i-annons");
    expect(notice?.previousElementSibling?.tagName).toBe("SECTION");
    expect(container.querySelector('a[href^="mailto:"], a[href^="tel:"]')).toBeNull();
  });

  it("offers nothing personal: no save, follow, applied, match or previous-applications", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    for (const label of [/Spara/, /Bevaka/, /Har ansökt/, /matchar/i, /tidigare ansökan/]) {
      expect(container).not.toHaveTextContent(label);
    }
    expect(screen.queryByRole("button")).toBeNull();
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
  it("shows kommun before län, and the employment type and extent as the catalogue names them, each with its axis for a screen reader", () => {
    const { container } = render(<PublicJobAd jobAd={AD} />);

    const facts = container.querySelector(".jp-modal__facts");
    expect(facts).not.toBeNull();
    const items = [...facts!.querySelectorAll(".jp-modal__fact")].map((f) => f.textContent?.replace(/\s+/g, " ").trim());
    expect(items).toEqual(["Ort Stockholm, Stockholms län", "Anställning Vikariat · Heltid"]);
    expect(facts!.querySelectorAll(".sr-only")).toHaveLength(2);
    expect(facts!.querySelectorAll("svg[aria-hidden='true']")).toHaveLength(2);
  });

  it("shows the kommun alone when the län is not resolved, and the län alone when the kommun is not", () => {
    const { container: a } = render(<PublicJobAd jobAd={{ ...AD, regionName: null }} />);
    expect(a.querySelector(".jp-modal__fact")).toHaveTextContent(/^Ort Stockholm$/);

    const { container: b } = render(<PublicJobAd jobAd={{ ...AD, municipalityName: null }} />);
    expect(b.querySelector(".jp-modal__fact")).toHaveTextContent(/^Ort Stockholms län$/);
  });

  it("leaves out an employment id the catalogue does not know, and the whole row when nothing is known", () => {
    const { container: a } = render(<PublicJobAd jobAd={{ ...AD, employmentTypeConceptId: "drifted_id" }} />);
    expect([...a.querySelectorAll(".jp-modal__fact")].at(-1)).toHaveTextContent(/Heltid$/);
    expect(a.textContent).not.toContain("drifted_id");

    const { container: b } = render(
      <PublicJobAd
        jobAd={{ ...AD, municipalityName: null, regionName: null, employmentTypeConceptId: null, worktimeExtentConceptId: null }}
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
});
