import { describe, it, expect, vi } from "vitest";
import { overviewSnapshotFixture } from "@/test/fixtures/admin-overview";
const { load, redirect } = vi.hoisted(() => ({ load: vi.fn(), redirect: vi.fn((path: string) => { throw new Error(path); }) }));
vi.mock("@/lib/api/admin-overview", () => ({ loadAdminOverview: load }));
vi.mock("next/navigation", () => ({ redirect }));
import { render, screen, within } from "@testing-library/react";
import { AdminOverview } from "@/components/admin/admin-overview";
import AdminOverviewPage from "./page";

function renderPage() {
  render(<AdminOverview />);
}

const CARDS = [
  "Nya användare",
  "Användare totalt",
  "Aktiva användare",
  "Inloggningar",
  "Nya användare och inloggningar",
  "Tjänster",
  "Server",
  "Backup",
  "E-post",
  "Kräver uppmärksamhet",
  "Senaste händelser",
];

describe("overview preview — unavailable regions", () => {
  it("is headed Översikt and renders every designed card as a labelled region", () => {
    renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "Översikt" })).toBeInTheDocument();
    for (const card of CARDS) {
      expect(screen.getByRole("region", { name: card })).toBeInTheDocument();
    }
  });

  it("shows no number at all: every value is an en-dash with no unit", () => {
    renderPage();

    // The trend card's only digits are its period labels ("7 dagar"), checked below.
    for (const card of CARDS.filter((name) => name !== "Nya användare och inloggningar")) {
      expect(screen.getByRole("region", { name: card }).textContent ?? "").not.toMatch(/\d/);
    }
    for (const card of ["Nya användare", "Användare totalt", "Aktiva användare", "Inloggningar", "E-post"]) {
      const region = screen.getByRole("region", { name: card });
      expect(within(region).getByText("–")).toBeInTheDocument();
      expect(within(region).getByText("Kommer snart")).toBeInTheDocument();
    }
  });

  it("says Kommer snart in every unbuilt list instead of placeholder rows", () => {
    renderPage();

    for (const card of ["Tjänster", "Kräver uppmärksamhet", "Senaste händelser"]) {
      const region = screen.getByRole("region", { name: card });
      expect(within(region).getByText("Kommer snart")).toBeInTheDocument();
      expect(within(region).queryByRole("listitem")).toBeNull();
    }
  });

  it("keeps the attention edge neutral while the state is unknown", () => {
    renderPage();

    expect(screen.getByRole("region", { name: "Kräver uppmärksamhet" })).toHaveAttribute(
      "data-state",
      "unknown",
    );
  });

  it("disables the trend period group and describes it with the region's Kommer snart line", () => {
    renderPage();

    const group = screen.getByRole("radiogroup", { name: "Period" });
    const options = within(group).getAllByRole("radio");
    expect(options.map((option) => option.textContent)).toEqual(["7 dagar", "30 dagar", "90 dagar"]);
    for (const option of options) expect(option).toBeDisabled();
    const describedBy = group.getAttribute("aria-describedby");
    expect(describedBy).not.toBeNull();
    expect(document.getElementById(describedBy ?? "")?.textContent).toBe("Kommer snart");
  });

  it("links each card to the page that holds its subject", () => {
    renderPage();

    expect(
      within(screen.getByRole("region", { name: "Tjänster" })).getByRole("link", { name: "Loggar" }),
    ).toHaveAttribute("href", "/admin/loggar");
    expect(
      within(screen.getByRole("region", { name: "E-post" })).getByRole("link", { name: "E-postleverans" }),
    ).toHaveAttribute("href", "/admin/e-post");
    expect(
      within(screen.getByRole("region", { name: "Senaste händelser" })).getByRole("link", {
        name: "Granskning",
      }),
    ).toHaveAttribute("href", "/admin/granskning");
  });

  it("points its card links under the base path it is given", () => {
    render(<AdminOverview basePath="/admin/forhandsvisning" />);

    expect(
      within(screen.getByRole("region", { name: "Tjänster" })).getByRole("link", { name: "Loggar" }),
    ).toHaveAttribute("href", "/admin/forhandsvisning/loggar");
  });
});

describe("/admin initial server data", () => {
  it("loads privileged data on the server and renders the exact total", async () => {
    load.mockResolvedValueOnce({ kind: "ok", data: overviewSnapshotFixture(), loadedAt: Date.now() });
    render(await AdminOverviewPage());
    expect(within(screen.getByRole("region", { name: "Användare totalt" })).getByRole("link", { name: "12" })).toBeInTheDocument();
  });
  it.each([["unauthorized", "/logga-in"], ["forbidden", "/"]])("redirects %s before data render", async (kind, path) => {
    load.mockResolvedValueOnce({ kind });
    await expect(AdminOverviewPage()).rejects.toThrow(path);
    expect(redirect).toHaveBeenCalledWith(path);
  });
});