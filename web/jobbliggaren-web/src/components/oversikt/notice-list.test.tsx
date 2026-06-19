import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NoticeList } from "./notice-list";
import type { NoticeData } from "./notice-row";

// next/link renderas som <a> i jsdom utan extra mock (Next client Link).

const dismissibleNotice: NoticeData = {
  id: "n-dismissible",
  kind: "info",
  label: "Sparad sökning",
  text: "En avfärdbar notis.",
  cta: "Visa",
  href: "/sokningar",
  time: "i går",
};

const nonDismissibleNudge: NoticeData = {
  id: "n-setup-match",
  kind: "info",
  dismissible: false,
  label: "Matchning",
  text: "Du har inte angett vilka yrken du söker inom.",
  cta: "Ställ in matchning",
  href: "/installningar#matchning",
  time: "",
};

describe("NoticeList — icke-avfärdbara notiser (F4-12 PR-B)", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("en icke-avfärdbar notis renderar INGEN dismiss-knapp (X)", () => {
    render(
      <NoticeList
        actionNotices={[]}
        infoNotices={[nonDismissibleNudge]}
        lastUpdated="2026-06-19"
      />
    );

    const nudge = screen
      .getByText("Du har inte angett vilka yrken du söker inom.")
      .closest("li") as HTMLElement;
    expect(
      within(nudge).queryByRole("button", { name: "Markera som läst" })
    ).toBeNull();
  });

  it("en avfärdbar notis renderar en dismiss-knapp (X)", () => {
    render(
      <NoticeList
        actionNotices={[]}
        infoNotices={[dismissibleNotice]}
        lastUpdated="2026-06-19"
      />
    );

    const row = screen
      .getByText("En avfärdbar notis.")
      .closest("li") as HTMLElement;
    expect(
      within(row).getByRole("button", { name: "Markera som läst" })
    ).toBeInTheDocument();
  });

  it("'Markera alla som lästa' lämnar den icke-avfärdbara notisen synlig", async () => {
    const user = userEvent.setup();
    render(
      <NoticeList
        actionNotices={[]}
        infoNotices={[dismissibleNotice, nonDismissibleNudge]}
        lastUpdated="2026-06-19"
      />
    );

    await user.click(
      screen.getByRole("button", { name: /Markera alla som lästa/ })
    );

    // Den avfärdbara försvinner, nudgen är kvar.
    expect(screen.queryByText("En avfärdbar notis.")).toBeNull();
    expect(
      screen.getByText("Du har inte angett vilka yrken du söker inom.")
    ).toBeInTheDocument();
  });

  it("'Markera alla som lästa' saknas när enda synliga notisen är icke-avfärdbar", () => {
    render(
      <NoticeList
        actionNotices={[]}
        infoNotices={[nonDismissibleNudge]}
        lastUpdated="2026-06-19"
      />
    );

    expect(
      screen.queryByRole("button", { name: /Markera alla som lästa/ })
    ).toBeNull();
  });

  it("'Markera alla som lästa' visas när minst en avfärdbar synlig notis finns", () => {
    render(
      <NoticeList
        actionNotices={[]}
        infoNotices={[dismissibleNotice, nonDismissibleNudge]}
        lastUpdated="2026-06-19"
      />
    );

    expect(
      screen.getByRole("button", { name: /Markera alla som lästa/ })
    ).toBeInTheDocument();
  });

  it("tom time → ingen tids-span renderas på raden", () => {
    render(
      <NoticeList
        actionNotices={[]}
        infoNotices={[nonDismissibleNudge]}
        lastUpdated="2026-06-19"
      />
    );

    const nudge = screen
      .getByText("Du har inte angett vilka yrken du söker inom.")
      .closest("li") as HTMLElement;
    expect(nudge.querySelector(".jp-notice__time")).toBeNull();
  });
});
