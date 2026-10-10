import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FeedbackPromptState, FeedbackSubmitOutcome } from "@/lib/dto/feedback";
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
const QUESTION = { name: "Hur fungerar sidan Jobb för dig?" };
const RATED = "Tack för ditt betyg.";
const RECEIPT = "Tack. Din feedback är sparad.";
const MORE = { name: "Lämna mer feedback" };
const CLOSE = { name: "Stäng" };
/** The text on the page, not its copy in the live region. */
const SHOWN = { ignore: '[role="status"], script, style' };
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

const answer = (outcome: FeedbackSubmitOutcome, status = 200) =>
  new Response(JSON.stringify(outcome), { status, headers: { "content-type": "application/json" } });

function sent(index: number): { payload: Record<string, unknown>; screenshot: FormDataEntryValue | null } {
  const body = fetchMock.mock.calls[index]![1]!.body as FormData; // The form always posts a FormData body.
  return { payload: JSON.parse(body.get("payload") as string) as Record<string, unknown>, screenshot: body.get("screenshot") };
}

const star = (rating: number) => screen.getByRole("button", { name: `${rating} av 5` });
const filledStars = () => [1, 2, 3, 4, 5].filter((rating) => star(rating).querySelector("svg")?.getAttribute("fill") === "currentColor");
const status = () => screen.getByRole("status");

function Page({ state }: { state: FeedbackPromptState }) {
  return (
    <FeedbackSessionProvider state={state} renderedVersion="abc1234">
      <PageFeedback pageKey="jobs" />
    </FeedbackSessionProvider>
  );
}

describe("PageFeedback and the rating row — asking", () => {
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

  it("asks with the question and the stars alone", () => {
    render(<Page state={{ kind: "open", answered: ["matches"] }} />);
    expect(screen.getByRole("group", QUESTION)).toBeVisible();
    expect(screen.getAllByRole("button").map((button) => button.getAttribute("aria-label"))).toEqual([
      "1 av 5",
      "2 av 5",
      "3 av 5",
      "4 av 5",
      "5 av 5",
    ]);
    expect(screen.queryByRole("radio")).toBeNull();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
    // The live region is there before anything is said in it.
    expect(status()).toHaveTextContent("");
  });

  it("names its own page in the question, since ratings are kept per page", () => {
    render(
      <FeedbackSessionProvider state={OPEN} renderedVersion={null}>
        <PageFeedback pageKey="saved-ads" />
      </FeedbackSessionProvider>,
    );
    expect(screen.getByRole("group", { name: "Hur fungerar sidan Sparade annonser för dig?" })).toBeVisible();
  });

  it("owns the page-width container in its own markup", () => {
    const { container } = render(<Page state={OPEN} />);
    expect(container.querySelector("section.jp-container.jp-feedback")).not.toBeNull();
  });
});

describe("PageFeedback and the rating row — a star is the answer", () => {
  it("sends the chosen star at once, as a rating alone", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    render(<Page state={OPEN} />);
    const user = userEvent.setup();

    await user.click(star(4));

    await screen.findByText(RATED, SHOWN);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0]![0]).toBe("/api/feedback");
    const { payload, screenshot } = sent(0);
    expect(payload).toEqual({ submissionKey: expect.stringMatching(UUID), page: "jobs", rating: 4, renderedVersion: "abc1234" });
    expect(screenshot).toBeNull();
  });

  it("replaces the stars with a confirmation that takes focus, with a way to more feedback and a close", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    render(<Page state={OPEN} />);
    const user = userEvent.setup();

    await user.click(star(5));

    const confirmation = await screen.findByText(RATED, SHOWN);
    await waitFor(() => expect(confirmation).toHaveFocus());
    expect(screen.queryByRole("group", QUESTION)).toBeNull();
    expect(screen.getByRole("button", MORE)).toBeVisible();
    expect(screen.getByRole("button", CLOSE)).toBeVisible();
  });

  it("says it is sending, keeps the stars, ignores a second star, and stays if the session closes meanwhile", async () => {
    let reply: (response: Response) => void = () => {};
    fetchMock.mockReturnValue(new Promise<Response>((resolve) => (reply = resolve)));
    const { rerender } = render(<Page state={OPEN} />);
    const user = userEvent.setup();

    await user.click(star(5));
    expect(status()).toHaveTextContent("Skickar…");
    expect(screen.getByText("Skickar…", SHOWN)).toBeVisible();
    expect(filledStars()).toEqual([1, 2, 3, 4, 5]);

    await user.click(star(3));
    expect(fetchMock).toHaveBeenCalledTimes(1);

    rerender(<Page state={{ kind: "closed" }} />);
    expect(screen.getByRole("group", QUESTION)).toBeVisible();

    await act(async () => reply(answer({ outcome: "saved" })));
    expect(await screen.findByText(RATED, SHOWN)).toBeVisible();
    expect(sent(0).payload).toMatchObject({ rating: 5 });
  });

  it("reaches the stars with Tab, fills each one it stands on without sending, and sends that star with Enter", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    render(<Page state={OPEN} />);
    const user = userEvent.setup();

    await user.tab();
    await user.tab();
    await user.tab();
    expect(star(3)).toHaveFocus();
    expect(filledStars()).toEqual([1, 2, 3]);
    expect(fetchMock).not.toHaveBeenCalled();

    await user.keyboard("{Enter}");

    await screen.findByText(RATED, SHOWN);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(sent(0).payload).toMatchObject({ rating: 3 });
  });

  it("sends the star that has focus with Space as well", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    render(<Page state={OPEN} />);
    const user = userEvent.setup();

    await user.tab();
    await user.tab();
    await user.keyboard(" ");

    await screen.findByText(RATED, SHOWN);
    expect(sent(0).payload).toMatchObject({ rating: 2 });
  });

  it("marks the page answered for the visit, so another row for it does not ask again", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
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
    await user.click(star(3));
    await screen.findByText(RATED, SHOWN);

    rerender(<TwoRows second />);

    // The confirmation stays on the row that sent; the new row for the same page asks nothing.
    expect(screen.getByText(RATED, SHOWN)).toBeVisible();
    expect(screen.queryByRole("group", QUESTION)).toBeNull();
  });
});

describe("PageFeedback and the rating row — after the rating", () => {
  async function rated(rating: number) {
    fetchMock.mockResolvedValueOnce(answer({ outcome: "saved" }));
    const view = render(<Page state={OPEN} />);
    const user = userEvent.setup();
    await user.click(star(rating));
    await screen.findByText(RATED, SHOWN);
    return { user, ...view };
  }

  it("keeps the confirmation if the session closes meanwhile", async () => {
    const { rerender } = await rated(2);

    rerender(<Page state={{ kind: "closed" }} />);

    expect(screen.getByText(RATED, SHOWN)).toBeVisible();
  });

  it("opens the whole form from Lämna mer feedback with the rating already chosen, and sends it as a new submission", async () => {
    const { user } = await rated(4);
    fetchMock.mockResolvedValueOnce(answer({ outcome: "saved" }));

    await user.click(screen.getByRole("button", MORE));

    const dialog = screen.getByRole("dialog", { name: "Feedback om sidan Jobb" });
    expect(within(dialog).getByRole("group", QUESTION)).toBeVisible();
    expect(within(dialog).getByRole("radio", { name: "4 av 5" })).toBeChecked();
    // The rating is saved already; the form that carries it cannot take it back.
    expect(within(dialog).queryByRole("button", { name: "Ta bort betyget" })).toBeNull();
    await user.type(within(dialog).getByRole("textbox", { name: "Kommentar (valfri)" }), "Mer om sidan");
    await user.click(within(dialog).getByRole("button", { name: "Skicka feedback" }));

    const receipt = await within(dialog).findByText(RECEIPT);
    await waitFor(() => expect(receipt).toHaveFocus());
    expect(sent(1).payload).toMatchObject({ page: "jobs", rating: 4, comment: "Mer om sidan" });
    expect(sent(1).payload.submissionKey).not.toBe(sent(0).payload.submissionKey);

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(screen.getByRole("button", MORE)).toHaveFocus());
    expect(screen.getByText(RATED, SHOWN)).toBeVisible();

    // A saved answer has done its work: the next opening starts empty.
    await user.click(screen.getByRole("button", MORE));
    const reopened = screen.getByRole("dialog");
    expect(within(reopened).getAllByRole("radio").some((radio) => (radio as HTMLInputElement).checked)).toBe(false);
    // Empty again, so a star chosen now is the form's own and can be taken back.
    await user.click(within(reopened).getByRole("radio", { name: "2 av 5" }));
    expect(within(reopened).getByRole("button", { name: "Ta bort betyget" })).toBeVisible();
  });

  it("keeps the dialog's draft over close and reopen", async () => {
    const { user } = await rated(2);
    await user.click(screen.getByRole("button", MORE));
    await user.type(screen.getByRole("textbox", { name: "Kommentar (valfri)" }), "Utkast");
    await user.keyboard("{Escape}");

    await user.click(screen.getByRole("button", MORE));

    expect(screen.getByRole("textbox", { name: "Kommentar (valfri)" })).toHaveValue("Utkast");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("moves focus to the next stop in reading order before the row goes, the footer's first control", async () => {
    fetchMock.mockResolvedValueOnce(answer({ outcome: "saved" }));
    render(
      <>
        <main>
          <h1>Jobb</h1>
          <FeedbackSessionProvider state={OPEN} renderedVersion={null}>
            <PageFeedback pageKey="jobs" />
          </FeedbackSessionProvider>
        </main>
        <footer>
          <a href="/kontakt">Kontakt</a>
        </footer>
      </>,
    );
    const user = userEvent.setup();
    await user.click(star(5));
    await screen.findByText(RATED, SHOWN);

    await user.click(screen.getByRole("button", CLOSE));

    expect(screen.queryByText(RATED, SHOWN)).toBeNull();
    const next = screen.getByRole("link", { name: "Kontakt" });
    expect(next).toHaveFocus();
    // Focus was moved to an ordinary stop; the control is not made unreachable by Tab.
    expect(next).not.toHaveAttribute("tabindex");
  });

  it("removes the row for the rest of the visit when it is closed", async () => {
    const { user, container, rerender } = await rated(5);

    await user.click(screen.getByRole("button", CLOSE));

    expect(container).toBeEmptyDOMElement();
    rerender(<Page state={{ kind: "open", answered: [] }} />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe("PageFeedback and the rating row — refusals", () => {
  it.each<[string, FeedbackSubmitOutcome, string, boolean]>([
    ["closed", { outcome: "closed" }, "Det går inte att skicka feedback just nu.", false],
    ["busy", { outcome: "busy" }, "Det går inte att skicka just nu. Försök igen om en stund.", false],
    [
      "rateLimited",
      { outcome: "rateLimited", retryAfterSeconds: 120 },
      "Du har skickat många svar på kort tid. Försök igen om 2 minuter.",
      false,
    ],
    ["refused invalid", { outcome: "refused", reason: "invalid" }, "Feedbacken kunde inte skickas. Ladda om sidan och försök igen.", true],
  ])("tells the %s answer under the stars and keeps the rating", async (_name, outcome, text, error) => {
    fetchMock.mockResolvedValue(answer(outcome, 400));
    render(<Page state={OPEN} />);
    const user = userEvent.setup();

    await user.click(star(3));

    const message = await screen.findByText(text, SHOWN);
    expect(message).toBeVisible();
    expect(message).toHaveClass("jp-feedback__message");
    expect(message.classList.contains("jp-feedback__message--error")).toBe(error);
    expect(status()).toHaveTextContent(text);
    // The click left the pointer and focus on star 3; with both away, the fill is the kept rating.
    await user.click(document.body);
    expect(filledStars()).toEqual([1, 2, 3]);
    expect(screen.queryByRole("button", { name: "Skicka igen" })).toBeNull();
  });

  it("sends again when a star is chosen again: the same star under the same key, another under a new one", async () => {
    fetchMock
      .mockResolvedValueOnce(answer({ outcome: "busy" }, 409))
      .mockResolvedValueOnce(answer({ outcome: "busy" }, 409))
      .mockResolvedValueOnce(answer({ outcome: "saved" }));
    render(<Page state={OPEN} />);
    const user = userEvent.setup();
    await user.click(star(3));
    await screen.findByText("Det går inte att skicka just nu. Försök igen om en stund.", SHOWN);

    await user.click(star(3));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    await screen.findByText("Det går inte att skicka just nu. Försök igen om en stund.", SHOWN);
    await user.click(star(4));

    await screen.findByText(RATED, SHOWN);
    expect(sent(1).payload.submissionKey).toBe(sent(0).payload.submissionKey);
    expect(sent(2).payload).toMatchObject({ rating: 4 });
    expect(sent(2).payload.submissionKey).not.toBe(sent(0).payload.submissionKey);
  });

  it("offers Skicka igen after an unknown answer, which sends the same rating under the same key and keeps focus while it waits", async () => {
    let reply: (response: Response) => void = () => {};
    fetchMock
      .mockResolvedValueOnce(answer({ outcome: "unknown" }, 502))
      .mockReturnValueOnce(new Promise<Response>((resolve) => (reply = resolve)));
    render(<Page state={OPEN} />);
    const user = userEvent.setup();
    await user.click(star(2));

    const text = "Vi vet inte om din feedback sparades. Skicka igen, så sparas den bara en gång.";
    expect(await screen.findByText(text, SHOWN)).toBeVisible();
    const again = screen.getByRole("button", { name: "Skicka igen" });
    expect(again).toHaveAccessibleDescription(text);

    await user.click(again);
    const waiting = screen.getByRole("button", { name: "Skickar…" });
    expect(waiting).toHaveAttribute("aria-disabled", "true");
    expect(waiting).toHaveFocus();
    await user.click(waiting);
    expect(fetchMock).toHaveBeenCalledTimes(2);

    await act(async () => reply(answer({ outcome: "saved" })));
    const confirmation = await screen.findByText(RATED, SHOWN);
    await waitFor(() => expect(confirmation).toHaveFocus());
    expect(sent(1).payload).toEqual(sent(0).payload);
  });

  it("offers a way back in when the session has ended", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "signedOut" }, 401));
    render(<Page state={OPEN} />);
    const user = userEvent.setup();

    await user.click(star(4));

    const link = await screen.findByRole("link", { name: "Logga in igen" });
    expect(link).toHaveAttribute("href", "/logga-in?next=%2Fjobb");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    expect(link.closest("p")).toHaveTextContent(
      "Du är utloggad. Logga in igen, som öppnas i en ny flik, och skicka sedan feedbacken här.",
    );
  });

  it("goes away with feedback closed while it only shows a refusal", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "busy" }, 409));
    const { container, rerender } = render(<Page state={OPEN} />);
    const user = userEvent.setup();
    await user.click(star(1));
    await screen.findByText("Det går inte att skicka just nu. Försök igen om en stund.", SHOWN);

    rerender(<Page state={{ kind: "closed" }} />);

    expect(container).toBeEmptyDOMElement();
  });
});
