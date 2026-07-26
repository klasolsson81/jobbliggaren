import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvUploadForm } from "./cv-upload-form";

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

const PARSED_ID = "11111111-1111-4111-8111-111111111111";
const RESUME_ID = "22222222-2222-4222-8222-222222222222";

/** Sammansatt utfall (CV-pivot 5c): BFF:ens ImportOutcomeResponse-shape. */
function promotedResponse() {
  return new Response(
    JSON.stringify({
      parsedResumeId: PARSED_ID,
      outcome: "Promoted",
      resumeId: RESUME_ID,
      blockReason: null,
      personnummer: { found: false, count: 0, kinds: [] },
    }),
    { status: 201, headers: { "content-type": "application/json" } }
  );
}

function pendingResponse(blockReason: string, count = 0) {
  return new Response(
    JSON.stringify({
      parsedResumeId: PARSED_ID,
      outcome: "LeftPending",
      resumeId: null,
      blockReason,
      personnummer: { found: count > 0, count, kinds: count > 0 ? ["Full"] : [] },
    }),
    { status: 200, headers: { "content-type": "application/json" } }
  );
}

function selectFile(input: HTMLInputElement) {
  const file = new File(["cv-bytes"], "cv.pdf", { type: "application/pdf" });
  return userEvent.upload(input, file);
}

function fileInput(): HTMLInputElement {
  return document.querySelector<HTMLInputElement>('input[type="file"]')!;
}

/** Läser FormData-kroppen ur ett fetch-mockanrop (strict-säkert index). */
function formDataOfCall(
  fetchMock: ReturnType<typeof vi.fn>,
  index: number
): FormData {
  const call = fetchMock.mock.calls[index];
  expect(call).toBeDefined();
  return (call![1] as RequestInit).body as FormData;
}

async function submit(user: ReturnType<typeof userEvent.setup>) {
  await user.click(
    screen.getByRole("button", { name: "Ladda upp och granska CV" })
  );
}

describe("CvUploadForm — ärlig upload-copy", () => {
  it("knappen namnger uppladdningen ('Ladda upp och granska CV'), inte 'Tolka'", () => {
    render(<CvUploadForm />);
    const button = screen.getByRole("button", {
      name: "Ladda upp och granska CV",
    });
    expect(button).toBeInTheDocument();
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
    const input = fileInput();
    expect(input).not.toBeNull();
    expect(input).not.toHaveAttribute("placeholder");
  });

  it("namnfältet renderas med label + hint, utan placeholder (5c)", () => {
    render(<CvUploadForm />);
    const name = screen.getByRole("textbox", { name: "Namn på CV" });
    // Tomt = "ingen människa döpte det här" → servern genererar ett icke-PII-namn (#1060).
    expect(name).toHaveValue("");
    expect(name).not.toHaveAttribute("placeholder");
    expect(
      screen.getByText(
        "Namnet visas i din CV-lista så att du hittar rätt variant. Lämnar du det tomt får CV:t namnet Importerat CV med dagens datum, och du kan byta namn när du vill."
      )
    ).toBeInTheDocument();
  });

  // #1060: webbläsarens autofyll erbjuder kontoinnehavarens namn på ett fält märkt
  // autoComplete="name" — och skulle därmed skriva personuppgiften rakt in i den
  // okrypterade etikett-kolumnen ändringen finns till för att hålla fri från den.
  // Risken ökade av att fältet numera startar tomt.
  it("namnfältet är INTE autofyllbart som personnamn", () => {
    render(<CvUploadForm />);
    expect(screen.getByRole("textbox", { name: "Namn på CV" })).toHaveAttribute(
      "autocomplete",
      "off"
    );
  });

  // #1060 (Klas 2026-07-26, alternativ 1): auto-läget behåller sitt ett-klick och visar
  // inget namnfält. Servern genererar etiketten; namnbyte sker på /cv.
  it("auto-läget visar inget namnfält (servern genererar etiketten)", () => {
    render(<CvUploadForm autoUpload />);
    expect(
      screen.queryByRole("textbox", { name: "Namn på CV" })
    ).not.toBeInTheDocument();
  });
});

describe("CvUploadForm — utfalls-baserad ruttning (CV-pivot 5c)", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    pushMock.mockReset();
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it("Promoted (201) navigerar till det nya CV:ts granskning", async () => {
    const user = userEvent.setup();
    global.fetch = vi.fn().mockResolvedValue(promotedResponse());

    render(<CvUploadForm />);
    await selectFile(fileInput());
    await submit(user);

    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(`/cv/${RESUME_ID}/granska`)
    );
  });

  it("LeftPending utan personnummer navigerar till staging-granskningen", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue(pendingResponse("ParseNotConfident"));

    render(<CvUploadForm />);
    await selectFile(fileInput());
    await submit(user);

    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(`/cv/granska/${PARSED_ID}`)
    );
  });

  it("skickar det redigerade namnet som formfält", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue(promotedResponse());
    global.fetch = fetchMock;

    render(<CvUploadForm />);
    await user.type(
      screen.getByRole("textbox", { name: "Namn på CV" }),
      "Backend-CV 2026"
    );
    await selectFile(fileInput());
    await submit(user);

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    const body = formDataOfCall(fetchMock, 0);
    expect(body.get("name")).toBe("Backend-CV 2026");
    // Fail-closed: flaggan skickas ALDRIG utan uttryckligt samtycke.
    expect(body.get("personnummerAcknowledged")).toBeNull();
  });

  // #1060: ett ORÖRT fält skickas ALDRIG. Det är den regel som håller det råa filnamnet
  // ute ur etikett-kanalen — masken mot personnummer i filnamn sitter server-side, så ett
  // klient-härlett förslag hade kunnat bära ett personnummer in i handlerns etikett-scan
  // och rest samtyckesdialogen på ett fynd serverns kroppsscan säger inte finns.
  it("orört namnfält skickar inget name-fält alls", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue(promotedResponse());
    global.fetch = fetchMock;

    render(<CvUploadForm />);
    await selectFile(fileInput());
    await submit(user);

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(formDataOfCall(fetchMock, 0).get("name")).toBeNull();
  });

  it("auto-läget skickar inget namn (servern genererar det)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(promotedResponse());
    global.fetch = fetchMock;

    render(<CvUploadForm onUploaded={vi.fn()} autoUpload />);
    await selectFile(fileInput());

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(formDataOfCall(fetchMock, 0).get("name")).toBeNull();
  });

  // Samtyckesdialogen är juridiskt bärande (ADR 0114) och dess copy säger "Vi hittade N
  // personnummer i FILEN". Samma orsaks-token kan komma från ett fynd i ETIKETTEN, och då
  // är serverns kroppsscan ren (count = 0) — att resa dialogen då vore ett felrapporterat
  // verdikt och ett samtycke inhämtat på fel premiss.
  it("etikett-fynd (count 0) reser INTE samtyckesdialogen utan rutar till granskningen", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue(pendingResponse("PersonnummerPresent", 0));

    render(<CvUploadForm />);
    await selectFile(fileInput());
    await submit(user);

    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(`/cv/granska/${PARSED_ID}`)
    );
    expect(
      screen.queryByText("Filen innehåller ett personnummer")
    ).not.toBeInTheDocument();
  });

  it("onUploaded får det sammansatta utfallet + filnamnet i stället för navigation", async () => {
    const user = userEvent.setup();
    const onUploaded = vi.fn();
    global.fetch = vi.fn().mockResolvedValue(promotedResponse());

    render(<CvUploadForm onUploaded={onUploaded} />);
    await selectFile(fileInput());
    await submit(user);

    await waitFor(() => expect(onUploaded).toHaveBeenCalledTimes(1));
    expect(onUploaded).toHaveBeenCalledWith(
      { kind: "promoted", resumeId: RESUME_ID, parsedResumeId: PARSED_ID },
      "cv.pdf"
    );
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("autoUpload: filval laddar upp direkt utan submit-klick (ingen submit-rad)", async () => {
    const onUploaded = vi.fn();
    global.fetch = vi
      .fn()
      .mockResolvedValue(pendingResponse("ParseNotConfident"));

    render(<CvUploadForm onUploaded={onUploaded} autoUpload showHelp={false} />);
    expect(
      screen.queryByRole("button", { name: "Ladda upp och granska CV" })
    ).not.toBeInTheDocument();

    await selectFile(fileInput());

    await waitFor(() => expect(onUploaded).toHaveBeenCalledTimes(1));
    expect(onUploaded).toHaveBeenCalledWith(
      {
        kind: "pending",
        parsedResumeId: PARSED_ID,
        blockReason: "ParseNotConfident",
        personnummerCount: 0,
      },
      "cv.pdf"
    );
    expect(pushMock).not.toHaveBeenCalled();
  });
});

describe("CvUploadForm — personnummer-samtycket (ADR 0114, 5b B6)", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    pushMock.mockReset();
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it("personnummer-fyndet reser dialogen och rutar INTE förrän användaren valt", async () => {
    const user = userEvent.setup();
    global.fetch = vi
      .fn()
      .mockResolvedValue(pendingResponse("PersonnummerPresent", 1));

    render(<CvUploadForm />);
    await selectFile(fileInput());
    await submit(user);

    // Dialogen namnger fyndet — utan att rendera värdet (B6 i).
    expect(
      await screen.findByRole("heading", {
        name: "Filen innehåller ett personnummer",
      })
    ).toBeInTheDocument();
    expect(pushMock).not.toHaveBeenCalled();
  });

  it("samtycke JA re-POST:ar samma fil med personnummerAcknowledged=true och rutar sedan", async () => {
    const user = userEvent.setup();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(pendingResponse("PersonnummerPresent", 1))
      .mockResolvedValueOnce(pendingResponse("PersonnummerPresent", 1));
    global.fetch = fetchMock;

    render(<CvUploadForm />);
    await selectFile(fileInput());
    await submit(user);

    await screen.findByRole("heading", {
      name: "Filen innehåller ett personnummer",
    });
    // Distinkt aktivt opt-in (B6 iii): den specifika knappen, aldrig "OK".
    await user.click(screen.getByRole("button", { name: "Spara filen ändå" }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    const first = formDataOfCall(fetchMock, 0);
    const second = formDataOfCall(fetchMock, 1);
    expect(first.get("personnummerAcknowledged")).toBeNull();
    expect(second.get("personnummerAcknowledged")).toBe("true");

    // Innehållet befordras aldrig med personnummer (B3) — ruttningen går till
    // granskningen av den NYA parsen (re-POST:en skapade den).
    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(`/cv/granska/${PARSED_ID}`)
    );
  });

  it("samtycke NEJ re-POST:ar INTE och rutar till granskningen av pending-parsen", async () => {
    const user = userEvent.setup();
    const fetchMock = vi
      .fn()
      .mockResolvedValue(pendingResponse("PersonnummerPresent", 1));
    global.fetch = fetchMock;

    render(<CvUploadForm />);
    await selectFile(fileInput());
    await submit(user);

    await screen.findByRole("heading", {
      name: "Filen innehåller ett personnummer",
    });
    await user.click(screen.getByRole("button", { name: "Spara inte filen" }));

    // Ingen andra POST — avböjt samtycke lagrar ingenting (fail-closed).
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(`/cv/granska/${PARSED_ID}`)
    );
  });

  it("Escape stänger dialogen som ett NEJ — aldrig ett tyst samtycke", async () => {
    const user = userEvent.setup();
    const fetchMock = vi
      .fn()
      .mockResolvedValue(pendingResponse("PersonnummerPresent", 1));
    global.fetch = fetchMock;

    render(<CvUploadForm />);
    await selectFile(fileInput());
    await submit(user);

    await screen.findByRole("heading", {
      name: "Filen innehåller ett personnummer",
    });
    await user.keyboard("{Escape}");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    await waitFor(() =>
      expect(pushMock).toHaveBeenCalledWith(`/cv/granska/${PARSED_ID}`)
    );
  });
});
