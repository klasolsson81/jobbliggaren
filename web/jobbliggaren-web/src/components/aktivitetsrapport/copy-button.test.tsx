import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { CopyButton } from "./copy-button";

const writeText = vi.fn().mockResolvedValue(undefined);

beforeEach(() => {
  writeText.mockClear();
  Object.assign(navigator, { clipboard: { writeText } });
});

describe("CopyButton", () => {
  it("exposes an accessible field-named label and the default copy text", () => {
    render(<CopyButton value="Skatteverket" fieldLabel="Arbetsgivare" />);
    // aria-label names the field so the per-field intent is clear to AT.
    expect(
      screen.getByRole("button", { name: "Kopiera Arbetsgivare" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Kopiera")).toBeInTheDocument();
  });

  it("writes the value to the clipboard and confirms with a polite announcement", async () => {
    render(<CopyButton value="Systemutvecklare" fieldLabel="Jobbtitel" />);
    fireEvent.click(screen.getByRole("button"));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith("Systemutvecklare"));
    expect(await screen.findByText("Kopierad")).toBeInTheDocument();

    const status = screen.getByRole("status");
    expect(status).toHaveTextContent("Jobbtitel kopierad");
  });

  it("does not throw or confirm when the clipboard is unavailable", async () => {
    writeText.mockRejectedValueOnce(new Error("denied"));
    render(<CopyButton value="x" fieldLabel="Ort" />);
    fireEvent.click(screen.getByRole("button"));

    await waitFor(() => expect(writeText).toHaveBeenCalled());
    // Failure degrades silently — no false "Kopierad" confirmation.
    expect(screen.queryByText("Kopierad")).not.toBeInTheDocument();
  });
});
