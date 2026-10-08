import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminFeedbackScreenshot } from "./admin-feedback-screenshot";
import { rgbaPng } from "../../../tests/admin/png-encoder";

const ID = "00000000-0000-4000-8000-000000000501";
const METADATA = { width: 2, height: 3 };
const PNG = rgbaPng(METADATA.width, METADATA.height);
const OriginalURL = URL;
const fetchMock = vi.fn<typeof fetch>();
const createUrl = vi.fn(() => "blob:private-screenshot");
const revokeUrl = vi.fn();
const imageResponse = () => new Response(PNG, { headers: { "content-type": "image/png" } });

beforeEach(() => {
  fetchMock.mockReset(); createUrl.mockClear(); revokeUrl.mockClear();
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("URL", class extends OriginalURL { static createObjectURL = createUrl; static revokeObjectURL = revokeUrl; });
});
afterEach(() => vi.unstubAllGlobals());

describe("the feedback screenshot in its detail", () => {
  it("shows actual absence without making a read", () => {
    render(<AdminFeedbackScreenshot id={ID} metadata={null} />);
    expect(screen.getByText("Det finns ingen skärmbild.")).toBeVisible();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows loading, then lets the keyboard open and close the full-size region", async () => {
    let resolve: (response: Response) => void = () => {};
    fetchMock.mockReturnValue(new Promise<Response>((done) => { resolve = done; }));
    render(<AdminFeedbackScreenshot id={ID} metadata={METADATA} />);
    expect(screen.getByRole("status")).toHaveTextContent("Hämtar skärmbilden…");
    await act(async () => { resolve(imageResponse()); });
    const button = await screen.findByRole("button", { name: "Visa full storlek" });
    const user = userEvent.setup();
    await user.tab(); expect(button).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(button).toHaveAttribute("aria-expanded", "true");
    await user.tab();
    const region = screen.getByRole("region", { name: "Skärmbild som bifogats feedbacken" });
    expect(region).toHaveFocus();
    expect(region).toHaveAttribute("data-full-size", "true");
    await user.tab({ shift: true }); await user.keyboard(" ");
    expect(button).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("img")).toHaveAttribute("src", "blob:private-screenshot");
  });

  it.each([404, 403, 500])("renders backend refusal %i truthfully", async (status) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));
    render(<AdminFeedbackScreenshot id={ID} metadata={METADATA} />);
    const liveRegion = screen.getByRole("status");
    const focused = document.activeElement;
    expect(await screen.findByText(status === 404 ? "Det finns ingen skärmbild." : "Skärmbilden kunde inte hämtas. Ladda om sidan.")).toBeVisible();
    expect(screen.getByRole("status")).toBe(liveRegion);
    expect(document.activeElement).toBe(focused);
    expect(createUrl).not.toHaveBeenCalled();
  });

  it("revokes a loaded blob and aborts the request when the detail changes", async () => {
    fetchMock.mockResolvedValue(imageResponse());
    const view = render(<AdminFeedbackScreenshot id={ID} metadata={METADATA} />);
    await screen.findByRole("img");
    const signal = fetchMock.mock.calls[0]?.[1]?.signal;
    fetchMock.mockReturnValue(new Promise<Response>(() => {}));
    view.rerender(<AdminFeedbackScreenshot id="00000000-0000-4000-8000-000000000502" metadata={METADATA} />);
    expect(signal?.aborted).toBe(true);
    expect(revokeUrl).toHaveBeenCalledWith("blob:private-screenshot");
    expect(screen.queryByRole("img")).toBeNull();
    expect(screen.getByRole("status")).toHaveTextContent("Hämtar skärmbilden…");
  });

  it("does not create a blob if an aborted response settles after unmount", async () => {
    let resolve: (response: Response) => void = () => {};
    fetchMock.mockReturnValue(new Promise<Response>((done) => { resolve = done; }));
    const view = render(<AdminFeedbackScreenshot id={ID} metadata={METADATA} />);
    const signal = fetchMock.mock.calls[0]?.[1]?.signal;
    view.unmount(); expect(signal?.aborted).toBe(true);
    await act(async () => { resolve(imageResponse()); });
    expect(createUrl).not.toHaveBeenCalled();
  });

  it("revokes the blob on unmount and re-reads if metadata returns", async () => {
    fetchMock.mockImplementation(async () => imageResponse());
    const view = render(<AdminFeedbackScreenshot id={ID} metadata={METADATA} />);
    await screen.findByRole("img");
    view.rerender(<AdminFeedbackScreenshot id={ID} metadata={null} />);
    expect(revokeUrl).toHaveBeenCalledTimes(1);
    view.rerender(<AdminFeedbackScreenshot id={ID} metadata={METADATA} />);
    await waitFor(() => expect(createUrl).toHaveBeenCalledTimes(2));
    view.unmount(); expect(revokeUrl).toHaveBeenCalledTimes(2);
  });
});
