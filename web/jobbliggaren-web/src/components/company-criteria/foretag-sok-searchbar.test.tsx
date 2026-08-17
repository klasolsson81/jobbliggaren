import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { renderToString } from "react-dom/server";
import { NextIntlClientProvider } from "next-intl";
import userEvent from "@testing-library/user-event";
import svPages from "../../../messages/sv/pages.json";
import svComponents from "../../../messages/sv/components.json";
import { ForetagSokSearchbar } from "./foretag-sok-searchbar";
import { buildForetagSokHref } from "@/lib/company-search/search-params";
import type { CriterionReference } from "@/lib/dto/company-criteria";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

const followActionMock = vi.fn();
const unfollowActionMock = vi.fn();
vi.mock("@/lib/actions/company-follows", () => ({
  followCompanyAction: (...args: unknown[]) => followActionMock(...args),
  unfollowCompanyAction: (...args: unknown[]) => unfollowActionMock(...args),
}));

// A small but structurally-real SCB reference: section J with two divisions (so a division's leaf-set is
// a strict subset of the section's — the seed test can match a division unambiguously), and one län.
const REFERENCE: CriterionReference = {
  sniVersion: "2025",
  kommunVersion: "2025",
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering, datakonsultverksamhet",
          leaves: [
            { code: "62010", name: "Datakonsultverksamhet" },
            { code: "62020", name: "Systemutveckling och programvarutveckling" },
          ],
        },
        {
          code: "63",
          name: "Informationstjänster",
          leaves: [
            { code: "63110", name: "Databehandling och hosting" },
            { code: "63120", name: "Webbportaler" },
          ],
        },
      ],
    },
  ],
  lan: [
    {
      code: "01",
      name: "Stockholms län",
      kommuner: [
        { code: "0180", name: "Stockholm" },
        { code: "0181", name: "Södertälje" },
      ],
    },
  ],
};

const VALID_ORGNR = "5560125790"; // 3rd digit 6 >= 2 → legal entity
const PNR_SHAPED = "1010101010"; // 3rd digit 1 < 2 → personnummer-shaped → must be refused locally
// #1075 — the century-prefixed written forms. Each strips to a fixture above, which is the point:
// the century is presentation, and what the dispatch decides on is the derived ten digits.
const PNR_SHAPED_12 = "191010101010"; // → PNR_SHAPED
const PNR_SHAPED_12_HYPHEN = "19101010-1010"; // → PNR_SHAPED
const VALID_ORGNR_12 = "205560125790"; // → VALID_ORGNR

const FOUND_COMPANY = {
  organizationNumber: VALID_ORGNR,
  isProtectedIdentity: false,
  name: "Volvo AB",
  seatMunicipalityCode: "1480",
  seatMunicipalityName: "Göteborg",
  sniCodes: ["29100"],
};

function orgNrResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const originalFetch = global.fetch;

beforeEach(() => {
  push.mockReset();
  followActionMock.mockReset();
  unfollowActionMock.mockReset();
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.restoreAllMocks();
});

function renderBar(
  props: Partial<React.ComponentProps<typeof ForetagSokSearchbar>> = {},
) {
  return render(
    <ForetagSokSearchbar
      reference={REFERENCE}
      referenceOk
      namn=""
      sni={[]}
      kommun={[]}
      {...props}
    />,
  );
}

/**
 * Open the bransch popover and return its dialog. The trigger owns the open state, exactly as the ort
 * trigger beside it does; the panel's accessible name matches the trigger's visible text.
 */
async function openBransch(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "Välj bransch" }));
  return screen.getByRole("dialog", { name: "Välj bransch" });
}

/** Filter inside the open popover and tick the row with `name` (any level). */
async function pickBransch(
  user: ReturnType<typeof userEvent.setup>,
  query: string,
  name: string,
) {
  const dialog = await openBransch(user);
  await user.type(within(dialog).getByLabelText("Sök bransch"), query);
  await user.click(within(dialog).getByRole("checkbox", { name }));
  await user.keyboard("{Escape}");
}

describe("ForetagSokSearchbar — filters commit live, the name still submits", () => {
  it("a bransch pick navigates on its own, carrying the applied ort with it", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    // One applied ort, so the commit has a second axis to preserve.
    renderBar({ kommun: ["0180"] });
    const user = userEvent.setup();

    expect(
      screen.getByRole("button", { name: "Ta bort Stockholm" }),
    ).toBeInTheDocument();

    await pickBransch(user, "system", "62020 Systemutveckling och programvarutveckling");

    // No submit. The whole state goes, never a delta — and `scroll: false`, because a chip narrows
    // the list you are already reading.
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: ["62020"], kommun: ["0180"] }),
      { scroll: false },
    );
    // A filter commit never touches the org.nr POST path.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("the name still requires its own submit, and carries the live filters with it", async () => {
    renderBar({ sni: ["62020"], kommun: ["0180"] });
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      "Volvo",
    );
    // Typing alone commits nothing: a name prefix runs against 1.07M rows.
    expect(push).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    // Default scroll here, unlike a filter commit — a new search is a new answer.
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "Volvo", sni: ["62020"], kommun: ["0180"] }),
    );
  });

  it("the unapplied line is about the NAME only — filters can no longer diverge", async () => {
    renderBar();
    const user = userEvent.setup();
    const LINE = "Ändringen i namnfältet tillämpas när du väljer Sök företag.";

    expect(screen.queryByText(LINE)).not.toBeInTheDocument();

    // A typed name is the one thing that still waits for the button.
    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      "Volvo",
    );
    expect(screen.getByText(LINE)).toBeInTheDocument();

    // A bransch pick applies immediately, so it must neither raise the line nor clear it: the line
    // tracks the NAME axis and nothing else. Measured HERE, against a state where the line is
    // already true — asserting its absence before anything was typed could not fail, because
    // nothing had diverged yet, and that vacuous middle assertion is what this replaces.
    await pickBransch(user, "datapro", "62 Dataprogrammering, datakonsultverksamhet");
    expect(screen.getByText(LINE)).toBeInTheDocument();
  });
});

/**
 * V1 (ADR 0087 D8(c)) — the invariant that makes live commit safe at all, and the one this whole
 * model could have broken silently.
 *
 * A filter commit builds its href from the `namn` PROP, never from the field. Build it from the
 * field and a chip click while ten unsubmitted digits sit in the input puts a possible personnummer
 * into `?namn=` — in history, in access logs, in any shared link — which is exactly what the org.nr
 * branch exists to make impossible. The org.nr branch guards the SUBMIT path; before live commit
 * there was no other way for the field's value to reach the URL, and now there is.
 */
describe("ForetagSokSearchbar — a filter commit never carries the typed value (V1, D8(c))", () => {
  it("commits the APPLIED name while ten unsubmitted digits sit in the field", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    renderBar({ namn: "Volvo" });
    const user = userEvent.setup();

    // Personnummer-shaped, unsubmitted. The user never pressed Sök, so this value has passed no gate.
    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      PNR_SHAPED,
    );
    await pickBransch(user, "system", "62020 Systemutveckling och programvarutveckling");

    expect(push).toHaveBeenCalledTimes(1);
    const [href] = push.mock.calls[0] as [string, unknown];
    // The applied name rides along; the typed digits do not appear anywhere in the URL.
    expect(href).toBe(
      buildForetagSokHref({ namn: "Volvo", sni: ["62020"], kommun: [] }),
    );
    expect(href).not.toContain(PNR_SHAPED);
    // And nothing was POSTed either — a filter commit is not an org.nr lookup.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  /**
   * The gate that protects the half-typed name, pinned against a commit that actually LANDS.
   *
   * Everywhere else in this file `router.push` is a synchronous mock, so props never change and the
   * re-seed can never fire — which means it can never be caught misfiring either. Here the landing is
   * simulated explicitly: the filter axis arrives changed while `namn` stays put, exactly what a
   * filter commit produces. Gate on the applied SIGNATURE instead of on `namn` and this wipes "Saab"
   * back to "Volvo" mid-typing; gate on `namn` and it cannot fire at all.
   */
  it("survives the commit LANDING: a new filter axis must not re-seed the field", async () => {
    const { rerender } = renderBar({ namn: "Volvo" });
    const user = userEvent.setup();
    const field = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    await user.clear(field);
    await user.type(field, "Saab");

    // The navigation lands: the filter axis is now applied, the name is untouched.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={["63120"]}
        kommun={[]}
      />,
    );

    expect(field).toHaveValue("Saab");

    // ...and the gate still works for the case it exists for: an external navigation that DOES
    // change the applied name re-seeds the field, so Back does not leave a stale value behind.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Scania"
        sni={["63120"]}
        kommun={[]}
      />,
    );
    expect(field).toHaveValue("Scania");
  });

  it("leaves the dirty field dirty — the commit does not clear or apply it", async () => {
    renderBar({ namn: "Volvo" });
    const user = userEvent.setup();
    const field = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    await user.clear(field);
    await user.type(field, "Saab");
    await pickBransch(user, "webb", "63120 Webbportaler");

    // The filter applied; the half-typed name is still the user's to submit or abandon. The
    // re-seed is gated on `namn`, which a filter commit never changes, so it cannot fire here.
    expect(field).toHaveValue("Saab");
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "Volvo", sni: ["63120"], kommun: [] }),
      { scroll: false },
    );
  });
});

describe("ForetagSokSearchbar — the filter change is announced (WCAG 4.1.3)", () => {
  it("mounts one polite region EMPTY, then names the OBJECT that changed", async () => {
    const { container } = renderBar();
    const user = userEvent.setup();

    // Present before the first change and empty: a live region mounted with its content already in
    // place is not reliably announced.
    const region = container.querySelector('[aria-live="polite"].sr-only');
    expect(region).toBeInTheDocument();
    expect(region).toHaveTextContent("");

    await pickBransch(user, "system", "62020 Systemutveckling och programvarutveckling");

    // The FILTER change, never the result count — the count arrives with the streamed results.
    // And it names the NODE, not the axis: this assertion read "Filtret Bransch är tillagt." until
    // 2026-07-29, i.e. it PINNED the defect below.
    expect(region).toHaveTextContent(
      "Filtret Systemutveckling och programvarutveckling är tillagt.",
    );
  });

  /**
   * The reason the announcement may not name the axis, measured rather than argued.
   *
   * An axis-named announcement produces a byte-identical string on the second change in a row.
   * React bails out on `Object.is`, the DOM never mutates, and `aria-live` never fires — so every
   * change after the first is TOTAL SILENCE to a screen reader (WCAG 4.1.3). Chromium, before the
   * fix: ticking Upplands Väsby then Vallentuna announced "Filtret Ort eller län är tillagt."
   * twice. `jobb-hero-search.tsx:370-379` documents the same trap in prose.
   *
   * Two picks in a row is exactly the sequence `useOptimistic` cannot survive in jsdom, so this
   * asserts on the STRINGS PUSHED TO THE REGION rather than on the resulting selection: each pick
   * is taken against a re-rendered bar seeded with what the previous pick applied — the same shape
   * `page.tsx` produces from the URL.
   */
  it("names a DIFFERENT object for a second pick, so the region actually changes", async () => {
    const { container, rerender } = renderBar();
    const user = userEvent.setup();
    const region = container.querySelector('[aria-live="polite"].sr-only');

    await pickBransch(user, "system", "62020 Systemutveckling och programvarutveckling");
    const first = region?.textContent ?? "";

    // The commit landed: the bar re-renders with the first pick applied, as the URL would deliver.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn=""
        sni={["62020"]}
        kommun={[]}
      />,
    );
    await pickBransch(user, "webb", "63120 Webbportaler");
    const second = region?.textContent ?? "";

    expect(first).toBe(
      "Filtret Systemutveckling och programvarutveckling är tillagt.",
    );
    expect(second).toBe("Filtret Webbportaler är tillagt.");
    // The assertion that matters: had either named the axis, these two would be equal and the
    // second announcement would never have reached a screen reader.
    expect(second).not.toBe(first);
  });

  it("announces a removal by the name of the chip that went", async () => {
    const { container } = renderBar({ kommun: ["0180"] });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Ta bort Stockholm" }));

    expect(
      container.querySelector('[aria-live="polite"].sr-only'),
    ).toHaveTextContent("Filtret Stockholm är borttaget.");
  });
});

/**
 * Every announcement string this surface can emit, and the branch that chooses it.
 *
 * Three of the five keys reached no assertion at all when this was first written, and BOTH
 * add/remove discriminations could be hardcoded to `true` with the suite green (test-writer M6-M10).
 * A key nothing asserts is a key nothing stops from silently becoming the wrong sentence.
 */
describe("ForetagSokSearchbar — every announcement branch", () => {
  const region = (c: HTMLElement) =>
    c.querySelector('[aria-live="polite"].sr-only');

  it("announces a REMOVAL, not an addition, when the ort popover unticks", async () => {
    const { container } = renderBar({ kommun: ["0180"] });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Välj ort eller län" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(screen.getByRole("checkbox", { name: "Stockholm" }));

    // `next.length > orter.length` chooses the verb. Hardcode it to `true` and this goes red.
    expect(region(container)).toHaveTextContent("Filtret Stockholm är borttaget.");
  });

  it("announces a REMOVAL, not an addition, when the bransch popover unticks", async () => {
    const { container } = renderBar({ sni: ["62020"] });
    const user = userEvent.setup();

    await pickBransch(user, "system", "62020 Systemutveckling och programvarutveckling");

    // `next.size > sniSelected.size` chooses the verb here. Same class as the ort case above.
    expect(region(container)).toHaveTextContent(
      "Filtret Systemutveckling och programvarutveckling är borttaget.",
    );
  });

  it("uses its own sentence when the BRANSCH axis is cleared wholesale", async () => {
    const { container } = renderBar({ sni: ["62010", "62020"] });
    const user = userEvent.setup();

    const dialog = await openBransch(user);
    await user.click(within(dialog).getByRole("button", { name: "Rensa val" }));

    expect(region(container)).toHaveTextContent("Alla branschfilter är borttagna.");
  });

  /**
   * The BULK branch of `ortChangeLabel`, which nothing reached until code-reviewer measured it: both
   * harnesses filtered "Hela …" out of their row selectors, so `changed.length > 1` and both
   * `t("ortLabel")` fallbacks were unasserted inside a block whose docblock claims to cover every
   * announcement this surface can emit. Swapping `return lan` for `return t("ortLabel")` left the
   * suite green — a survivor in the one function that exists because an axis-named announcement
   * silences every change after the first.
   *
   * Naming the län rather than its 26 kommuner is the point: a screen reader cannot use a list, and
   * naming the axis would be the original defect again.
   */
  it("names the LÄN when one action changes several orter at once", async () => {
    const { container } = renderBar();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Välj ort eller län" }));
    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(
      screen.getByRole("checkbox", { name: "Hela Stockholms län" }),
    );

    expect(region(container)).toHaveTextContent(
      "Filtret Stockholms län är tillagt.",
    );
    // And it really was a multi-code change — otherwise this would be measuring the single-code
    // branch under a bulk-looking label.
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: ["0180", "0181"] }),
      { scroll: false },
    );
  });

  it("uses its own sentence when the ORT axis is cleared wholesale", async () => {
    const { container } = renderBar({ kommun: ["0180", "0181"] });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Välj ort eller län" }));
    // Still plain "Rensa": the ORT dialog is the region/municipality cascade, a
    // different component reading a different namespace. Only the bransch popover
    // and the criterion dialog render `CriterionPicker`, whose label #1146
    // qualified to "Rensa val".
    await user.click(
      within(screen.getByRole("dialog")).getByRole("button", { name: "Rensa" }),
    );

    expect(region(container)).toHaveTextContent("Alla ortfilter är borttagna.");
  });

  it("uses its own sentence when the WHOLE search is cleared", async () => {
    const { container } = renderBar({ namn: "Volvo", kommun: ["0180"] });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Rensa sökningen" }));

    expect(region(container)).toHaveTextContent("Sökningen är rensad.");
  });

  /**
   * V1's sibling, in the one channel V1 itself does not cover. `{namn}` in the announcement keys is
   * an ICU parameter with no type guard on it, unlike `commit`, whose `FilterSelection` has no name
   * field at all. A future edit that passed the field's value here would leak a pnr-shaped string
   * into a live region — announced aloud, and present in the DOM.
   */
  it("never interpolates the typed value into an announcement", async () => {
    const { container } = renderBar({ kommun: ["0180"] });
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      PNR_SHAPED,
    );
    await user.click(screen.getByRole("button", { name: "Ta bort Stockholm" }));

    expect(region(container)).toHaveTextContent("Filtret Stockholm är borttaget.");
    expect(region(container)?.textContent ?? "").not.toContain(PNR_SHAPED);
  });
});

/**
 * Focus after a live commit that UNMOUNTS the control which was clicked (design-reviewer M3).
 *
 * The chip × removes its own chip; without a deliberate move the browser drops focus to `<body>`
 * and the next Tab restarts at the top of the document. Live commit makes the chip × the primary
 * filter gesture on this surface, so removing three chips would traverse the whole page three times.
 */
/**
 * Three guarantees that a mutation sweep found unpinned after round 2 — each one survived being
 * removed with the whole suite green, which is the only reason they are here rather than trusted.
 */
describe("ForetagSokSearchbar — round-2 guarantees, pinned", () => {
  it("puts role=status on the live region, not only aria-live", () => {
    const { container } = renderBar();
    const region = container.querySelector("p.sr-only");
    // `role="status"` carries `aria-atomic="true"`, so a partial update is read as one sentence
    // rather than in fragments. The precedent this mirrors (`jobb-hero-search.tsx`) has both.
    expect(region).toHaveAttribute("role", "status");
    expect(region).toHaveAttribute("aria-live", "polite");
  });

  it("describes the field with the unapplied line, and only while it diverges", async () => {
    renderBar();
    const user = userEvent.setup();
    const field = screen.getByLabelText("Företagsnamn eller organisationsnummer");
    const hintOnly = field.getAttribute("aria-describedby");
    expect(hintOnly).toBeTruthy();

    await user.type(field, "Volvo");

    const described = field.getAttribute("aria-describedby") ?? "";
    // Two ids now, and the second one is the line's — so a screen reader hears WHY the field is not
    // applied as part of the field itself, rather than having to find a sentence elsewhere.
    expect(described.split(" ")).toHaveLength(2);
    const unappliedId = described.split(" ")[1] ?? "";
    expect(document.getElementById(unappliedId)).toHaveTextContent(
      "Ändringen i namnfältet tillämpas när du väljer Sök företag.",
    );

    await user.clear(field);
    expect(field.getAttribute("aria-describedby")).toBe(hintOnly);
  });

  /**
   * #1075 — the unapplied line is gated on the NAME branch (`!isOrgNrValue`), so widening the
   * normaliser also widens what suppresses it. A twelve-digit draft now describes the field with the
   * hint alone, exactly as a ten-digit one already did: the filter axes are meaningless on the org.nr
   * path, so promising that Sök företag will apply them would be false.
   */
  it("does not describe the field with the unapplied line for a twelve-digit draft", async () => {
    renderBar();
    const user = userEvent.setup();
    const field = screen.getByLabelText("Företagsnamn eller organisationsnummer");
    const hintOnly = field.getAttribute("aria-describedby");

    await user.type(field, PNR_SHAPED_12);

    expect(field.getAttribute("aria-describedby")).toBe(hintOnly);
    expect(
      screen.queryByText(
        "Ändringen i namnfältet tillämpas när du väljer Sök företag.",
      ),
    ).not.toBeInTheDocument();
  });

  /**
   * The two decisions that meet at the org.nr lookup's `focus()` call: the answer SURVIVES a filter
   * commit (so the fetch is neither cancelled nor discarded), and a filter commit PLACES focus. Both
   * are right; the late `focus()` is only right if it checks that nothing else has claimed focus
   * since the submit. Otherwise a lookup resolving 300-900 ms after a chip click yanks focus off the
   * chip row onto a section the user did not ask for (WCAG 3.2.1).
   *
   * The fetch is held open deliberately so the chip click lands INSIDE the request window — that
   * window is the whole subject, and a resolved-immediately mock would measure a different one.
   */
  it("does not steal focus when an org.nr lookup resolves after a chip commit", async () => {
    let release: (r: Response) => void = () => {};
    const held = new Promise<Response>((resolve) => {
      release = resolve;
    });
    global.fetch = vi.fn().mockReturnValue(held);

    const { container } = renderBar({ kommun: ["0180"] });
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    // The lookup is in flight. Remove the chip — the commit places focus on the ort trigger, since
    // this was the only chip.
    await user.click(screen.getByRole("button", { name: "Ta bort Stockholm" }));
    const afterCommit = document.activeElement;
    expect(afterCommit).toBe(
      screen.getByRole("button", { name: "Välj ort eller län" }),
    );

    // Now the answer arrives.
    release(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    expect(await screen.findByText("Volvo AB")).toBeInTheDocument();

    // The answer RENDERS — it is not discarded — but focus stays where the user's last gesture put
    // it. Withholding the move is the whole fix; withholding the answer would be a different bug.
    expect(document.activeElement).toBe(afterCommit);
    expect(document.activeElement).not.toBe(
      container.querySelector('section[tabindex="-1"]'),
    );
  });
});

describe("ForetagSokSearchbar — focus survives a live commit", () => {
  it("moves to the chip that took the removed one's place", async () => {
    renderBar({ kommun: ["0180", "0181"] });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Ta bort Stockholm" }));

    expect(document.activeElement).toBe(
      screen.getByRole("button", { name: "Ta bort Södertälje" }),
    );
  });

  it("falls back to the axis trigger when the last chip goes", async () => {
    renderBar({ kommun: ["0180"] });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Ta bort Stockholm" }));

    expect(document.activeElement).toBe(
      screen.getByRole("button", { name: "Välj ort eller län" }),
    );
  });

  it("moves to the name field when the whole search is cleared", async () => {
    renderBar({ namn: "Volvo", kommun: ["0180"] });
    const user = userEvent.setup();

    // The clear control removes ITSELF — `showClear` goes false inside the same transition.
    await user.click(screen.getByRole("button", { name: "Rensa sökningen" }));

    expect(document.activeElement).toBe(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
    );
  });
});

describe("ForetagSokSearchbar — bransch popover (#999)", () => {
  it("filters at ALL THREE SNI levels, not leaves only", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    const dialog = await openBransch(user);
    // "verksamhet" occurs in the SECTION name, a DIVISION name and a LEAF name in the fixture. The
    // control this replaced searched all three; the picker used to match leaves only, which is the
    // half of "hard to find" (#999) that no popover shape fixes on its own.
    await user.type(within(dialog).getByLabelText("Sök bransch"), "verksamhet");

    for (const name of [
      "J Informations- och kommunikationsverksamhet", // section
      "62 Dataprogrammering, datakonsultverksamhet", // division
      "62010 Datakonsultverksamhet", // leaf
    ]) {
      expect(within(dialog).getByRole("checkbox", { name })).toBeInTheDocument();
    }
    // Still no network: the filter runs over the reference the page already loaded.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("ticking a matched DIVISION selects its whole expansion", async () => {
    renderBar();
    const user = userEvent.setup();

    await pickBransch(user, "datapro", "62 Dataprogrammering, datakonsultverksamhet");

    // Both of division 62's leaves ride the URL — a parent is stored as its leaves, never as itself.
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: ["62010", "62020"], kommun: [] }),
      { scroll: false },
    );
  });

  it("a partially selected division reads as mixed, so a click cannot silently deselect", async () => {
    // One leaf of division 62 applied — what picking a single leaf and searching produces.
    renderBar({ sni: ["62010"] });
    const user = userEvent.setup();

    const dialog = await openBransch(user);
    await user.type(within(dialog).getByLabelText("Sök bransch"), "datapro");

    expect(
      within(dialog).getByRole("checkbox", {
        name: "62 Dataprogrammering, datakonsultverksamhet",
      }),
    ).toHaveAttribute("aria-checked", "mixed");
  });

  /**
   * Seeded as APPLIED rather than picked twice in a row, and the reason is a real limit of the
   * harness rather than a shortcut. `useOptimistic` holds its overlay only while the transition that
   * set it is in flight; `router.push` is a synchronous mock here, so the transition ends at once and
   * the overlay reverts to props that never changed. Two sequential picks would therefore measure
   * jsdom, not the component. What a SECOND pick adds to an ALREADY-APPLIED first is the same
   * question without that dependency — and the cumulative-under-flight behaviour is verified
   * rendered against the running stack instead (see the PR body).
   */
  it("is MULTI-select: a second branch is added to the applied one, not swapped for it", async () => {
    renderBar({ sni: ["62020"] });
    const user = userEvent.setup();

    await pickBransch(user, "webb", "63120 Webbportaler");

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: ["62020", "63120"], kommun: [] }),
      { scroll: false },
    );
  });

  it("chips decompose upward: a fully selected division reads as ONE division chip", () => {
    renderBar({ sni: ["62010", "62020"] });
    expect(
      screen.getByText("Dataprogrammering, datakonsultverksamhet"),
    ).toBeInTheDocument();
    // Not also its two leaves — the decomposition emits the fewest nodes that describe the set.
    expect(screen.queryByText("Datakonsultverksamhet")).not.toBeInTheDocument();
  });

  it("chips decompose to the SECTION when every leaf under it is selected", () => {
    renderBar({ sni: ["62010", "62020", "63110", "63120"] });
    expect(
      screen.getByText("Informations- och kommunikationsverksamhet"),
    ).toBeInTheDocument();
    expect(
      screen.queryByText("Dataprogrammering, datakonsultverksamhet"),
    ).not.toBeInTheDocument();
  });

  it("removing a chip removes exactly that node's leaves, leaving the rest", async () => {
    renderBar({ sni: ["62010", "62020", "63110"] });
    const user = userEvent.setup();

    await user.click(
      screen.getByRole("button", {
        name: "Ta bort Dataprogrammering, datakonsultverksamhet",
      }),
    );

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: ["63110"], kommun: [] }),
      { scroll: false },
    );
  });

  it("carries the one sentence that explains what a click here does", async () => {
    renderBar();
    const user = userEvent.setup();
    const dialog = await openBransch(user);
    // "Välj bransch" names the control; it does not say that a checkbox on a parent selects its whole
    // subtree, or that several branches can be picked. One click can select 52 codes, and after #999
    // this is the only place on the surface that says so.
    expect(
      within(dialog).getByText(
        "Välj en eller flera branscher. Du kan välja en hel avdelning, en huvudgrupp eller enskilda koder.",
      ),
    ).toBeInTheDocument();
    // And the sentence that paid no rent is gone: a hint under a field already labelled "Sök bransch".
    expect(
      within(dialog).queryByText("Skriv för att smalna av listan över branscher."),
    ).not.toBeInTheDocument();
  });

  it("has the panel's counter region mounted and empty before the first pick", async () => {
    // The 0→1 transition is the one that matters most, and it is exactly the case a region gated on
    // "something is selected" gets wrong: it MOUNTS carrying the new text, which is the form the
    // house has ruled unreliable. So the region has to be there, empty, from the moment the panel is.
    renderBar();
    const user = userEvent.setup();
    const dialog = await openBransch(user);
    const region = dialog.querySelector("span[aria-live='polite']");
    expect(region).not.toBeNull();
    expect(region).toHaveTextContent("");
    // Nothing to clear at zero, so the clear control IS conditional — only the region is not.
    expect(within(dialog).queryByRole("button", { name: "Rensa val" })).not.toBeInTheDocument();
  });

  it("the panel and the chip row report the SAME number for the same axis", async () => {
    // A section is four leaves that decompose to ONE chip. Counting `selected.size` would put
    // "4 valda branscher" in the panel beside a single chip outside it. Seeded as APPLIED rather
    // than picked, because the pick now navigates and the overlay does not outlive a mocked push.
    renderBar({ sni: ["62010", "62020", "63110", "63120"] });
    const user = userEvent.setup();
    const dialog = await openBransch(user);

    const counter = within(dialog).getByText("1 vald bransch");
    expect(counter).toBeInTheDocument();
    // Announced, because the number changes without focus moving: one click on a section takes it
    // from nothing to a whole subtree, and the row's own aria-checked does not carry the total.
    expect(counter).toHaveAttribute("aria-live", "polite");
    await user.keyboard("{Escape}");
    expect(
      screen.getByRole("button", {
        name: "Ta bort Informations- och kommunikationsverksamhet",
      }),
    ).toBeInTheDocument();
  });

  it("above the cap the chips collapse to one summary chip that clears the axis", async () => {
    // Ten divisions, one leaf picked in each — a decomposition of ten chips, which is what the cap
    // exists to prevent. Reachable by ticking ten leaves; nothing here is a shape the UI cannot make.
    const wide: CriterionReference = {
      ...REFERENCE,
      sni: [
        {
          code: "C",
          name: "Tillverkning",
          divisions: Array.from({ length: 10 }, (_, i) => ({
            code: `${10 + i}`,
            name: `Huvudgrupp ${10 + i}`,
            leaves: [
              { code: `${10 + i}100`, name: `Detalj ${10 + i}100` },
              { code: `${10 + i}200`, name: `Detalj ${10 + i}200` },
            ],
          })),
        },
      ],
    };
    render(
      <ForetagSokSearchbar
        reference={wide}
        referenceOk
        namn=""
        sni={Array.from({ length: 10 }, (_, i) => `${10 + i}100`)}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();

    expect(screen.getByText("10 valda branscher")).toBeInTheDocument();
    expect(screen.queryByText("Detalj 10100")).not.toBeInTheDocument();

    // The summary REPORTS. It must not carry the same × as the per-branch chips: identical pixels,
    // and one of them drops the entire bransch axis with no undo.
    const summary = screen.getByText("10 valda branscher").closest(".jp-chip")!;
    expect(within(summary as HTMLElement).queryByRole("button")).not.toBeInTheDocument();

    // The bulk removal is a sibling with visible text, at the same size as the row's other control —
    // and it now APPLIES the emptied axis rather than editing a draft.
    await user.click(screen.getByRole("button", { name: "Ta bort alla branscher" }));
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: [] }),
      { scroll: false },
    );
  });
});

describe("ForetagSokSearchbar — ort", () => {
  it("seeds ort chips from the URL kommun, and removing one APPLIES immediately", async () => {
    renderBar({ kommun: ["0180", "0181"] });
    const user = userEvent.setup();

    expect(
      screen.getByRole("button", { name: "Ta bort Stockholm" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Ta bort Södertälje" }),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Ta bort Stockholm" }));

    // Removing a chip now APPLIES — the whole remaining state, never a delta.
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: ["0181"] }),
      { scroll: false },
    );
  });

  it("opens the cascade popover and applies a kommun on the pick", async () => {
    renderBar();
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Välj ort eller län" }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Stockholms län" }));
    await user.click(screen.getByRole("checkbox", { name: "Stockholm" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: ["0180"] }),
      { scroll: false },
    );
  });
});

describe("ForetagSokSearchbar — unified name/org.nr field", () => {
  it("routes a non-org.nr value to the NAME branch: pushes the shareable URL, never fetches", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), "Volvo");
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "Volvo", sni: [], kommun: [] }),
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("REFUSES a personnummer-shaped value LOCALLY: never fetches, never navigates (D8(c))", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), PNR_SHAPED);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(
      await screen.findByText(/Det ser ut som ett personnummer/i),
    ).toBeInTheDocument();
    // The value left the browser by NO path: not the org.nr POST, not the name-branch URL.
    expect(fetchMock).not.toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();
  });

  /**
   * #1075 — the money test. Before the widening a twelve-digit personnummer form failed the org.nr
   * test and took the NAME branch, so `router.push` put it in `?namn=`: the address bar, history, a
   * re-shared link and the access log. ADR 0087 D8(c); CLAUDE.md §5 ranks this guard highest.
   */
  it.each([PNR_SHAPED_12, PNR_SHAPED_12_HYPHEN])(
    "REFUSES the twelve-digit personnummer form %s LOCALLY: never fetches, never navigates",
    async (written) => {
      const fetchMock = vi.fn();
      global.fetch = fetchMock;
      renderBar();
      const user = userEvent.setup();

      await user.type(
        screen.getByLabelText("Företagsnamn eller organisationsnummer"),
        written,
      );
      await user.click(screen.getByRole("button", { name: "Sök företag" }));

      expect(
        await screen.findByText(/Det ser ut som ett personnummer/i),
      ).toBeInTheDocument();
      expect(fetchMock).not.toHaveBeenCalled();
      expect(push).not.toHaveBeenCalled();
    },
  );

  it("routes a twelve-digit LEGAL-ENTITY form to the org.nr branch, POSTing the stripped ten", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR_12,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByText("Volvo AB")).toBeInTheDocument();
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    // The raw string never crosses the wire — only the value the domain would derive.
    expect(JSON.parse(init.body as string)).toEqual({ organizationNumber: VALID_ORGNR });
    expect(push).not.toHaveBeenCalled();
  });

  it.each(["55601257901", "189001011234"])(
    "leaves %s on the NAME branch — outside the written-form contract on both sides",
    async (outside) => {
      const fetchMock = vi.fn();
      global.fetch = fetchMock;
      renderBar();
      const user = userEvent.setup();

      await user.type(
        screen.getByLabelText("Företagsnamn eller organisationsnummer"),
        outside,
      );
      await user.click(screen.getByRole("button", { name: "Sök företag" }));

      expect(push).toHaveBeenCalledWith(
        buildForetagSokHref({ namn: outside, sni: [], kommun: [] }),
      );
      expect(fetchMock).not.toHaveBeenCalled();
    },
  );

  it("routes a 10-digit value to the ORG.NR branch: POSTs, never the URL", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    global.fetch = fetchMock;
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByText("Volvo AB")).toBeInTheDocument();
    expect(screen.getByText("Göteborg", { exact: false })).toBeInTheDocument();

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/foretag/sok");
    expect(JSON.parse(init.body as string)).toEqual({ organizationNumber: VALID_ORGNR });
    expect(push).not.toHaveBeenCalled();
  });

  it("ignores the applied bransch/ort axes on an org.nr lookup (org.nr never enters the URL)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    global.fetch = fetchMock;
    renderBar({ sni: ["62020"], kommun: ["0180"] });
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    await screen.findByText("Volvo AB");
    // Even with the bransch/ort axes applied, the org.nr path POSTs only the org.nr and never navigates.
    expect(JSON.parse((fetchMock.mock.calls[0]![1] as RequestInit).body as string)).toEqual({
      organizationNumber: VALID_ORGNR,
    });
    expect(push).not.toHaveBeenCalled();
  });

  it("renders a Bevaka affordance on the org.nr result and follows via the org.nr", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    followActionMock.mockResolvedValue({ success: true, companyWatchId: "cw-new" });
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    const bevaka = await screen.findByRole("button", { name: "Bevaka Volvo AB" });
    await user.click(bevaka);

    expect(followActionMock).toHaveBeenCalledWith(VALID_ORGNR);
    expect(await screen.findByText("Bevakar")).toBeInTheDocument();
  });

  it("shows the not-found state when the register has no such org.nr", async () => {
    global.fetch = vi.fn().mockResolvedValue(orgNrResponse(null));
    renderBar();
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), VALID_ORGNR);
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(
      await screen.findByText("Inget företag med det numret"),
    ).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("surfaces a concrete retry time on 429 (Retry-After → seconds in copy)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(null, { status: 429, headers: { "Retry-After": "30" } }),
    );
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByText(/Vänta 30 sekunder/i)).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("renders the technical-error state on a non-ok backend response", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 502 }));
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /Sökningen kunde inte genomföras/i,
    );
    expect(push).not.toHaveBeenCalled();
  });
});

/**
 * ADR 0087 D8(c) — the CALL-SITE pin, not only the rule.
 *
 * `parseNamn`'s gate is unit-tested next door, but a rule test cannot see what the FORM hands the
 * browser. This suite serialises the form exactly as a native GET would and asserts the URL it
 * produces — which is the instrument that measured the defect in the first place: on HEAD before the
 * fix, typing `1010101010` and reading `new FormData(form)` produced
 * `/foretag/sok?namn=1010101010`. Without this, a later edit could restore `name="namn"` on the
 * visible input and every other test in this file would stay green.
 */
describe("ForetagSokSearchbar — what a NATIVE GET would carry (D8(c) call-site pin)", () => {
  function nativeGetHref(input: HTMLElement): string {
    const form = input.closest("form");
    if (form === null) throw new Error("the search input is not inside a form");
    const params = new URLSearchParams();
    for (const [key, value] of new FormData(form).entries()) {
      params.append(key, String(value));
    }
    return `${form.getAttribute("action")}?${params.toString()}`;
  }

  it("never carries the TYPED value — a ten-digit draft cannot reach ?namn=", async () => {
    renderBar();
    const user = userEvent.setup();
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    await user.type(input, PNR_SHAPED);

    const href = nativeGetHref(input);
    expect(href).not.toContain(PNR_SHAPED);
    expect(href).not.toContain("namn=");
  });

  it("does not carry a typed ORDINARY name either — only what is already applied", async () => {
    // The rule is not "gate the pnr class in the form"; it is "the form never carries the draft".
    // A draft-carrying form would be one predicate away from leaking again.
    renderBar();
    const user = userEvent.setup();
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    await user.type(input, "Volvo");

    expect(nativeGetHref(input)).not.toContain("Volvo");
  });

  it("carries the APPLIED name (which has passed the server gate) via a hidden input", () => {
    renderBar({ namn: "Volvo", kommun: ["0180"] });
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    const href = nativeGetHref(input);
    expect(href).toContain("namn=Volvo");
    expect(href).toContain("kommun=0180");
  });

  /**
   * The form is the one producer of the code axes that cannot call a URL builder, because a form
   * serialises its own fields. It must therefore emit the same shape the builders do — ONE param
   * per axis — or a native GET writes the REPEATED shape and puts the router-cache collision back
   * (`search-params.ts` documents the mechanism).
   *
   * Two codes, deliberately. The assertion above uses one, where `?kommun=0180` is byte-identical
   * under both shapes — which is exactly why switching the inputs over left the whole suite green
   * and the gap reached review instead of a test (code-reviewer, #1134).
   */
  it("emits ONE param per axis, not one per code", () => {
    renderBar({ sni: ["62010", "62020"], kommun: ["0180", "0181"] });
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");

    const qs = new URLSearchParams(nativeGetHref(input).split("?")[1] ?? "");
    expect(qs.getAll("sni")).toEqual(["62010-62020"]);
    expect(qs.getAll("kommun")).toEqual(["0180-0181"]);
  });

  it("strips the name attribute from the visible input once hydrated", () => {
    renderBar({ namn: "Volvo" });
    // The visible field still SHOWS the applied name; it just does not submit it.
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");
    expect(input).toHaveValue("Volvo");
    expect(input).not.toHaveAttribute("name");
  });

  /**
   * The OTHER side of the hydration split, which `render()` can never reach: every RTL render is a
   * client render, so `hydrated` is `true` in all of the tests above. Server-render the component
   * instead, which is what `getServerSnapshot` (false) actually drives.
   *
   * Without this, replacing `name={hydrated ? undefined : "namn"}` with a bare `name={undefined}`
   * leaves the whole suite green — and silently kills no-JS name search, because the field would
   * submit nothing and every no-JS search would return the entire register.
   */
  it("KEEPS the name attribute before hydration, so a no-JS search still works", () => {
    // The fixture carries an APPLIED name deliberately. With `namn=""` the second assertion would be
    // vacuous — the hidden input is gated on `namn.length > 0`, so it is absent whatever `hydrated`
    // says, and dropping the `hydrated &&` guard would leave the test green.
    const html = renderToString(
      <NextIntlClientProvider
        locale="sv"
        // BranschPopover is MOUNTED unconditionally in this subtree (`open` controls
        // visibility, not mounting), so it reaches `components.criterionPicker`.
        // Seeding only `pages` was green by accident: the clear control is gated on
        // `pickedCount > 0` and this fixture passes `sni={[]}`, so the key was never
        // read. A future case with a non-empty `sni` would render the raw key instead
        // of "Rensa" (code-reviewer, #1146). The payload fitness function cannot see
        // this — it excludes `*.test.tsx` by design.
        messages={{ pages: svPages, components: svComponents }}
      >
        <ForetagSokSearchbar
          reference={REFERENCE}
          referenceOk
          namn="Volvo"
          sni={[]}
          kommun={[]}
        />
      </NextIntlClientProvider>,
    );
    // Exactly ONE `name="namn"` pre-hydration — the visible input. Counting rather than matching a
    // literal attribute order: two would mean the visible input and the hidden applied-name input
    // both submit, which is the state the hydration split exists to make impossible.
    expect(html.match(/name="namn"/g) ?? []).toHaveLength(1);
    expect(html).toContain('value="Volvo"');
  });
});

describe("ForetagSokSearchbar — degraded reference", () => {
  it("disables the bransch field with a civil notice; the name search still works", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;
    render(
      <ForetagSokSearchbar
        reference={{ sniVersion: "", kommunVersion: "", sni: [], lan: [] }}
        referenceOk={false}
        namn=""
        sni={[]}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();

    expect(screen.getByRole("button", { name: "Välj bransch" })).toBeDisabled();
    expect(
      screen.getByText(/Branschlistan kunde inte laddas just nu/i),
    ).toBeInTheDocument();

    // The reference-free name field keeps working.
    await user.type(screen.getByLabelText("Företagsnamn eller organisationsnummer"), "Acme");
    await user.click(screen.getByRole("button", { name: "Sök företag" }));
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "Acme", sni: [], kommun: [] }),
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("still shows an APPLIED bransch filter it cannot name, and lets you remove it", async () => {
    // Producible by a path in src/: the reference endpoint is down (page.tsx falls back to
    // EMPTY_REFERENCE) while the visitor arrives on a shared link carrying ?sni=. page.tsx passes NO
    // allowlist to normalizeCodes in that branch, so the axis survives and IS applied to the results
    // below — but an empty tree means no chip can be named for it. Before this branch the row rendered
    // nothing: an active, invisible, unremovable filter.
    render(
      <ForetagSokSearchbar
        reference={{ sniVersion: "", kommunVersion: "", sni: [], lan: [] }}
        referenceOk={false}
        namn=""
        sni={["62010", "62020"]}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();

    // Counted in the unit it can honestly report: codes, not named branches.
    expect(screen.getByText("2 branschkoder")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Ta bort alla branscher" }));

    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: [] }),
      { scroll: false },
    );
  });

  it("keeps the org.nr answer OUT of the form's width cap, and the clear control on the rail", () => {
    // #1090, both halves, pinned at the only level jsdom can see: structure and classes. The widths
    // themselves (672 vs 1136) and the text's x-position are rendered measurements in the PR body —
    // jsdom has no layout. What it CAN pin is that the org.nr <section> is not inside the capped
    // wrapper, and that the clear control carries the modifier that cancels the button's padding.
    const { container } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );
    const capped = container.querySelector(".max-w-2xl");
    expect(capped).not.toBeNull();
    const answer = container.querySelector("section[aria-live='polite']");
    expect(answer).not.toBeNull();
    expect(capped!.contains(answer!)).toBe(false);

    expect(capped!.contains(container.querySelector("form"))).toBe(true);

    // The class alone guarantees nothing: `.jp-btn--flush` is CSS-scoped to `:first-child`, so a
    // preceding sibling, or an unconditionally rendered chip list, would silently kill the offset
    // with every gate green (guard:css reads the stylesheet, jsdom has no cascade, eslint sees a
    // valid string). A WRAPPER would not kill the OFFSET — the button stays its parent's first child
    // — but it would un-scope the modifier from the ROW, which is the harm `:first-child` exists to
    // prevent; that half is caught by the inert-arm test below, not by this assertion. The POSITION
    // is structural, which is exactly what jsdom can see, so it is asserted rather than excused.
    const clear = screen.getByRole("button", { name: "Rensa sökningen" });
    expect(clear).toHaveClass("jp-btn--flush");
    expect(clear.parentElement!.firstElementChild).toBe(clear);
  });

  it("leaves the flush offset inert when chips precede the clear control", () => {
    // The other arm, and the reason the modifier is CSS-scoped rather than always-on: here the
    // negative start margin would eat 18px of a 12px gap and lap the last chip.
    render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn=""
        sni={["62010"]}
        kommun={[]}
      />,
    );
    const clear = screen.getByRole("button", { name: "Rensa sökningen" });
    expect(clear.parentElement!.firstElementChild).not.toBe(clear);
  });

  it("never renders an empty chip list", () => {
    render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );
    // A pure name search shows the clear control but no chips. A <ul> with no <li> is announced as
    // "list, 0 items" — a container for nothing.
    expect(screen.getByRole("button", { name: "Rensa sökningen" })).toBeInTheDocument();
    expect(screen.queryByRole("list")).not.toBeInTheDocument();
  });
});

/**
 * The live-review fixes (Klas, 2026-07-25). Each of these was a measured complaint about the shipped
 * S2 surface, not a hypothetical — the design framing is
 * `docs/reviews/2026-07-25-foretag-sok-followup-design.md`.
 */
describe("ForetagSokSearchbar — the live-review fixes", () => {
  it("CLEARS the whole search and NAVIGATES, not just the draft filter (finding 6)", async () => {
    renderBar({ namn: "Volvo", kommun: ["0180"] });
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Rensa sökningen" }));

    // It navigates: the old version nulled two draft fields and left the applied URL filter in
    // place, so the results below kept answering a search the controls no longer showed.
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: [] }),
    );
    // ...and the name field is cleared too, which it never was.
    expect(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
    ).toHaveValue("");
  });

  it("offers the clear control for a PURE NAME search, which had no clear path at all", () => {
    // The old gate was `hasFilter` (bransch or ort), so the most common search — a name — could not
    // be cleared. That is the whole of finding 6.
    renderBar({ namn: "Volvo" });
    expect(
      screen.getByRole("button", { name: "Rensa sökningen" }),
    ).toBeInTheDocument();
  });

  it("hides the clear control when there is genuinely nothing to clear", () => {
    renderBar();
    expect(
      screen.queryByRole("button", { name: "Rensa sökningen" }),
    ).not.toBeInTheDocument();
  });

  it("stays hidden while the user TYPES — the gate reads applied state, never the draft", async () => {
    // Reading the draft made the control appear on the first keystroke and shove the results 64px
    // down mid-typing (measured). Every other clear test clicks without typing, so without this the
    // gate can be widened back to `value` with the whole suite green.
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      "Volvo",
    );

    expect(
      screen.queryByRole("button", { name: "Rensa sökningen" }),
    ).not.toBeInTheDocument();
  });

  it("renders the org.nr answer through the shared register table (finding 5)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    renderBar();
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));

    // A real table with the register's own columns — not a hand-rolled card that can drift from it.
    const table = await screen.findByRole("table", {
      name: "Företag som matchar organisationsnumret",
    });
    expect(table).toBeInTheDocument();
    expect(within(table).getByText("Volvo AB")).toBeInTheDocument();
    // The seat renders WITHOUT the SCB code (finding 7) — it used to read "Göteborg (1480)".
    expect(within(table).queryByText(/\(1480\)/)).not.toBeInTheDocument();
  });

  it("groups the narrowing controls under their own labelled section (finding 4)", () => {
    renderBar();
    // The two interaction models differ — the name SUBMITS, these narrow — so the difference is
    // drawn as a group rather than explained in more hint prose (which finding 9 asked to reduce).
    expect(screen.getByRole("group", { name: "Avgränsa" })).toBeInTheDocument();
  });

  it("drops both filter hints, whose triggers already say the same thing (finding 9)", () => {
    renderBar();
    expect(
      screen.queryByText("Välj ett eller flera län eller kommuner."),
    ).not.toBeInTheDocument();
    // The bransch hint described a typeahead that no longer exists; "Välj bransch" names itself.
    expect(
      screen.queryByText("Skriv och välj en bransch."),
    ).not.toBeInTheDocument();
  });

  it("names the bransch trigger without a <label htmlFor> (WCAG 2.5.3 label-in-name)", () => {
    renderBar();
    // A label pointed at a button becomes its accessible name and overrides the visible text. The
    // trigger must therefore be findable by what it SAYS, and not by the field heading beside it.
    expect(
      screen.getByRole("button", { name: "Välj bransch" }),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("Bransch")).not.toBeInTheDocument();
  });
});

/**
 * The island is rendered without a `key`, so it never remounts. TWO different mechanisms keep it
 * in step with the URL, and they are worth not confusing:
 *
 *  - the FILTER axes derive from props through `useOptimistic`, so they follow the URL by
 *    construction and need no re-seeding at all;
 *  - the NAME FIELD is the one `useState` initialiser left, and it runs once — so without an
 *    explicit re-seed, Back after a search leaves the field showing what you just left while the
 *    URL and results show something else. "Rensa sökningen" makes that reachable in one click.
 *
 * This block said "all three draft pieces are `useState` initialisers" until 2026-07-29. That was
 * true of the draft model #1125 removed, and the component now states the opposite in as many
 * words ("`value` is the last `useState` initialiser left").
 */
describe("ForetagSokSearchbar — the field re-seeds when the applied URL changes", () => {
  it("re-seeds the field and chips when the applied props change (Back)", () => {
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={["62020"]}
        kommun={["0180"]}
      />,
    );
    expect(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
    ).toHaveValue("Volvo");
    expect(screen.getByRole("button", { name: "Ta bort Stockholm" })).toBeInTheDocument();

    // Same component instance, new applied URL — what Back does.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Saab"
        sni={[]}
        kommun={[]}
      />,
    );

    expect(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
    ).toHaveValue("Saab");
    expect(
      screen.queryByRole("button", { name: "Ta bort Stockholm" }),
    ).not.toBeInTheDocument();
  });

  it("re-seeds the BRANSCH chip too, not only the name and orter", () => {
    // The test above asserts only the ort chip, so this one carries the bransch axis. It pins the
    // DECOMPOSITION path — props → `useOptimistic` → `decomposeSelection` → chip — which is how the
    // bransch chip follows the URL now. (It used to name `setBranch(seedBranch(...))` as the
    // mutant it kills. Those symbols no longer exist: #1125 deleted the seed helper along with the
    // draft state, so the named counterfactual was unwritable.)
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn=""
        sni={["62010", "62020"]}
        kommun={[]}
      />,
    );
    expect(
      screen.getByText("Dataprogrammering, datakonsultverksamhet"),
    ).toBeInTheDocument();

    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn=""
        sni={["63110", "63120"]}
        kommun={[]}
      />,
    );

    expect(screen.getByText("Informationstjänster")).toBeInTheDocument();
    expect(
      screen.queryByText("Dataprogrammering, datakonsultverksamhet"),
    ).not.toBeInTheDocument();
  });

  it("follows an axis-only URL change, with the name held constant", () => {
    // What this pins after #1125 is the OVERLAY: with `namn` unchanged, the chips must still follow
    // the props, because they derive from them rather than from a re-seed.
    //
    // Its previous title claimed "the signature is not just the name", and the comment named
    // "truncating the signature to `${namn}`" as the mutant it kills. Both were false on this head:
    // the gate IS `namn` alone, so the named mutant is the shipped code, and this test passes for a
    // different reason than it claimed. Recorded rather than quietly retitled — a test that states
    // the wrong counterfactual teaches the wrong thing about what is guarded.
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={["0180"]}
      />,
    );
    expect(screen.getByRole("button", { name: "Ta bort Stockholm" })).toBeInTheDocument();

    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={["0181"]}
      />,
    );

    expect(screen.getByRole("button", { name: "Ta bort Södertälje" })).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Ta bort Stockholm" }),
    ).not.toBeInTheDocument();
  });

  it("does NOT clobber what the user is typing when the applied props are unchanged", async () => {
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();
    const input = screen.getByLabelText("Företagsnamn eller organisationsnummer");
    await user.clear(input);
    await user.type(input, "Scania");

    // A re-render that does not change the applied search — the draft must survive it, or the
    // re-seed would fight the user on every keystroke-adjacent render.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );

    expect(input).toHaveValue("Scania");
  });
});

/**
 * The org.nr answer's lifetime. Two halves, and the second is the one #1125 changed.
 *
 * It is dropped when the applied NAME moves on — that is supersession: a new name search replaces
 * the answer the field produced. It SURVIVES a filter commit, and that is a deliberate reversal of
 * this PR's first shape (design-reviewer bind, 2026-07-29). Under draft-commit, changing a filter
 * meant pressing the same control that produced the answer; live commit severs that. The lookup is
 * independent of the filter axes by design, and the answer renders as its own headed, rule-separated
 * section precisely so it never reads as part of the browse — so the browse cannot make it stale.
 * Task B silently destroying the result of task A is the ADR 0047 failure this avoids.
 */
describe("ForetagSokSearchbar — the org.nr answer's lifetime", () => {
  it("clears the org.nr result when the applied URL changes", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    const { rerender } = render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Volvo"
        sni={[]}
        kommun={[]}
      />,
    );
    const user = userEvent.setup();

    await user.clear(screen.getByLabelText("Företagsnamn eller organisationsnummer"));
    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));
    expect(await screen.findByText("Volvo AB")).toBeInTheDocument();

    // Back: the applied URL moves, and the stale company row must not sit above it.
    rerender(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn="Saab"
        sni={[]}
        kommun={[]}
      />,
    );

    expect(screen.queryByText("Volvo AB")).not.toBeInTheDocument();
  });

  /**
   * The INVERSE of the drop above, and it is the assertion that would have gone red on the shape
   * this PR originally shipped: `appliedSignature` covered all three axes, so a chip click cleared
   * the answer. The gate is now `namn` alone, which a filter commit provably never changes.
   */
  it("KEEPS the org.nr result across a filter commit", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(orgNrResponse({ company: FOUND_COMPANY, companyWatchId: null }));
    render(
      <ForetagSokSearchbar
        reference={REFERENCE}
        referenceOk
        namn=""
        sni={[]}
        kommun={["0180"]}
      />,
    );
    const user = userEvent.setup();

    await user.type(
      screen.getByLabelText("Företagsnamn eller organisationsnummer"),
      VALID_ORGNR,
    );
    await user.click(screen.getByRole("button", { name: "Sök företag" }));
    expect(await screen.findByText("Volvo AB")).toBeInTheDocument();

    // A live filter commit: remove the applied ort chip. The answer is not its subject.
    await user.click(screen.getByRole("button", { name: "Ta bort Stockholm" }));
    expect(push).toHaveBeenCalledWith(
      buildForetagSokHref({ namn: "", sni: [], kommun: [] }),
      { scroll: false },
    );

    expect(screen.getByText("Volvo AB")).toBeInTheDocument();
  });
});
