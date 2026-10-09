import type { ReactNode } from "react";
import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import svMessages from "../../../messages/sv";
import { useFeedbackSession } from "@/components/feedback/feedback-session";
import type { FeedbackPromptState } from "@/lib/dto/feedback";
import AppLayout from "./layout";

vi.mock("next/navigation", () => ({
  redirect: (path: string) => {
    throw new Error(`REDIRECT:${path}`);
  },
  usePathname: () => "/jobb",
}));

vi.mock("next-intl/server", () => ({
  getLocale: async () => "sv",
  getMessages: async () => svMessages,
  getTranslations: async () => (key: string) => key,
}));

// The shell's own tree is not what is measured here; it renders the page and nothing else.
vi.mock("@/components/shell/app-shell", () => ({
  AppShell: ({ children }: { children: ReactNode }) => <main>{children}</main>,
}));
vi.mock("@/components/site/reloaded-after-update-notice", () => ({ ReloadedAfterUpdateNotice: () => null }));
vi.mock("@/components/applications/application-toast-host", () => ({ ApplicationToastHost: () => null }));
// The real footer reads the request catalogue; this stand-in shows where the slot lands.
vi.mock("@/components/site/site-footer", () => ({
  SiteFooter: ({ feedbackSlot }: { feedbackSlot?: ReactNode }) => (
    <footer>
      <ul aria-label="Stöd och guider">{feedbackSlot}</ul>
    </footer>
  ),
}));

vi.mock("@/lib/api/landing", () => ({ fetchLandingStats: async () => null }));
vi.mock("@/lib/env", () => ({ env: { APP_VERSION: "0123abc" } }));

const feedback = vi.hoisted(() => ({
  state: { kind: "unavailable" } as FeedbackPromptState,
  getFeedbackPromptState: vi.fn(),
}));
vi.mock("@/lib/api/feedback", () => ({ getFeedbackPromptState: feedback.getFeedbackPromptState }));

type Session = { userId: string; email: string; roles: string[] } | null;
const session = vi.hoisted(() => ({ current: null as Session }));
vi.mock("@/lib/auth/session", () => ({
  ROLES: { Admin: "Admin" },
  getServerSession: async () => session.current,
}));

beforeEach(() => {
  session.current = { userId: "u-1", email: "person@example.test", roles: [] };
  feedback.getFeedbackPromptState.mockReset();
  feedback.getFeedbackPromptState.mockImplementation(async () => feedback.state);
});

/** A page that reports what the layout's feedback session says. */
function SessionProbe() {
  const current = useFeedbackSession();
  if (current === null) return <p>no session</p>;
  return (
    <p>
      {`open=${current.open} version=${current.renderedVersion} jobs=${current.isAnswered("jobs")} matches=${current.isAnswered("matches")}`}
    </p>
  );
}

describe("(app)/layout — feedback (#1979 PR3)", () => {
  it("starts the prompt-state read beside the session read, before the session gate", async () => {
    session.current = null;

    await expect(AppLayout({ children: null, modal: null })).rejects.toThrow(/^REDIRECT:\/logga-in$/);
    expect(feedback.getFeedbackPromptState).toHaveBeenCalledTimes(1);
  });

  it("gives the page a feedback session with the prompt state and the rendered version", async () => {
    feedback.state = { kind: "open", answered: ["jobs"] };

    render(await AppLayout({ children: <SessionProbe />, modal: null }));

    expect(screen.getByText("open=true version=0123abc jobs=true matches=false")).toBeInTheDocument();
  });

  it("passes the footer's feedback button into the footer slot, labelled from the client catalogue", async () => {
    feedback.state = { kind: "open", answered: [] };

    render(await AppLayout({ children: null, modal: null }));

    const slot = screen.getByRole("list", { name: "Stöd och guider" });
    // The label resolving at all shows the layout's own client provider carries `feedback`.
    expect(slot).toContainElement(screen.getByRole("button", { name: "Lämna feedback om sidan" }));
  });

  it.each<FeedbackPromptState>([{ kind: "closed" }, { kind: "unavailable" }])(
    "shows no feedback control while the prompt state is $kind",
    async (state) => {
      feedback.state = state;

      render(await AppLayout({ children: <SessionProbe />, modal: null }));

      expect(screen.queryByRole("button", { name: "Lämna feedback om sidan" })).toBeNull();
      expect(screen.getByText(/^open=false /)).toBeInTheDocument();
    },
  );
});
