import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FeedbackPromptState } from "@/lib/dto/feedback";
import { FeedbackSessionProvider } from "./feedback-session";
import { PageFeedback } from "./page-feedback";

vi.mock("next/navigation", () => ({ usePathname: () => "/jobb" }));

const fetchMock = vi.fn<typeof fetch>();
beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

const OPEN: FeedbackPromptState = { kind: "open", answered: [] };
const QUESTION = { name: "Hur fungerar den här sidan för dig?" };

function Page({ state }: { state: FeedbackPromptState }) {
  return (
    <FeedbackSessionProvider state={state} renderedVersion="abc1234">
      <PageFeedback pageKey="jobs" />
    </FeedbackSessionProvider>
  );
}

describe("PageFeedback and the rating row", () => {
  it("renders nothing outside the signed-in layout", () => {
    const { container } = render(<PageFeedback pageKey="jobs" />);
    expect(container).toBeEmptyDOMElement();
  });

  it.each<[string, FeedbackPromptState]>([
    ["closed", { kind: "closed" }],
    ["unavailable", { kind: "unavailable" }],
    ["already answered", { kind: "open", answered: ["jobs"] }],
  ])("renders nothing when feedback is %s", (_name, state) => {
    const { container } = render(<Page state={state} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("asks on an open, unanswered page with only the question and the stars", () => {
    render(<Page state={{ kind: "open", answered: ["matches"] }} />);
    expect(screen.getByRole("group", QUESTION)).toBeVisible();
    expect(screen.getAllByRole("radio")).toHaveLength(5);
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.queryByRole("button", { name: "Skicka feedback" })).toBeNull();
    // The live region is there before anything is said in it.
    expect(screen.getByRole("status")).toHaveTextContent("");
  });

  it("unfolds the rest of the form below a chosen star without moving focus", async () => {
    render(<Page state={OPEN} />);
    const user = userEvent.setup();
    const star = screen.getByRole("radio", { name: "4 av 5" });

    await user.click(star);

    expect(screen.getByRole("textbox", { name: "Kommentar (valfri)" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Skicka feedback" })).toBeVisible();
    expect(star).toHaveFocus();
  });

  it("stays while it holds a draft, whatever the session says meanwhile (C3)", async () => {
    const { rerender } = render(<Page state={OPEN} />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("radio", { name: "2 av 5" }));
    await user.type(screen.getByRole("textbox", { name: "Kommentar (valfri)" }), "Utkast");

    rerender(<Page state={{ kind: "closed" }} />);
    expect(screen.getByRole("textbox", { name: "Kommentar (valfri)" })).toHaveValue("Utkast");

    rerender(<Page state={{ kind: "open", answered: ["jobs"] }} />);
    expect(screen.getByRole("textbox", { name: "Kommentar (valfri)" })).toHaveValue("Utkast");
  });

  it("stays with a send in flight, then shows the receipt with focus and keeps it after the page counts as answered", async () => {
    let reply: (response: Response) => void = () => {};
    fetchMock.mockReturnValue(new Promise<Response>((resolve) => (reply = resolve)));
    const { rerender } = render(<Page state={OPEN} />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("radio", { name: "5 av 5" }));
    await user.click(screen.getByRole("button", { name: "Skicka feedback" }));

    rerender(<Page state={{ kind: "closed" }} />);
    expect(screen.getByRole("button", { name: "Skickar…" })).toHaveAttribute("aria-disabled", "true");

    await act(async () => reply(new Response(JSON.stringify({ outcome: "saved" }), { status: 200 })));

    const receipt = await screen.findByText("Tack. Din feedback är sparad.");
    await waitFor(() => expect(receipt).toHaveFocus());
    expect(screen.queryByRole("group", QUESTION)).toBeNull();
  });

  it("marks the page answered for the visit, so another row for it does not ask again", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ outcome: "saved" }), { status: 200 }));
    function TwoRows({ second }: { second: boolean }) {
      return (
        <FeedbackSessionProvider state={OPEN} renderedVersion={null}>
          <PageFeedback pageKey="jobs" />
          {second && <PageFeedback pageKey="jobs" />}
        </FeedbackSessionProvider>
      );
    }
    const { rerender } = render(<TwoRows second={false} />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("radio", { name: "3 av 5" }));
    await user.click(screen.getByRole("button", { name: "Skicka feedback" }));
    await screen.findByText("Tack. Din feedback är sparad.");

    rerender(<TwoRows second />);

    // The receipt stays on the row that sent; the new row for the same page asks nothing.
    expect(screen.getByText("Tack. Din feedback är sparad.")).toBeVisible();
    expect(screen.queryByRole("group", QUESTION)).toBeNull();
  });

  it("owns the page-width container in its own markup", () => {
    const { container } = render(<Page state={OPEN} />);
    expect(container.querySelector("section.jp-container.jp-feedback")).not.toBeNull();
  });
});
