import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { publicJobAdUrl } from "@/lib/job-ads/public-job-ad-url";
import { ShareJobAd, type ShareJobAdLabels } from "./share-job-ad";

const JOB_AD = {
  id: "0b0b0b0b-1111-4222-8333-444444444444",
  title: "Sjuksköterska till vårdcentral",
  companyName: "Region Exempel",
};
const TITLE = "Sjuksköterska till vårdcentral – Region Exempel";
const URL_ = publicJobAdUrl(JOB_AD.id);

const LABELS: ShareJobAdLabels = {
  heading: "Dela annonsen",
  via: "Dela via",
  newTab: "(öppnas i ny flik)",
  targets: { facebook: "Facebook", linkedin: "LinkedIn", whatsapp: "WhatsApp", email: "E-post" },
  copy: "Kopiera länken",
  copied: "Kopierad",
  more: "Fler sätt att dela",
  field: "Länk till annonsen",
  copyFailed: "Länken kunde inte kopieras. Kopiera den från fältet.",
  shareFailed: "Delningen kunde inte startas. Kopiera länken från fältet.",
};

const refusal = (name: string) => Object.assign(new Error(name), { name });
const writeText = vi.fn<(text: string) => Promise<void>>();

function stubNavigator(extra: Record<string, unknown> = {}) {
  vi.stubGlobal("navigator", { ...navigator, clipboard: { writeText }, ...extra });
}

beforeEach(() => {
  writeText.mockReset().mockResolvedValue(undefined);
  stubNavigator();
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const renderRow = (extra: Record<string, unknown> = {}) =>
  render(<ShareJobAd jobAd={{ ...JOB_AD, ...extra }} labels={LABELS} headingId="share-title" />);
const moreItem = () => screen.getByRole("button", { name: "Fler sätt att dela" }).closest("li") as HTMLElement;

describe("ShareJobAd", () => {
  it("is a nosnippet section named by its heading, with the four links, then copy, then the share sheet", () => {
    renderRow();

    const section = screen.getByRole("region", { name: "Dela annonsen" });
    expect(section).toHaveAttribute("data-nosnippet");
    expect(within(section).getByRole("heading", { level: 2, name: "Dela annonsen" })).toHaveClass("jp-eyebrow");
    const items = within(section).getAllByRole("listitem").map((li) => li.textContent);
    expect(items).toEqual([
      "Dela via Facebook (öppnas i ny flik)",
      "Dela via LinkedIn (öppnas i ny flik)",
      "Dela via WhatsApp (öppnas i ny flik)",
      "Dela via E-post",
      "Kopiera länkenKopierad",
      "Fler sätt att dela",
    ]);
  });

  it("takes an h3 where the host asks for one", () => {
    render(<ShareJobAd jobAd={JOB_AD} labels={LABELS} headingId="share-title" headingLevel={3} />);

    expect(screen.getByRole("heading", { level: 3, name: "Dela annonsen" })).toBeInTheDocument();
  });

  it("builds every link from the canonical and the page title, never from another address the host's ad carries", () => {
    renderRow({ url: "https://arbetsformedlingen.se/platsbanken/annonser/1", description: "Text", contacts: [] });

    for (const link of screen.getAllByRole("link")) {
      expect(link.getAttribute("href")).toContain(encodeURIComponent(URL_));
      expect(link.getAttribute("href")).not.toContain("arbetsformedlingen");
    }
    expect(screen.getByRole("link", { name: "Dela via E-post" }).getAttribute("href")).toBe(
      `mailto:?subject=${encodeURIComponent(TITLE)}&body=${encodeURIComponent(URL_)}`
    );
  });

  it("names each link by what it does and keeps the visible name in it, with no aria-label (WCAG 2.4.4, 2.5.3)", () => {
    renderRow();

    for (const name of ["Facebook", "LinkedIn", "WhatsApp"]) {
      const link = screen.getByRole("link", { name: `Dela via ${name} (öppnas i ny flik)` });
      expect(link).not.toHaveAttribute("aria-label");
      expect(link).toHaveAttribute("target", "_blank");
      expect(link.getAttribute("rel")?.split(" ").sort()).toEqual(["noopener", "noreferrer", "nofollow"].sort());
    }
    const email = screen.getByRole("link", { name: "Dela via E-post" });
    expect(email).not.toHaveAttribute("target");
    expect(email.getAttribute("href")).toMatch(/^mailto:\?subject=/);
  });

  it("hides the two buttons without scripting, and holds the share sheet's place invisibly where the browser has none", () => {
    renderRow();

    const copyItem = screen.getByRole("button", { name: "Kopiera länken" }).closest("li");
    expect(copyItem).toHaveClass("noscript:hidden");
    expect(moreItem()).toHaveClass("noscript:hidden", "invisible");
  });

  it("shows the share sheet's button where the browser has one, and hands it exactly the title and the link", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    stubNavigator({ share });
    renderRow();

    expect(moreItem()).not.toHaveClass("invisible");
    const button = screen.getByRole("button", { name: "Fler sätt att dela" });
    fireEvent.click(button);

    await waitFor(() => expect(share).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(button).not.toHaveAttribute("aria-disabled"));
    expect(share.mock.calls[0]?.[0]).toStrictEqual({ title: TITLE, url: URL_ });
    expect(writeText).not.toHaveBeenCalled();
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("says nothing when the visitor cancels the sheet", async () => {
    const share = vi.fn().mockRejectedValue(refusal("AbortError"));
    stubNavigator({ share });
    renderRow();

    const button = screen.getByRole("button", { name: "Fler sätt att dela" });
    fireEvent.click(button);

    await waitFor(() => expect(share).toHaveBeenCalled());
    await waitFor(() => expect(button).not.toHaveAttribute("aria-disabled"));
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("opens no second sheet while one is pending, and marks itself unavailable meanwhile", async () => {
    let settle: () => void = () => {};
    const share = vi.fn(() => new Promise<void>((resolve) => (settle = resolve)));
    stubNavigator({ share });
    renderRow();

    const button = screen.getByRole("button", { name: "Fler sätt att dela" });
    fireEvent.click(button);
    fireEvent.click(button);

    expect(share).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(button).toHaveAttribute("aria-disabled", "true"));
    await act(async () => settle());
    expect(button).not.toHaveAttribute("aria-disabled");
  });

  it("shows the link in the field, and copies nothing, when the sheet refuses for any reason but a cancel", async () => {
    stubNavigator({ share: vi.fn().mockRejectedValue(refusal("NotAllowedError")) });
    renderRow();

    fireEvent.click(screen.getByRole("button", { name: "Fler sätt att dela" }));

    const field = await screen.findByRole("textbox", { name: "Länk till annonsen" });
    expect(field).toHaveValue(URL_);
    expect(field).toHaveAccessibleDescription(LABELS.shareFailed);
    await waitFor(() => expect(field).toHaveFocus());
    const input = field as HTMLInputElement;
    expect([input.selectionStart, input.selectionEnd]).toEqual([0, URL_.length]);
    expect(writeText).not.toHaveBeenCalled();
  });

  it("copies the link and confirms it on the button and to a screen reader for three seconds, and a new click restarts them", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    renderRow();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Kopiera länken" }));
    });

    expect(writeText).toHaveBeenCalledWith(URL_);
    const button = screen.getByRole("button", { name: "Kopierad" });
    const [idle, done] = [...button.querySelectorAll("span > span")];
    expect(idle).toHaveClass("invisible");
    expect(done).not.toHaveClass("invisible");
    expect(screen.getByRole("status")).toHaveTextContent("Kopierad");

    await act(async () => vi.advanceTimersByTime(2000));
    await act(async () => {
      fireEvent.click(button);
    });
    await act(async () => vi.advanceTimersByTime(2999));
    expect(screen.getByRole("status")).toHaveTextContent("Kopierad");
    await act(async () => vi.advanceTimersByTime(1));
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    expect(screen.getByRole("button", { name: "Kopiera länken" })).toBe(button);
    expect(idle).not.toHaveClass("invisible");
  });

  it("shows the link in a labelled read-only field, focused and selected, when the copy fails", async () => {
    writeText.mockRejectedValue(refusal("NotAllowedError"));
    renderRow();

    fireEvent.click(screen.getByRole("button", { name: "Kopiera länken" }));

    const field = await screen.findByRole("textbox", { name: "Länk till annonsen" });
    expect(field).toHaveValue(URL_);
    expect(field).toHaveAttribute("readonly");
    expect(field).toHaveAccessibleDescription(LABELS.copyFailed);
    expect(field).not.toHaveAttribute("aria-invalid");
    await waitFor(() => expect(field).toHaveFocus());
    const input = field as HTMLInputElement;
    expect([input.selectionStart, input.selectionEnd]).toEqual([0, URL_.length]);
    expect(screen.queryByRole("alert")).toBeNull();
    expect(field.closest("ul")).toBeNull();
  });

  it("moves focus to the field again on every later failure, not only the first", async () => {
    writeText.mockRejectedValue(refusal("NotAllowedError"));
    renderRow();
    const copy = screen.getByRole("button", { name: "Kopiera länken" });

    fireEvent.click(copy);
    const field = await screen.findByRole("textbox", { name: "Länk till annonsen" });
    await waitFor(() => expect(field).toHaveFocus());

    copy.focus();
    expect(copy).toHaveFocus();
    fireEvent.click(copy);

    await waitFor(() => expect(field).toHaveFocus());
    expect(writeText).toHaveBeenCalledTimes(2);
  });
});
