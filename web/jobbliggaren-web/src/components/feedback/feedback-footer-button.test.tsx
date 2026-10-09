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
const OPEN_BUTTON = { name: "Lämna feedback" };
const DIALOG = { name: "Feedback om Jobbliggaren" };
const GENERAL_QUESTION = { name: "Hur fungerar Jobbliggaren för dig?" };
const PAGE_QUESTION = { name: "Hur fungerar sidan Jobb för dig?" };
const COMMENT = { name: "Kommentar (valfri)" };
const RECEIPT = "Tack. Din feedback är sparad.";

const saved = () => new Response(JSON.stringify({ outcome: "saved" }), { status: 200 });

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

  it("renders nothing while feedback is closed", () => {
    render(<Footer state={{ kind: "closed" }} />);
    expect(screen.getByRole("list")).toBeEmptyDOMElement();
  });

  it("is a button in its own list item, also on a route without a page and once pages have been answered", () => {
    nav.pathname = "/cv/ny";
    render(<Footer state={{ kind: "open", answered: ["jobs", "cv"] }} />);
    const button = screen.getByRole("button", OPEN_BUTTON);
    expect(button.closest("li")).not.toBeNull();
    expect(button).toHaveClass("jp-foot__linkbtn");
  });

  it("opens a dialog for general feedback, and Escape returns focus to the button", async () => {
    render(<Footer />);
    const user = userEvent.setup();
    const button = screen.getByRole("button", OPEN_BUTTON);

    await user.click(button);

    const dialog = screen.getByRole("dialog", DIALOG);
    expect(within(dialog).getByRole("group", GENERAL_QUESTION)).toBeVisible();
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

  it("keeps one draft for the visit when the route changes", async () => {
    nav.pathname = "/mina-sidor/konto";
    const { rerender } = render(<Footer />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", OPEN_BUTTON));
    await user.type(screen.getByRole("textbox", COMMENT), "Om tjänsten");
    await user.keyboard("{Escape}");

    nav.pathname = "/jobb";
    rerender(<Footer />);
    await user.click(screen.getByRole("button", OPEN_BUTTON));

    expect(screen.getByRole("textbox", COMMENT)).toHaveValue("Om tjänsten");
  });

  it("sends as general feedback, shows the receipt in the dialog, and opens empty next time", async () => {
    fetchMock.mockResolvedValue(saved());
    render(<Footer />);
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", OPEN_BUTTON));
    const dialog = screen.getByRole("dialog", DIALOG);
    await user.type(within(dialog).getByRole("textbox", COMMENT), "Bara text");
    await user.click(within(dialog).getByRole("button", { name: "Skicka feedback" }));

    const receipt = await within(dialog).findByText(RECEIPT);
    await waitFor(() => expect(receipt).toHaveFocus());
    const body = fetchMock.mock.calls[0]![1]!.body as FormData; // The form always posts a FormData body.
    expect(JSON.parse(body.get("payload") as string)).toMatchObject({ page: "general", comment: "Bara text" });

    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", OPEN_BUTTON));
    expect(screen.getByRole("textbox", COMMENT)).toHaveValue("");
  });

  it("does not answer the page: its row still asks after general feedback is saved", async () => {
    fetchMock.mockResolvedValue(saved());
    render(<Footer withRow />);
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", OPEN_BUTTON));
    const dialog = screen.getByRole("dialog", DIALOG);
    await user.type(within(dialog).getByRole("textbox", COMMENT), "Om tjänsten");
    await user.click(within(dialog).getByRole("button", { name: "Skicka feedback" }));
    await within(dialog).findByText(RECEIPT);
    await user.keyboard("{Escape}");

    expect(screen.getByRole("group", PAGE_QUESTION)).toBeVisible();
  });

  it("keeps the page's row while it shows its confirmation", async () => {
    fetchMock.mockImplementation(async () => saved());
    render(<Footer withRow />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("radio", { name: "4 av 5" }));
    await screen.findByText("Tack för ditt betyg.");

    await user.click(screen.getByRole("button", OPEN_BUTTON));
    const dialog = screen.getByRole("dialog", DIALOG);
    // The footer's draft is its own: the row's rating is not carried over.
    expect(within(dialog).getAllByRole("radio").some((radio) => (radio as HTMLInputElement).checked)).toBe(false);
    await user.type(within(dialog).getByRole("textbox", COMMENT), "Från sidfoten");
    await user.click(within(dialog).getByRole("button", { name: "Skicka feedback" }));
    await within(dialog).findByText(RECEIPT);
    await user.keyboard("{Escape}");

    expect(screen.getByText("Tack för ditt betyg.")).toBeVisible();
  });
});
