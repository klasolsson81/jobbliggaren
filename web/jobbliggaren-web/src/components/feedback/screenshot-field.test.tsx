import { useState } from "react";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ScreenshotField, pastedScreenshot } from "./screenshot-field";
import type { ScreenshotState } from "./use-feedback-form";

const OriginalURL = URL;
const createUrl = vi.fn((blob: Blob) => `blob:preview-${blob.size}`);
const revokeUrl = vi.fn();

beforeEach(() => {
  createUrl.mockClear();
  revokeUrl.mockClear();
  vi.stubGlobal(
    "URL",
    class extends OriginalURL {
      static createObjectURL = createUrl;
      static revokeObjectURL = revokeUrl;
    },
  );
});
afterEach(() => vi.unstubAllGlobals());

const png = (bytes = 10) => new File([new Uint8Array(bytes)], "shot.png", { type: "image/png" });
const ready = (blob: Blob): ScreenshotState => ({ kind: "ready", blob, width: 1280, height: 720 });

/** A clipboard as a paste hands it over: text, files and items. */
function clipboard({ text = "", files = [] as File[] }): DataTransfer {
  return {
    getData: (format: string) => (format === "text/plain" ? text : ""),
    files: files as unknown as FileList,
    items: files.map((file) => ({ kind: "file", type: file.type, getAsFile: () => file })) as unknown as DataTransferItemList,
  } as unknown as DataTransfer; // A partial DataTransfer: the three members the reader uses.
}

describe("pastedScreenshot (C12)", () => {
  it("takes an image when the paste carries no text", () => {
    const file = png();
    expect(pastedScreenshot(clipboard({ files: [file] }))).toBe(file);
  });

  it("lets the text through when the paste also carries text", () => {
    expect(pastedScreenshot(clipboard({ text: "Fel på sidan", files: [png()] }))).toBeNull();
  });

  it("ignores an image in a format that is not accepted", () => {
    const gif = new File([new Uint8Array(4)], "a.gif", { type: "image/gif" });
    expect(pastedScreenshot(clipboard({ files: [gif] }))).toBeNull();
  });

  it("reads the image from the items when the file list is empty", () => {
    const file = new File([new Uint8Array(4)], "a.webp", { type: "image/webp" });
    const data = {
      getData: () => "",
      files: [] as unknown as FileList,
      items: [{ kind: "file", type: file.type, getAsFile: () => file }] as unknown as DataTransferItemList,
    } as unknown as DataTransfer; // A partial DataTransfer, as above.
    expect(pastedScreenshot(data)).toBe(file);
  });

  it("returns nothing for a plain text paste", () => {
    expect(pastedScreenshot(clipboard({ text: "hej" }))).toBeNull();
  });
});

describe("ScreenshotField", () => {
  it("opens the picker from the add button and hands over the chosen file", async () => {
    const onAdd = vi.fn();
    const { container } = render(<ScreenshotField screenshot={{ kind: "none" }} onAdd={onAdd} onRemove={vi.fn()} />);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    expect(input).toHaveAttribute("accept", "image/png,image/jpeg,image/webp");
    const click = vi.spyOn(input, "click");
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Lägg till skärmbild" }));
    expect(click).toHaveBeenCalled();

    const file = png();
    await user.upload(input, file);
    expect(onAdd).toHaveBeenCalledWith(file);
  });

  it("describes the add button with the paste hint", () => {
    render(<ScreenshotField screenshot={{ kind: "none" }} onAdd={vi.fn()} onRemove={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Lägg till skärmbild" })).toHaveAccessibleDescription(
      "Du kan också klistra in en skärmbild i kommentarsfältet.",
    );
  });

  it("says it is preparing the image", () => {
    render(<ScreenshotField screenshot={{ kind: "preparing", job: 1 }} onAdd={vi.fn()} onRemove={vi.fn()} />);
    expect(screen.getByText("Förbereder bilden…")).toBeVisible();
  });

  it.each([
    ["type", "Bilden måste vara en PNG-, JPEG- eller WebP-fil."],
    ["unreadable", "Bilden kunde inte läsas. Välj en annan bild."],
    ["tooLarge", "Bilden är för stor för att öppnas. Välj en mindre bild."],
    ["tooDetailed", "Bilden är för stor för att skickas. Beskär den eller välj en mindre skärmbild."],
  ] as const)("shows the %s refusal beside the control, as its description", (reason, text) => {
    render(<ScreenshotField screenshot={{ kind: "refused", reason }} onAdd={vi.fn()} onRemove={vi.fn()} />);
    const note = screen.getByText(text);
    expect(note).not.toHaveAttribute("role");
    expect(screen.getByRole("button", { name: "Lägg till skärmbild" }).getAttribute("aria-describedby")).toContain(note.id);
  });

  it("previews from a blob URL and revokes it when the image changes and when the field goes", async () => {
    const first = png(11);
    const second = png(22);
    const { rerender, unmount } = render(
      <ScreenshotField screenshot={ready(first)} onAdd={vi.fn()} onRemove={vi.fn()} />,
    );
    const image = await screen.findByRole("img", { name: "Skärmbilden du har valt" });
    expect(image).toHaveAttribute("src", "blob:preview-11");
    expect(createUrl).toHaveBeenCalledWith(first);

    rerender(<ScreenshotField screenshot={ready(second)} onAdd={vi.fn()} onRemove={vi.fn()} />);
    expect(revokeUrl).toHaveBeenCalledWith("blob:preview-11");
    expect(await screen.findByRole("img", { name: "Skärmbilden du har valt" })).toHaveAttribute("src", "blob:preview-22");

    unmount();
    expect(revokeUrl).toHaveBeenCalledWith("blob:preview-22");
  });

  it("revokes the preview when the image is removed and moves focus to the add button", async () => {
    function Harness() {
      const [screenshot, setScreenshot] = useState<ScreenshotState>(ready(png(33)));
      return (
        <ScreenshotField screenshot={screenshot} onAdd={vi.fn()} onRemove={() => setScreenshot({ kind: "none" })} />
      );
    }
    render(<Harness />);
    await screen.findByRole("img");
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Ta bort bilden" }));

    expect(screen.queryByRole("img")).toBeNull();
    expect(revokeUrl).toHaveBeenCalledWith("blob:preview-33");
    expect(screen.getByRole("button", { name: "Lägg till skärmbild" })).toHaveFocus();
  });

  it("moves focus to the remove button when the image it was waiting for replaces the add button", async () => {
    const { rerender } = render(
      <ScreenshotField screenshot={{ kind: "preparing", job: 1 }} onAdd={vi.fn()} onRemove={vi.fn()} />,
    );
    screen.getByRole("button", { name: "Lägg till skärmbild" }).focus();

    rerender(<ScreenshotField screenshot={ready(png(5))} onAdd={vi.fn()} onRemove={vi.fn()} />);
    await act(async () => {});

    expect(screen.getByRole("button", { name: "Ta bort bilden" })).toHaveFocus();
  });

  it("leaves focus where the user moved it when the image becomes ready", async () => {
    const { rerender } = render(
      <>
        <textarea aria-label="Kommentar" />
        <ScreenshotField screenshot={{ kind: "preparing", job: 1 }} onAdd={vi.fn()} onRemove={vi.fn()} />
      </>,
    );
    const comment = screen.getByRole("textbox", { name: "Kommentar" });
    comment.focus();

    rerender(
      <>
        <textarea aria-label="Kommentar" />
        <ScreenshotField screenshot={ready(png(5))} onAdd={vi.fn()} onRemove={vi.fn()} />
      </>,
    );
    await act(async () => {});

    expect(comment).toHaveFocus();
  });
});
