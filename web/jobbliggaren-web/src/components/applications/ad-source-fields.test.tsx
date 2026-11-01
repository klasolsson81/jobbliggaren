import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AdSourceFields } from "./ad-source-fields";
import type { ApplicationDto, JobAdSummaryDto } from "@/lib/dto/applications";

// #2248 — the card's a-kassa facts: Ort, Sökt and the ad link with its own copy button.

const AD_URL = "https://arbetsformedlingen.se/platsbanken/annonser/30123456";

const jobAd: JobAdSummaryDto = {
  jobAdId: "ad-1",
  title: "Testledare",
  company: "Exempelbolaget AB",
  url: AD_URL,
  source: "Platsbanken",
  publishedAt: "2026-10-01",
  expiresAt: null,
  status: "Active",
};

function makeApplication(overrides: Partial<ApplicationDto> = {}): ApplicationDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-10-30",
    updatedAt: "2026-10-31",
    appliedAt: "2026-10-31T09:00:00Z",
    lastStatusChangeAt: "2026-10-31T09:00:00Z",
    location: "Stockholm",
    jobAd,
    ...overrides,
  };
}

function renderFields(application: ApplicationDto) {
  return render(<AdSourceFields application={application} title="Testledare" />);
}

function valueOf(container: HTMLElement, label: string): HTMLElement {
  const term = within(container).getByText(label, { selector: "dt" });
  return term.nextElementSibling as HTMLElement;
}

describe("AdSourceFields (#2248)", () => {
  const writeText = vi.fn(async () => {});

  // userEvent.setup() installs its own clipboard stub, so ours goes on after it.
  function setupUser() {
    const user = userEvent.setup();
    Object.defineProperty(navigator, "clipboard", {
      value: { writeText },
      configurable: true,
    });
    return user;
  }

  beforeEach(() => {
    writeText.mockClear();
  });

  it("shows the ad's Ort and the applied date", () => {
    const { container } = renderFields(makeApplication());
    expect(valueOf(container, "Ort")).toHaveTextContent("Stockholm");
    expect(valueOf(container, "Sökt")).toHaveTextContent("31 okt. 2026");
  });

  it("shows a missing Ort as missing — never hidden, never guessed", () => {
    const { container } = renderFields(makeApplication({ location: null }));
    const ort = valueOf(container, "Ort");
    expect(ort).toHaveTextContent("Saknas i annonsen");
    expect(ort).toHaveAttribute("data-missing");
  });

  it("reads an absent location field (deploy skew) as missing", () => {
    const application = makeApplication();
    delete application.location;
    const { container } = renderFields(application);
    expect(valueOf(container, "Ort")).toHaveTextContent("Saknas i annonsen");
  });

  it("says a draft has not been applied for", () => {
    const { container } = renderFields(makeApplication({ status: "Draft", appliedAt: null }));
    expect(valueOf(container, "Sökt")).toHaveTextContent("Inte sökt än");
  });

  it("links the ad in a new tab, showing the URL itself as the link text", () => {
    renderFields(makeApplication());
    const link = screen.getByRole("link", { name: "Öppna annonsen Testledare i en ny flik" });
    expect(link).toHaveAttribute("href", AD_URL);
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    expect(link).toHaveAttribute("title", AD_URL);
    expect(link).toHaveTextContent("arbetsformedlingen.se/platsbanken/annonser/30123456");
  });

  it("copies exactly the URL the card shows", async () => {
    const user = setupUser();
    renderFields(makeApplication());

    await user.click(screen.getByRole("button", { name: "Kopiera länken till annonsen Testledare" }));

    expect(writeText).toHaveBeenCalledWith(AD_URL);
    expect(await screen.findByRole("status")).toHaveTextContent("Länken till annonsen är kopierad");
  });

  it("keeps the link and the copy button in the layer above the card's row link", () => {
    const { container } = renderFields(makeApplication());
    const layer = container.querySelector(".jp-app__adlink");
    expect(layer).not.toBeNull();
    expect(within(layer as HTMLElement).getByRole("link")).toBeInTheDocument();
    expect(within(layer as HTMLElement).getByRole("button")).toBeInTheDocument();
  });

  it("names a live ad's source", () => {
    renderFields(makeApplication());
    expect(screen.getByText("Platsbanken · öppnas i ny flik")).toBeInTheDocument();
  });

  it("says an erased ad's link is the one saved when the user applied", () => {
    renderFields(makeApplication({ jobAd: { ...jobAd, status: "Erased" } }));
    expect(screen.getByText("Platsbanken · sparad när du sökte")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Öppna annonsen Testledare i en ny flik" }))
      .toHaveAttribute("href", AD_URL);
  });

  it("labels a manual application's link as the user's own", () => {
    renderFields(
      makeApplication({
        jobAdId: null,
        location: null,
        jobAd: { ...jobAd, jobAdId: null, source: "Manual", status: null, url: "https://example.com/jobb" },
      }),
    );
    expect(screen.getByText("Länk du angav")).toBeInTheDocument();
  });

  it("never turns a non-http URL into a link, but still lets the user copy it", async () => {
    const user = setupUser();
    const typed = "javascript:alert(1)";
    renderFields(
      makeApplication({ jobAd: { ...jobAd, jobAdId: null, source: "Manual", status: null, url: typed } }),
    );

    expect(screen.queryByRole("link")).toBeNull();
    expect(screen.getByText(typed)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Kopiera länken till annonsen Testledare" }));
    expect(writeText).toHaveBeenCalledWith(typed);
  });

  it.each([
    ["no ad row", null],
    ["an ad without a URL", { ...jobAd, url: null }],
    ["an empty URL", { ...jobAd, url: "" }],
  ])("shows a missing link as missing (%s), with nothing to copy", (_case, ad) => {
    const { container } = renderFields(makeApplication({ jobAd: ad }));
    const value = valueOf(container, "Annons");
    expect(value).toHaveTextContent("Ingen annonslänk sparad");
    expect(value).toHaveAttribute("data-missing");
    expect(screen.queryByRole("button")).toBeNull();
    expect(screen.queryByRole("link")).toBeNull();
  });
});
