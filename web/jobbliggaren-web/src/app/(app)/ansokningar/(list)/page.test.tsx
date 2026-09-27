import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../messages/sv/pages.json";
import svAktivitetsrapport from "../../../../../messages/sv/aktivitetsrapport.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import AnsokningarPage from "./page";

const getServerSession = vi.fn();
const getPipeline = vi.fn<() => Promise<ApiResult<PipelineGroupDto[]>>>();

// The async server page resolves its copy through next-intl/server, which jsdom lacks; these
// are real translators over the Swedish catalogs.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: "pages" | "aktivitetsrapport") =>
    namespace === "pages"
      ? createTranslator({ locale: "sv", messages: { pages: svPages }, namespace })
      : createTranslator({
          locale: "sv",
          messages: { aktivitetsrapport: svAktivitetsrapport },
          namespace,
        }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("@/lib/api/applications", () => ({
  getPipeline: () => getPipeline(),
}));

vi.mock("@/lib/applications/view-preference", () => ({
  readApplicationsView: async () => "lista",
}));

vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: vi.fn(),
  logFollowUpAction: vi.fn(),
  batchTransitionAction: vi.fn(),
  deleteApplicationAction: vi.fn(),
}));

vi.mock("@/lib/actions/set-applications-view-action", () => ({
  setApplicationsViewAction: vi.fn(),
}));

vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({ back: vi.fn(), push: vi.fn(), replace: vi.fn() }),
    redirect: (url: string) => {
      throw new Error(`NEXT_REDIRECT:${url}`);
    },
  };
});

async function renderPage() {
  return render(await AnsokningarPage());
}

// #1827 M6: the hero and "Ny ansökan" work without the list, so a failed read keeps them and
// shows the error under them, in the block the detail page uses.
describe("/ansokningar — a failed list read keeps the hero (#1827 M6)", () => {
  beforeEach(() => {
    getServerSession.mockReset();
    getPipeline.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
  });

  it("an error shows the load error under the hero, with Ny ansökan still offered", async () => {
    getPipeline.mockResolvedValue({ kind: "error" });
    const { container } = await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Mina ansökningar" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ny ansökan" })).toHaveAttribute(
      "href",
      "/ny-ansokan",
    );
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte ladda ansökningar");
    expect(alert).toHaveTextContent(
      "Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.",
    );
    // Under the hero: the page's content column carries it.
    expect(container.querySelector(".jp-container.jp-page")).toContainElement(alert);
  });

  it("a 429 shows the rate-limit text under the hero", async () => {
    getPipeline.mockResolvedValue({ kind: "rateLimited", retryAfterSeconds: 30 });
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Mina ansökningar" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ny ansökan" })).toBeInTheDocument();
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("För många förfrågningar");
    expect(alert).toHaveTextContent("Försök igen om 30 sekunder.");
  });
});

// #1827 Minor 14: "Ny ansökan" stands in the hero, so the empty state offers one step, finding
// a job, as its primary button.
describe("/ansokningar — the empty page (#1827 Minor 14)", () => {
  beforeEach(() => {
    getServerSession.mockReset();
    getPipeline.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
  });

  it("offers 'Sök jobb' as its one primary action, beside the hero's 'Ny ansökan'", async () => {
    getPipeline.mockResolvedValue({ kind: "ok", data: [] });
    const { container } = await renderPage();

    const empty = container.querySelector(".jp-empty")!;
    expect(empty).toHaveTextContent("Inga ansökningar ännu");
    const actions = empty.querySelectorAll("a");
    expect(actions).toHaveLength(1);
    expect(actions[0]).toHaveAccessibleName("Sök jobb");
    expect(actions[0]).toHaveAttribute("href", "/jobb");
    expect(actions[0]).toHaveClass("jp-btn", "jp-btn--primary");
    expect(screen.getByRole("link", { name: "Ny ansökan" })).toBeInTheDocument();
  });
});
