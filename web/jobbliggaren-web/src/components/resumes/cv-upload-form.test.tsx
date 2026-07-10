import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvUploadForm } from "./cv-upload-form";

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

describe("CvUploadForm — ärlig upload-copy", () => {
  it("knappen namnger uppladdningen ('Ladda upp och granska CV'), inte 'Tolka'", () => {
    render(<CvUploadForm />);
    const button = screen.getByRole("button", {
      name: "Ladda upp och granska CV",
    });
    expect(button).toBeInTheDocument();
    // Den gamla, missvisande copyn (filen är bara VALD vid klick) får inte
    // finnas kvar på knappen.
    expect(
      screen.queryByRole("button", { name: "Tolka och granska CV" })
    ).not.toBeInTheDocument();
  });

  it("knappen är disabled tills en fil valts", () => {
    render(<CvUploadForm />);
    expect(
      screen.getByRole("button", { name: "Ladda upp och granska CV" })
    ).toBeDisabled();
  });

  it("har label kopplad till filväljaren och ingen exempel-placeholder", () => {
    render(<CvUploadForm />);
    const input = document.querySelector<HTMLInputElement>(
      'input[type="file"]'
    );
    expect(input).not.toBeNull();
    expect(input).not.toHaveAttribute("placeholder");
  });
});

describe("CvUploadForm — onUploaded-callback (ADR 0077 STEG 5)", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    pushMock.mockReset();
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  function selectFile(input: HTMLInputElement) {
    const file = new File(["cv-bytes"], "cv.pdf", { type: "application/pdf" });
    return userEvent.upload(input, file);
  }

  it("autoUpload: filval laddar upp direkt utan submit-klick (och ingen submit-rad renderas)", async () => {
    const onUploaded = vi.fn();
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ parsedResumeId: PARSED_ID }), {
        status: 201,
        headers: { "content-type": "application/json" },
      })
    );

    render(<CvUploadForm onUploaded={onUploaded} autoUpload showHelp={false} />);
    // Auto-läget har ingen submit-knapp — uppladdningen startar vid filval.
    expect(
      screen.queryByRole("button", { name: "Ladda upp och granska CV" })
    ).not.toBeInTheDocument();

    const input = document.querySelector<HTMLInputElement>(
      'input[type="file"]'
    )!;
    await selectFile(input);

    await waitFor(() => expect(onUploaded).toHaveBeenCalledTimes(1));
    expect(onUploaded).toHaveBeenCalledWith(PARSED_ID, "cv.pdf");
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("anropar onUploaded med parsedResumeId i stället för router.push vid 201", async () => {
    const user = userEvent.setup();
    const onUploaded = vi.fn();
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ parsedResumeId: PARSED_ID }), {
        status: 201,
        headers: { "content-type": "application/json" },
      })
    );

    render(<CvUploadForm onUploaded={onUploaded} />);
    const input = document.querySelector<HTMLInputElement>(
      'input[type="file"]'
    )!;
    await selectFile(input);
    await user.click(
      screen.getByRole("button", { name: "Ladda upp och granska CV" })
    );

    await waitFor(() => expect(onUploaded).toHaveBeenCalledTimes(1));
    // Epik #526: onUploaded får nu även filnamnet (driver "CV inläst: {filnamn}").
    expect(onUploaded).toHaveBeenCalledWith(PARSED_ID, "cv.pdf");
    // Callback-vägen navigerar INTE bort (host-modalen stannar öppen).
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("utan onUploaded navigerar till Slutför-guiden vid 201 (default)", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ parsedResumeId: PARSED_ID }), {
        status: 201,
        headers: { "content-type": "application/json" },
      })
    );

    render(<CvUploadForm />);
    const input = document.querySelector<HTMLInputElement>(
      'input[type="file"]'
    )!;
    await selectFile(input);
    await user.click(
      screen.getByRole("button", { name: "Ladda upp och granska CV" })
    );

    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(`/cv/slutfor/${PARSED_ID}`)
    );
  });
});
