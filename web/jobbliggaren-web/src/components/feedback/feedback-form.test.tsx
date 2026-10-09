import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { FeedbackSubmitOutcome } from "@/lib/dto/feedback";
import { readClientContext } from "@/lib/feedback/client-context";
import type { ScreenshotCodec } from "@/lib/feedback/image/prepare";
import { rgbaPng } from "../../../tests/admin/png-encoder";
import { FeedbackForm } from "./feedback-form";
import { useFeedbackForm } from "./use-feedback-form";

vi.mock("next/navigation", () => ({ usePathname: () => "/jobb" }));

// The real reader, observed: it may run only at Send and only with the box ticked.
vi.mock("@/lib/feedback/client-context", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/feedback/client-context")>();
  return { ...actual, readClientContext: vi.fn(actual.readClientContext) };
});

const OriginalURL = URL;
const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  fetchMock.mockReset();
  vi.mocked(readClientContext).mockClear();
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal(
    "URL",
    class extends OriginalURL {
      static createObjectURL = vi.fn(() => "blob:preview");
      static revokeObjectURL = vi.fn();
    },
  );
});
afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const ENCODED = new Blob([new Uint8Array(64)], { type: "image/png" });

/** A codec without a canvas: the image library's own tests measure the plan; this one only answers. */
function fakeCodec({ decode = "ok" }: { decode?: "ok" | "fail" | "pending" } = {}): ScreenshotCodec {
  return {
    decode: () =>
      decode === "pending"
        ? new Promise(() => {})
        : decode === "fail"
          ? Promise.reject(new Error("decode"))
          : Promise.resolve({ width: 4, height: 3, release: () => {} }),
    encodePng: async () => ENCODED,
  };
}

function Harness({
  renderedVersion = "abc1234",
  codec = fakeCodec(),
  onSaved = () => {},
}: {
  renderedVersion?: string | null;
  codec?: ScreenshotCodec;
  onSaved?: () => void;
}) {
  const controller = useFeedbackForm({ page: "jobs", renderedVersion, codec, onSaved });
  return <FeedbackForm controller={controller} />;
}

const answer = (outcome: FeedbackSubmitOutcome, status = 200) =>
  new Response(JSON.stringify(outcome), { status, headers: { "content-type": "application/json" } });

type SentPayload = Record<string, unknown> & { submissionKey: string };

function sent(index: number): { payload: SentPayload; screenshot: FormDataEntryValue | null; url: unknown } {
  const [url, init] = fetchMock.mock.calls[index]!;
  const body = init!.body as FormData; // The form always posts a FormData body.
  return { url, payload: JSON.parse(body.get("payload") as string) as SentPayload, screenshot: body.get("screenshot") };
}

/** The text on the page, not its copy in the live region. */
const SHOWN = { ignore: '[role="status"], script, style' };
const sendButton = (name = "Skicka feedback") => screen.getByRole("button", { name });
const comment = () => screen.getByRole("textbox", { name: "Kommentar (valfri)" });
const status = () => screen.getByRole("status");

function pasteImage(target: Element) {
  const file = new File([rgbaPng(2, 3)], "shot.png", { type: "image/png" });
  fireEvent.paste(target, {
    clipboardData: { getData: () => "", files: [file], items: [] },
  });
}

describe("FeedbackForm — what Send carries", () => {
  it("mounts its live region empty, from the first render", () => {
    render(<Harness />);
    expect(status()).toHaveTextContent("");
    expect(status()).toHaveAttribute("aria-atomic", "true");
  });

  it("refuses a send with neither a rating nor a comment, without a request", async () => {
    render(<Harness />);
    const user = userEvent.setup();

    await user.click(sendButton());

    expect(screen.getByText("Välj ett betyg eller skriv en kommentar.", SHOWN)).toBeVisible();
    expect(status()).toHaveTextContent("Välj ett betyg eller skriv en kommentar.");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses an image alone", async () => {
    render(<Harness />);
    const user = userEvent.setup();
    pasteImage(comment());
    await screen.findByRole("img", { name: "Skärmbilden du har valt" });

    await user.click(sendButton());

    expect(screen.getByText("Välj ett betyg eller skriv en kommentar.", SHOWN)).toBeVisible();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("posts the rating, the trimmed comment and the rendered version, and no device context while the box is unticked", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    render(<Harness />);
    const user = userEvent.setup();

    await user.click(screen.getByRole("radio", { name: "4 av 5" }));
    await user.type(comment(), "  Bra sida ");
    await user.click(sendButton());

    await screen.findByText("Tack. Din feedback är sparad.", SHOWN);
    const { url, payload, screenshot } = sent(0);
    expect(url).toBe("/api/feedback");
    expect(fetchMock.mock.calls[0]![1]).toMatchObject({ method: "POST" });
    expect(payload).toEqual({
      submissionKey: expect.stringMatching(/^[0-9a-f-]{36}$/),
      page: "jobs",
      rating: 4,
      comment: "Bra sida",
      renderedVersion: "abc1234",
    });
    expect(screenshot).toBeNull();
    expect(readClientContext).not.toHaveBeenCalled();
  });

  it("reads and sends the device context at Send when the box is ticked", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    render(<Harness />);
    const user = userEvent.setup();

    const consent = screen.getByRole("checkbox", {
      name: "Skicka med skärm- och fönsterstorlek, pixeltäthet, enhetstyp, operativsystem och webbläsare",
    });
    expect(consent).not.toBeChecked();
    await user.click(consent);
    await user.click(screen.getByRole("radio", { name: "2 av 5" }));
    expect(readClientContext).not.toHaveBeenCalled();
    await user.click(sendButton());

    await screen.findByText("Tack. Din feedback är sparad.", SHOWN);
    expect(readClientContext).toHaveBeenCalledTimes(1);
    expect(sent(0).payload.client).toEqual(readClientContext(window));
  });

  it("says how to withdraw the consent before the box is ticked (Art. 7(3))", () => {
    render(<Harness />);
    const consent = screen.getByRole("checkbox", {
      name: "Skicka med skärm- och fönsterstorlek, pixeltäthet, enhetstyp, operativsystem och webbläsare",
    });
    const hint =
      "Frivilligt. Vill du återkalla samtycket efteråt kan du mejla oss, så raderar vi uppgifterna. Läs mer i integritetspolicyn, som öppnas i en ny flik.";

    expect(consent).not.toBeChecked();
    expect(consent).toHaveAccessibleDescription(hint);
    expect(screen.getByText(/^Frivilligt\. Vill du återkalla samtycket efteråt/, SHOWN)).toBeVisible();
    expect(screen.getByRole("link", { name: "integritetspolicyn" })).toHaveAttribute("href", "/integritet");
  });

  it("sends no rendered version when the page was rendered without one", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    render(<Harness renderedVersion={null} />);
    const user = userEvent.setup();

    await user.type(comment(), "Text");
    await user.click(sendButton());

    await screen.findByText("Tack. Din feedback är sparad.", SHOWN);
    expect(sent(0).payload).not.toHaveProperty("renderedVersion");
  });

  it("sends the prepared PNG as the screenshot part, named screenshot.png", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    render(<Harness />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("radio", { name: "5 av 5" }));
    pasteImage(comment());
    await screen.findByRole("img", { name: "Skärmbilden du har valt" });

    await user.click(sendButton());

    await screen.findByText("Tack. Din feedback är sparad.", SHOWN);
    const { screenshot } = sent(0);
    expect(screenshot).toBeInstanceOf(File);
    expect((screenshot as File).name).toBe("screenshot.png");
    expect((screenshot as File).size).toBe(ENCODED.size);
  });
});

describe("FeedbackForm — the submission key", () => {
  it("resends the same content under the same key after an unknown answer", async () => {
    fetchMock.mockResolvedValueOnce(answer({ outcome: "unknown" }, 502)).mockResolvedValueOnce(answer({ outcome: "saved" }));
    render(<Harness />);
    const user = userEvent.setup();
    await user.type(comment(), "Sidan laddar långsamt");
    await user.click(sendButton());

    expect(
      await screen.findByText("Vi vet inte om din feedback sparades. Skicka igen, så sparas den bara en gång.", SHOWN),
    ).toBeVisible();
    await user.click(sendButton("Skicka igen"));

    await screen.findByText("Tack. Din feedback är sparad.", SHOWN);
    expect(sent(1).payload.submissionKey).toBe(sent(0).payload.submissionKey);
  });

  it("uses a new key once the content has been edited", async () => {
    fetchMock.mockResolvedValueOnce(answer({ outcome: "unknown" }, 502)).mockResolvedValueOnce(answer({ outcome: "saved" }));
    render(<Harness />);
    const user = userEvent.setup();
    await user.type(comment(), "Första");
    await user.click(sendButton());
    await screen.findByRole("button", { name: "Skicka igen" });

    await user.type(comment(), " och mer");

    // The promise of one saved record held only for the content that was lost.
    expect(screen.queryByText(/Vi vet inte om din feedback sparades/)).toBeNull();
    await user.click(sendButton());
    await screen.findByText("Tack. Din feedback är sparad.", SHOWN);
    expect(sent(1).payload.submissionKey).not.toBe(sent(0).payload.submissionKey);
    expect(sent(1).payload.comment).toBe("Första och mer");
  });
});

describe("FeedbackForm — answers", () => {
  it.each<[string, FeedbackSubmitOutcome, string]>([
    ["refused empty", { outcome: "refused", reason: "empty" }, "Välj ett betyg eller skriv en kommentar."],
    [
      "refused screenshot",
      { outcome: "refused", reason: "screenshot" },
      "Bilden kunde inte sparas. En bild kan bli för stor efter bearbetningen även om den gick att välja. Ta bort bilden för att skicka utan den.",
    ],
    ["refused invalid", { outcome: "refused", reason: "invalid" }, "Feedbacken kunde inte skickas. Ladda om sidan och försök igen."],
    ["closed", { outcome: "closed" }, "Det går inte att skicka feedback just nu."],
    ["busy", { outcome: "busy" }, "Det går inte att skicka just nu. Försök igen om en stund."],
    ["tooLarge", { outcome: "tooLarge" }, "Bilden är för stor. Den får vara högst 5 MB."],
    [
      "rateLimited, one minute",
      { outcome: "rateLimited", retryAfterSeconds: 60 },
      "Du har skickat många svar på kort tid. Försök igen om 1 minut.",
    ],
    [
      "rateLimited, rounded up",
      { outcome: "rateLimited", retryAfterSeconds: 61 },
      "Du har skickat många svar på kort tid. Försök igen om 2 minuter.",
    ],
    ["unknown", { outcome: "unknown" }, "Vi vet inte om din feedback sparades. Skicka igen, så sparas den bara en gång."],
  ])("shows the %s answer and keeps the content", async (_name, outcome, text) => {
    fetchMock.mockResolvedValue(answer(outcome, 400));
    render(<Harness />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("radio", { name: "3 av 5" }));
    await user.type(comment(), "Kvar");
    await user.click(sendButton());

    const message = await screen.findByText(text, SHOWN);
    expect(message).toBeVisible();
    expect(status()).toHaveTextContent(text);
    expect(screen.getByRole("radio", { name: "3 av 5" })).toBeChecked();
    expect(comment()).toHaveValue("Kvar");
    const button = screen.getByRole("button", { name: outcome.outcome === "unknown" ? "Skicka igen" : "Skicka feedback" });
    expect(button).toHaveAccessibleDescription(text);
  });

  it("offers a way back in when the session has ended", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "signedOut" }, 401));
    render(<Harness />);
    const user = userEvent.setup();
    await user.type(comment(), "Text");
    await user.click(sendButton());

    const text = "Du är utloggad. Logga in igen, som öppnas i en ny flik, och skicka sedan feedbacken här.";
    const link = await screen.findByRole("link", { name: "Logga in igen" });
    expect(link).toBeVisible();
    expect(link).toHaveAttribute("href", "/logga-in?next=%2Fjobb");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    expect(sendButton()).toHaveAccessibleDescription(text);
    expect(status()).toHaveTextContent(text);
    expect(comment()).toHaveValue("Text");
  });

  it.each([
    ["a network error", () => Promise.reject(new TypeError("Failed to fetch"))],
    ["an unparseable answer", () => Promise.resolve(new Response("<html>", { status: 502 }))],
  ])("reads %s as unknown", async (_name, reply) => {
    fetchMock.mockImplementation(reply);
    render(<Harness />);
    const user = userEvent.setup();
    await user.type(comment(), "Text");
    await user.click(sendButton());

    expect(
      await screen.findByText("Vi vet inte om din feedback sparades. Skicka igen, så sparas den bara en gång.", SHOWN),
    ).toBeVisible();
  });

  it("replaces the form with the receipt, focuses it, and reports the save once", async () => {
    fetchMock.mockResolvedValue(answer({ outcome: "saved" }));
    const onSaved = vi.fn();
    render(<Harness onSaved={onSaved} />);
    const user = userEvent.setup();
    await user.type(comment(), "Text");
    await user.click(sendButton());

    const receipt = await screen.findByText("Tack. Din feedback är sparad.", SHOWN);
    await waitFor(() => expect(receipt).toHaveFocus());
    expect(screen.queryByRole("button", { name: "Skicka feedback" })).toBeNull();
    expect(onSaved).toHaveBeenCalledTimes(1);
  });
});

describe("FeedbackForm — waiting", () => {
  it("is aria-disabled and keeps focus while sending, and a second press sends nothing", async () => {
    let reply: (response: Response) => void = () => {};
    fetchMock.mockReturnValue(new Promise<Response>((resolve) => (reply = resolve)));
    render(<Harness />);
    const user = userEvent.setup();
    await user.type(comment(), "Text");
    await user.click(sendButton());

    const button = sendButton("Skickar…");
    expect(button).toHaveAttribute("aria-disabled", "true");
    expect(button).not.toBeDisabled();
    expect(button).toHaveFocus();
    expect(status()).toHaveTextContent("Skickar…");
    await user.click(button);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await act(async () => reply(answer({ outcome: "busy" }, 409)));
    expect(sendButton()).not.toHaveAttribute("aria-disabled");
  });

  it("holds Send while the image is being prepared", async () => {
    render(<Harness codec={fakeCodec({ decode: "pending" })} />);
    const user = userEvent.setup();
    await user.type(comment(), "Text");
    pasteImage(comment());

    expect(await screen.findByText("Förbereder bilden…", SHOWN)).toBeVisible();
    expect(status()).toHaveTextContent("Förbereder bilden…");
    expect(sendButton()).toHaveAttribute("aria-disabled", "true");
    await user.click(sendButton());
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows an image refusal and keeps the rating and the text", async () => {
    render(<Harness codec={fakeCodec({ decode: "fail" })} />);
    const user = userEvent.setup();
    await user.click(screen.getByRole("radio", { name: "1 av 5" }));
    await user.type(comment(), "Text");
    pasteImage(comment());

    expect(await screen.findByText("Bilden kunde inte läsas. Välj en annan bild.", SHOWN)).toBeVisible();
    expect(status()).toHaveTextContent("Bilden kunde inte läsas. Välj en annan bild.");
    expect(screen.getByRole("radio", { name: "1 av 5" })).toBeChecked();
    expect(comment()).toHaveValue("Text");
    expect(sendButton()).not.toHaveAttribute("aria-disabled");
  });

  it("lets a text paste through into the comment", () => {
    render(<Harness />);
    const file = new File([rgbaPng(2, 3)], "shot.png", { type: "image/png" });
    const event = fireEvent.paste(comment(), {
      clipboardData: { getData: (format: string) => (format === "text/plain" ? "kopierad text" : ""), files: [file], items: [] },
    });
    // Not prevented: the browser goes on to insert the text.
    expect(event).toBe(true);
    expect(screen.queryByText("Förbereder bilden…")).toBeNull();
  });
});

describe("FeedbackForm — nothing is kept on the device", () => {
  it("writes no storage and no history entry through a whole send", async () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const pushState = vi.spyOn(window.history, "pushState");
    const replaceState = vi.spyOn(window.history, "replaceState");
    fetchMock.mockResolvedValueOnce(answer({ outcome: "unknown" }, 502)).mockResolvedValueOnce(answer({ outcome: "saved" }));
    render(<Harness />);
    const user = userEvent.setup();

    await user.click(screen.getByRole("radio", { name: "2 av 5" }));
    await user.type(comment(), "Text");
    await user.click(screen.getByRole("checkbox"));
    pasteImage(comment());
    await screen.findByRole("img");
    await user.click(sendButton());
    await user.click(await screen.findByRole("button", { name: "Skicka igen" }));
    await screen.findByText("Tack. Din feedback är sparad.", SHOWN);

    expect(setItem).not.toHaveBeenCalled();
    expect(pushState).not.toHaveBeenCalled();
    expect(replaceState).not.toHaveBeenCalled();
  });
});
