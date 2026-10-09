import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FeedbackPromptState } from "@/lib/dto/feedback";
import { FeedbackFooterButton } from "./feedback-footer-button";
import { FeedbackSessionProvider } from "./feedback-session";
import { PageFeedback } from "./page-feedback";

const nav = vi.hoisted(() => ({ pathname: "/jobb" }));
vi.mock("next/navigation", () => ({ usePathname: () => nav.pathname }));

const fetchMock = vi.fn<typeof fetch>();
beforeEach(() => {
  nav.pathname = "/jobb";
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

const OPEN: FeedbackPromptState = { kind: "open", answered: [] };
const OPEN_BUTTON = { name: "Lämna feedback om sidan" };
const COMMENT = { name: "Kommentar (valfri)" };

function Footer({ state = OPEN, withRow = false }: { state?: FeedbackPromptState; withRow?: boolean }) {
  return (
    <FeedbackSessionProvider state={state} renderedVersion="abc1234">
      {withRow && <PageFeedback pageKey="jobs" />}
      <ul>
        <FeedbackFooterButton />
      </ul>
    </FeedbackSessionProvider>
  );
}

describe("FeedbackFooterButton", () => {
  it("renders nothing outside the signed-in layout", () => {
    const { container } = render(<FeedbackFooterButton />);
    expect(container).toBeEmptyDOMElement();
  });

  it.each(["/cv/ny", "/foretag", "/okand-sida"])("renders nothing on %s, which has no page key", (pathname) => {
    nav.pathname = pathname;
    render(<Footer />);
    expect(screen.getByRole("list")).toBeEmptyDOMElement();
  });

  it("renders nothing while feedback is closed", () => {
    render(<Footer state={{ kind: "closed" }} />);
    expect(screen.getByRole("list")).toBeEmptyDOMElement();
  });

  it("is a button in its own list item, also once the page has been answered", () => {
    render(<Footer state={{ kind: "open", answered: ["jobs"] }} />);
    const button = screen.getByRole("button", OPEN_BUTTON);
    expect(button.closest("li")).not.toBeNull();
    expect(button).toHaveClass("jp-foot__linkbtn");
  });

  it("opens a dialog with the form, and Escape returns focus to the button", async () => {
    render(<Footer />);
    const user = userEvent.setup();
    const button = screen.getByRole("button", OPEN_BUTTON);

    await user.click(button);

    const dialog = screen.getByRole("dialog", { name: "Feedback om sidan" });
    expect(within(dialog).getByRole("group", { name: "Hur fungerar den här sidan för dig?" })).toBeVisible();
    // Comment-only feedback is allowed here: the fields are there before a star is chosen.
    expect(within(dialog).getByRole("textbox", COMMENT)).toBeVisible();

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(button).toHaveFocus());
  });

  it("keeps the draft over close and reopen", async () => {
    render(<Footer />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", OPEN_BUTTON));
    await user.click(screen.getByRole("radio", { name: "2 av 5" }));
    await user.type(screen.getByRole("textbox", COMMENT), "Sparat utkast");
    await user.keyboard("{Escape}");

    await user.click(screen.getByRole("button", OPEN_BUTTON));

    expect(screen.getByRole("textbox", COMMENT)).toHaveValue("Sparat utkast");
    expect(screen.getByRole("radio", { name: "2 av 5" })).toBeChecked();
  });

  it("starts over when the page key changes, and keeps the draft between two routes of one key", async () => {
    nav.pathname = "/mina-sidor/konto";
    const { rerender } = render(<Footer />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", OPEN_BUTTON));
    await user.type(screen.getByRole("textbox", COMMENT), "Om kontot");
    await user.keyboard("{Escape}");

    nav.pathname = "/mina-sidor/notiser";
    rerender(<Footer />);
    await user.click(screen.getByRole("button", OPEN_BUTTON));
    expect(screen.getByRole("textbox", COMMENT)).toHaveValue("Om kontot");
    await user.keyboard("{Escape}");

    nav.pathname = "/jobb";
    rerender(<Footer />);
    await user.click(screen.getByRole("button", OPEN_BUTTON));
    expect(screen.getByRole("textbox", COMMENT)).toHaveValue("");
  });

  it("shows the receipt in the dialog, hides the page's untouched row, and opens empty next time", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ outcome: "saved" }), { status: 200 }));
    render(<Footer withRow />);
    const user = userEvent.setup();
    expect(screen.getAllByRole("group", { name: "Hur fungerar den här sidan för dig?" })).toHaveLength(1);

    await user.click(screen.getByRole("button", OPEN_BUTTON));
    const dialog = screen.getByRole("dialog");
    await user.type(within(dialog).getByRole("textbox", COMMENT), "Bara text");
    await user.click(within(dialog).getByRole("button", { name: "Skicka feedback" }));

    const receipt = await within(dialog).findByText("Tack. Din feedback är sparad.");
    await waitFor(() => expect(receipt).toHaveFocus());
    expect(JSON.parse((fetchMock.mock.calls[0]![1]!.body as FormData).get("payload") as string)).toMatchObject({
      page: "jobs",
      comment: "Bara text",
    });

    await user.keyboard("{Escape}");
    // The inline row had no draft, so the answered page no longer asks.
    expect(screen.queryByRole("group", { name: "Hur fungerar den här sidan för dig?" })).toBeNull();

    await user.click(screen.getByRole("button", OPEN_BUTTON));
    expect(screen.getByRole("textbox", COMMENT)).toHaveValue("");
  });

  it("does not hide the page's row while that row holds a draft", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ outcome: "saved" }), { status: 200 }));
    render(<Footer withRow />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("radio", { name: "4 av 5" }));

    await user.click(screen.getByRole("button", OPEN_BUTTON));
    const dialog = screen.getByRole("dialog");
    await user.type(within(dialog).getByRole("textbox", COMMENT), "Från sidfoten");
    await user.click(within(dialog).getByRole("button", { name: "Skicka feedback" }));
    await within(dialog).findByText("Tack. Din feedback är sparad.");
    await user.keyboard("{Escape}");

    expect(screen.getByRole("radio", { name: "4 av 5" })).toBeChecked();
  });
});
