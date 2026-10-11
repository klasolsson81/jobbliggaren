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
};

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

  it("renders the description as text, never as HTML (ADR 0160 D9.6)", () => {
    const hostile = '<img src=x onerror="alert(1)"><script>alert(2)</script> Fet <b>text</b>';
    const { container } = render(<PublicJobAd jobAd={{ ...AD, description: hostile }} />);

    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector("script")).toBeNull();
    expect(container.querySelector(".jp-modal__description b")).toBeNull();
    expect(container.textContent).toContain("<script>alert(2)</script>");
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
