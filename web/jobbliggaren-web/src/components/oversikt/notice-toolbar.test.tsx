import { describe, it, expect, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NoticeToolbar } from "./notice-toolbar";
import type { SectionNoticeData } from "./notice-section";

const DISMISS_KEY = "jp-oversikt-dismissed-notices";
const PREFS_KEY = "jp-oversikt-notice-prefs";

function notice(overrides: Partial<SectionNoticeData> = {}): SectionNoticeData {
  return {
    id: "n-1",
    source: "jobads",
    type: "matches",
    kind: "info",
    label: "Matchning",
    text: "En notis.",
    cta: "Visa",
    href: "/jobb",
    time: "i dag",
    ...overrides,
  };
}

describe("NoticeToolbar", () => {
  beforeEach(() => window.localStorage.clear());

  it("renderar 'senast uppdaterad'-stämpeln", () => {
    render(<NoticeToolbar lastUpdated="2026-07-19" notices={[]} />);
    expect(screen.getByText(/senast uppdaterad/)).toBeInTheDocument();
    expect(screen.getByText("2026-07-19")).toBeInTheDocument();
  });

  it("döljer 'Markera alla' när inget avfärdbart är synligt", () => {
    render(<NoticeToolbar lastUpdated="x" notices={[]} />);
    expect(
      screen.queryByRole("button", { name: /Markera alla/ }),
    ).toBeNull();
  });

  it("visar 'Markera alla' och avfärdar alla synliga vid klick", async () => {
    const user = userEvent.setup();
    render(
      <NoticeToolbar
        lastUpdated="x"
        notices={[
          notice({ id: "a" }),
          notice({ id: "b", source: "applications", type: "followup" }),
        ]}
      />,
    );

    await user.click(
      screen.getByRole("button", { name: /Markera alla som lästa/ }),
    );

    const stored = JSON.parse(
      window.localStorage.getItem(DISMISS_KEY) ?? "[]",
    );
    expect(stored).toContain("a");
    expect(stored).toContain("b");
    // Inget avfärdbart kvar → knappen försvinner.
    expect(
      screen.queryByRole("button", { name: /Markera alla/ }),
    ).toBeNull();
  });

  it("räknar inte en pref-avstängd typ som synlig", () => {
    window.localStorage.setItem(
      PREFS_KEY,
      JSON.stringify({ "jobads:matches": false }),
    );
    render(<NoticeToolbar lastUpdated="x" notices={[notice({ id: "a" })]} />);
    expect(
      screen.queryByRole("button", { name: /Markera alla/ }),
    ).toBeNull();
  });

  it("räknar inte en icke-avfärdbar notis", () => {
    render(
      <NoticeToolbar
        lastUpdated="x"
        notices={[notice({ id: "a", dismissible: false })]}
      />,
    );
    expect(
      screen.queryByRole("button", { name: /Markera alla/ }),
    ).toBeNull();
  });
});
