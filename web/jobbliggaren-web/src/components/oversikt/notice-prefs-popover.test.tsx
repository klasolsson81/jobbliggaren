import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NoticePrefsPopover, type NoticePrefGroup } from "./notice-prefs-popover";
import messages from "../../../messages/sv";

const COPY = messages.oversikt.notices;

const groups: NoticePrefGroup[] = [
  {
    source: "applications",
    title: "Mina ansökningar",
    types: [
      { id: "followup", label: "Uppföljning vid uteblivet svar" },
      { id: "interviews", label: "Intervjuer och möten" },
    ],
  },
  {
    source: "jobads",
    title: "Jobbannonser",
    types: [{ id: "matches", label: "Nya matchande annonser" }],
  },
];
const notices = [
  { id: "a", source: "applications" as const, type: "followup" },
  { id: "b", source: "jobads" as const, type: "matches" },
];

beforeEach(() => window.localStorage.clear());

async function open() {
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: COPY.settingsAria }));
  return user;
}

describe("NoticePrefsPopover", () => {
  it("the gear is icon-only with an accessible name, and opens a grouped popover", async () => {
    render(<NoticePrefsPopover groups={groups} notices={notices} />);
    const gear = screen.getByRole("button", { name: COPY.settingsAria });
    expect(gear).toHaveAttribute("aria-haspopup", "true");
    expect(gear).toHaveAttribute("aria-expanded", "false");
    await open();
    const panel = screen.getByRole("group", { name: COPY.settingsAria });
    expect(within(panel).getByText(COPY.settingsHeading)).toBeInTheDocument();
    expect([...panel.querySelectorAll(".jp-notice-prefs__grouptitle")].map((e) => e.textContent)).toEqual([
      "Mina ansökningar",
      "Jobbannonser",
    ]);
    expect(within(panel).getAllByRole("checkbox")).toHaveLength(3);
  });

  it("a single untitled group renders no group title at all — the ledger's markup", async () => {
    render(<NoticePrefsPopover groups={[{ source: "jobads", types: groups[1]!.types }]} notices={[]} />);
    await open();
    expect(document.querySelector(".jp-notice-prefs__grouptitle")).toBeNull();
    expect(screen.getAllByRole("checkbox")).toHaveLength(1);
  });

  it("unchecking a type writes the shared store key in its source:type form", async () => {
    render(<NoticePrefsPopover groups={groups} notices={notices} />);
    const user = await open();
    await user.click(screen.getByRole("checkbox", { name: "Nya matchande annonser" }));
    expect(JSON.parse(window.localStorage.getItem("jp-oversikt-notice-prefs") ?? "{}")).toEqual({
      "jobads:matches": false,
    });
  });

  it("the foot counts the read notices among the ones it was given, restores them in one write, and focuses the first checkbox", async () => {
    window.localStorage.setItem("jp-oversikt-dismissed-notices", JSON.stringify(["a", "b", "elsewhere"]));
    render(<NoticePrefsPopover groups={groups} notices={notices} />);
    const user = await open();
    const reset = screen.getByRole("button", { name: "Återställ lästa notiser (2)" });
    await user.click(reset);

    expect(JSON.parse(window.localStorage.getItem("jp-oversikt-dismissed-notices") ?? "[]")).toEqual([
      "elsewhere",
    ]);
    expect(screen.queryByRole("button", { name: /Återställ lästa notiser/ })).toBeNull();
    expect(document.activeElement).toBe(screen.getAllByRole("checkbox")[0]);
  });

  it("a read notice of a switched-off type is not counted — it is not on the page to restore", async () => {
    window.localStorage.setItem("jp-oversikt-dismissed-notices", JSON.stringify(["a", "b"]));
    window.localStorage.setItem("jp-oversikt-notice-prefs", JSON.stringify({ "jobads:matches": false }));
    render(<NoticePrefsPopover groups={groups} notices={notices} />);
    await open();
    expect(screen.getByRole("button", { name: "Återställ lästa notiser (1)" })).toBeInTheDocument();
  });
});
