# LinkedIn api-host lever (follow-up to #1926) form round — senior-cto-advisor routing (report-only)

- **Date:** 2026-10-03 (report returned ~2026-10-03T10:0xZ)
- **Agent:** `senior-cto-advisor`, report-only, against `.claude/worktrees/jobb-card-density-1828-79e055` at `4e0bf168a`
- **Inputs:** the brief, `…-form-security-auditor.md` (md5 `f658107d…`) and `…-form-dotnet-architect.md` (md5 `8d0945d7…`)
- **Transcription:** the agent's SubagentHandback message, byte-extracted from its transcript
  (`subagents/agent-ab980e7efd78a9e5c.jsonl`); nothing below the rule is the session's.

---

## CTO-rekommendation: LinkedIn's api host (follow-up to #1926, epic #1732): routing

**Report-only.** I wrote, committed, labelled and posted nothing.
- Severity stays with the agent that reported each finding (CLAUDE.md §9.6). Nothing below re-grades a finding, and I measured nothing.
- These verdicts take effect without a further Klas GO.
- The one Klas question is security-auditor's. The session relays it.

**What I read on 2026-10-03, in this worktree at `4e0bf168a`:**
- the brief and both form reports;
- my #1926 routing (`docs/reviews/2026-10-03-1926-form-cto.md`);
- ADR 0142 Amendment (22) (:2116–2377);
- the session log (mirrored here);
- `LinkedInIdentityProvider.cs`, whole, for the 1022 causes;
- `docs/reviews/2026-10-03-1926-form-security-auditor.md` Fråga 1, for what Klas has already been asked;
- two memory entries: Klas-direktiv 2026-09-05 (a small tracked-doc correction rides the next session's own PR) and the escalation doctrine.

**One tool call outside my charter.** I ran one read-only `git check-ignore`/`git ls-files` call to see which homes are tracked. ADR 0142, `vps-deploy-stack.md` and `docs/decisions/README.md` are tracked. `gdpr-processing-register.md` is gitignored (`.gitignore:171`). My charter lists no Bash, so I disclose the call. Nothing below rests on more than that read.

I did not read #1926, #1732 or Supabase on GitHub.

**Abbreviations used below:**
- sa = security-auditor; da = dotnet-architect.
- "Answer A/B" = Klas's answer to sa's question.
- "Form (a)/(b)" = the host forms.

### Beslut

**1. One question goes to Klas: security-auditor's, alone, byte for byte. dotnet-architect's text is not put, and the session discloses its option 3 in one line.**
- **One question, not two.** da's options 1 and 2 are sa's (a) and (b) in other words. Two questions on one decision invite two inconsistent answers, and the session could not reconcile them without paraphrasing, which it may not do.
- **sa's text, for three reasons:**
  1. Her (a) authorises the whole measured chain in Klas's own decision: form (a), then form (b) "där även appens hemlighet skickas till api-adressen", then today's state "utan att du behöver svara igen". da's option 1 says only that a failed pre-merge measurement means option 2. So a "1" would not cover moving the client secret to an undocumented endpoint, which is the fallback both reports name.
  2. Her (a) states the contract risk that bears on the decision: the API Terms let LinkedIn suspend a non-conforming app. da's text does not.
  3. Her (23) texts quote her own "‹label›" and "‹option text›". Option 3 has no signed variant at all.
- **da's option 3 is not put, because Klas answered it today.**
  - #1926's question 1 offered "(c) Stäng av LinkedIn". He chose (b) "Prova först", with (a) "Som i dag" applying (ADR 0142 (22)).
  - A new lever adds an option. It does not change how deactivation compares with today's state.
  - Asking again would break his never-re-ask rule.
- **The relay, in this order, in one turn:**
  1. sa's "Text före frågan", both paragraphs, as chat text.
  2. Her "En avläsning, oavsett svar (inget beslut)" paragraph, as chat text.
  3. This line, which is the session's own and not an agent's:
     > dotnet-architect skrev samma fråga med ett tredje alternativ: att stänga av LinkedIn-inloggningen. Det ställs inte, eftersom du fick samma alternativ i dag, (c) i #1926-rundans fråga 1, och valde (b). Vill du ändå ha det, skriv det i svaret.
  4. AskUserQuestion with her question, her two labels and her two option texts.
- **Before sending,** the session diffs each relayed text against its source in her report.
- **If he answers outside the two options,** nothing is built or written until sa has his answer, report-only.
- **His answer is written down in the same turn,** in three places: the session log, the main copy's `current-work.md`, and a dated #1732 comment that carries the question, the chosen label and its text.

**2. Order.**

**Under answer A:**
1. **The question; the answer is recorded; the issue is filed and claimed** (decision 4).
2. **Minor 6's probe (i′)**, sa's instrument.
   - **When:** after the answer, before any code. It cannot change the choice set, because a crafted request reaches the api host whichever host ours uses. A code that reaches a control, however, halts the build.
   - **Sign-in:** Klas does any sign-in an arm needs.
   - **A code at a control:** nothing proceeds until sa has graded it.
3. **C1** (decision 5) is committed, then da R4's mutations run against that commit. A web mutation counts as red only when the vitest run fails with a non-zero `total`.
4. **The stack.** The session measures ownership before it claims anything: `git worktree list`, who holds `:3000` and the api, the containers, and `current-work.md`'s stack-owner line.
   - **Free:** it claims the stack visibly in the main copy and runs it from its own worktree with the env override (§6.5 Model 1). It releases the stack after the readings.
   - **Owned elsewhere:** it neither takes the stack over nor starts a second web. The registered localhost callback is `:3000`, and §6.5 keeps a non-owner off the shared DB. It tells Klas that (i) waits. That is sequencing, not a STOPP and not a `blocked`.
   - **Before the sitting:** it reads the local before-state (his account, its `linkedin` link, registration) and compares the local client to the box's by sha256 (m-4). If sa's "2 × `login_succeeded`" cannot occur on that state, the question goes to her before the sitting.
5. **(i)**, exactly sa's steps 1–4, then da N2's appended step (decision 3).
6. **Form (b) only on a 1022 with a token cause** (decision 6, item 4).
   - Then C2, a full repeat of (i), and sa's Chapter V re-read.
   - **Any other failure** (a non-token 1022, a 1023, a LinkedIn error page) stops the work and goes to sa.
7. **If both forms are refused,** answer A's own text applies: "blir det som i dag".
   - No code PR. The branch is abandoned.
   - The issue closes on that dated reading.
   - The docs are routed as under answer B, with a variant from sa, since none of her texts fits.
8. **Then:**
   - one report-only question to sa (decision 6, item 9);
   - C3 and C4;
   - push and `gh pr create`;
   - the panel;
   - the merge-day reading, then `agents-done`, then merge, then (ii).

**Under answer B:** the record (decision 1), then Minor 6's probe in the same sitting, posted dated on #1732.

**3. Disposal (§9.6).**

| Item | Answer A | Answer B |
|---|---|---|
| sa Minor 5 ((22)'s heading) | In-block, C4: marker (ii) 1, A form. | Parked: marker (ii) 1, B form. |
| sa Minor 6 (api host's redirect matching) | In-block: the probe, marker (ii) 4 (C4), §3d point 4's sentence (C3). | Probe taken; texts parked. |
| da N1 (`route.test.ts` www row) | **In-block, C1.** | Moot. |
| da N2 (no-grant path) | **In-block,** appended after sa's steps 1–4. | Moot. |
| da N3 (session log, line numbers) | **In-block,** a drafting rule for C3–C4. | Same rule for the parked text. |
| e2e | **Not a §9.6 skip;** a "Not in this PR" line. | Moot. |
| Register (sa (iv)) | Form (a): no change. Form (b): in-block, in the main copy. | No change (Close B). |

**N1, in-block.** It pins a claim this PR writes into (23) and §3d: an api image and a web image that name different hosts fail closed. Only Google's row (:138) pins that today. The row names its actor: the api image before this PR answering the new web during a rollout. The images move independently (measured 2026-10-03T08:48:24Z). The PR touches the file anyway.

**N2, in-block.**
- **The step:** Klas removes the app's grant in LinkedIn's settings and logs in once through the button.
- **What is recorded:** which pages showed (host, path and title, never a value).
- **The expected read-back:** 1 × `login_succeeded … Method=LinkedIn`, 0 × 1021–1023, 0 `User.ExternalLoginLinked`, and the `linkedin` link unchanged.
- **Where it goes:** into (23)'s measurement part, which is the session's, never into sa's text.
- **On failure:** the work stops and goes to sa.
- **If Klas declines the removal:** it becomes a named skip, and no text describes a permission page on the api host.
- **Why:** it is the path every real test user meets first.

**N3, in-block, as a drafting rule.**
- Tracked text cites #1926's comment 5967746641, never the gitignored log. The session checks that each cited fact is in that comment.
- No line numbers into Supabase's `master`. sa's text has none.

**e2e: a scope line.** No finding underlies it. da R3 decides "no line", because LinkedIn has no client in `e2e.yml` and the suite is observe-only.

**Register under form (b).** It is edited in the main copy (`.gitignore:171`) on the merge day, after sa's Chapter V re-read. ‹datum› and the reading list are filled only from that re-read. The PR body names the edit as done outside the diff.

**Under answer B, the docs are parked (Klas-direktiv 2026-09-05).** ADR 0065 rules out a docs-only PR.
- **The parked set:**
  - (23) with sa's text in its B form (Variant B and Close B);
  - markers (ii) 1 (B form) and (ii) 4;
  - §3d point 4's sentence and §3d's B sentence;
  - the README row's "(23)" clause.
- **Where it is written:** the main copy's `current-work.md`, with the file, the anchor, the filled slots and each text's source.
- **Who carries it:** the next session's own scope PR, as a separate `docs(adr)` commit. A PR whose panel already includes security-auditor is preferred; otherwise that panel adds her for this commit.
- **The amendment number:** that session re-reads the next free one. If (23) is taken, the change touches her markers, so it goes to her.
- **Neither an issue nor a skip.** Every session reads `current-work.md` (§1.5), and #1732 carries the decision.

**4. The issue: one, under answer A only, filed directly after the answer.**
- **Title:** `fix(auth): LinkedIn asks for the account at every login through its api host`
- **Labels:** `area:auth`, `P2` (as #1926), lane `BE+FE`, **`mvp`** (a real test user meets the LinkedIn login). No `hotspot:`, since no file is on §6.5's list.
- **On filing:** assign the session and add `wip`. Link it to epic #1732, as a follow-up to #1926.
- **The body:**
  1. Klas's directive (#1926, 2026-09-29) in one line, and his word "undersöka detta i ny session" (2026-10-03).
  2. His answer verbatim, dated: the question, the label and its text.
  3. What was measured on 2026-10-03, read in the session's browser pane with Klas signing in himself (#1926 comment 5967746641).
     - The api host showed the sign-in page to a member signed in at www.
     - No permission page followed.
     - The mixed pair is written as unmeasured.
  4. The form, one line each: the host as an adapter constant plus the web's single entry; (b) only on a measured token refusal; nothing else in the request; the four pins.
  5. Bound before merge: (i′), (i) with N2, and the merge-day reading.
  6. **Closes on** sa's (ii), posted dated here and on #1732. Never at merge.
  7. Not claimed: a request built by someone else, or ours edited back to www, still mints a code without prompting ((21), sa S1).
- **No gitignored review is cited as authority.** The tracked home is the amendment this issue's PR writes, and it is named as that deliverable.
- **Why not before the answer:** the title, `mvp` and whether anything is built at all depend on it. The claim is already visible in `current-work.md`.
- **Cap:**
  - Answer A: +1 filed, −1 closed (#1926, closed on its own reading, not to make room). Net 0, or −1 if (ii) closes the issue in this session.
  - Answer B: 0 filed, net −1.
  - A failed (ii) is a defect in delivered code and is always filed.

**5. The PR: one PR, labelled `automerge` at creation.**
- **One change-reason:** LinkedIn asks for the account at every login.
  - The adapter and the web entry cannot be split. Each alone fails closed at every start and turns the mirror test red (da R9).
  - Splitting docs from code would make a docs-only PR.
- **Commits:**
  - **C1 `fix(auth)`:**
    - the constant, with da R1's pointer comment; the discovery comment stops covering it;
    - `AUTHORIZATION_ENDPOINTS.linkedin`;
    - unit test A1 :97 and integration :159;
    - the architecture literal: kept, pointed at the api host, renamed without "documented";
    - `route.test.ts:15`, plus N1's row.

    One commit, because pre-commit runs the Application and Architecture tests on staged `.cs`.
  - **C2 `fix(auth)`, form (b) only:**
    - `TokenEndpoint`;
    - `ScriptedLinkedIn.TokenEndpoint` and its DECLARED clause;
    - the token-URL literal pin (sa condition 10);
    - the pointer comment, extended to cover both constants.
  - **C3 `docs(runbook)`:** §3d per sa (iii).
  - **C4 `docs(adr)`:** (23), markers (ii) 1–4 (5–6 under form (b)), the README row and Implementation status. Close A's ‹reading, date› comes from a box reading taken before C4.
- **`gh pr create`** once C4 exists, with every slot filled from a reading that has happened. The body is written at creation and edited once after the last verdict.
- **One batched panel round on C1–C4:**
  - `security-auditor` (auth, the OAuth integration; her final-diff verdict is owed);
  - `code-reviewer` + `dotnet-architect` (more than 5 files);
  - `test-writer`, **added by the session, not mandatory.** N1 hands the route a URL as the api's answer, which is the hand-built-argument class in AGENTS.md §5 `Tests:`. R4's mutations also need a reader from the test side, as in the #1926 precedent.
  - `design-reviewer` is **not triggered**: no `.tsx`/`.css`, and `/logga-in`'s notice is unchanged. One table line; not a skip.
  - The panel prompt names sa's verbatim blocks, and names N1's www literal against her condition 1.
- **Bound before `agents-done`:**
  - (i) with N2's step, recorded in (23). It is taken again if a later commit touches the adapter, the web start or `external-login.ts`.
  - **The merge-day reading, (e)-style.** It is read-only on the box and posted as a PR comment, never a doc edit: registration `false`, 2 accounts (both the controller's, 0 created), `github:1,google:1,linkedin:1`, and the providers. A deviation from Close A's reading stops the work and goes to sa.
  - **Form (b) only:** the Chapter V re-read and the main copy's register on the merge day. If the merge slips, both are taken again, and that slot change costs sa's one scoped re-check.
  - The amendment number is re-read, and HEAD is unchanged.
- **After merge:**
  - (ii) at Klas's first login after the rollout, with both images on the merge's `sha-<short>`. It closes the issue.
  - A failed (ii) is sa's lapse (2): the §3d deactivation and a revert PR at once.
- **The PR takes #1888's `blocked` shape** only if the final diff reaches beyond the host, the pins and the docs.
- **PR body, "Not in this PR" and named skips:**
  - e2e (a scope line);
  - sa's carried Minor 1 (the apex redirect), repeating #1939's skip line, since this PR touches the request it concerns;
  - N2, only if Klas declined;
  - design-reviewer, not triggered (not a skip).

**6. Where the reports disagree, and which governs.**
1. **Two options or three:** sa's two (decision 1).
2. **N1's www literal against sa's condition 1 ("no www entry"):** no conflict.
   - Her condition binds production: the allow-list and the adapter.
   - A literal that asserts the refusal is the executable form of her "the start answers no other LinkedIn host".
   - She reads it in her final-diff verdict.
3. **The session log as a source:**
   - A valid working source for the agents.
   - Tracked text cites #1926's comment (N3).
4. **What triggers form (b)** (da: `TokenInvalidRequest`; sa: "a token cause"):
   - sa's governs, since the trigger is part of her signed form.
   - The sha256 reading before the sitting keeps a local configuration fault from reading as a refusal.
5. **Read-back counts:**
   - For 1021, sa's "at most the control's" governs. Her control's state names no flow, and 1021 is the only answer the api can give it (Amendment (14)).
   - da's 0 × 1024 and 1025 are added in the session's part.
6. **(23)'s lapse set** (da T1–T4 against sa's (1)–(4)):
   - sa's is the one set. In substance it is the superset: it adds the API Terms notice and covers T4 in her Form (b) block.
   - Her condition 6 gives the set one home, so the T-list is not written.
   - da R7's order holds otherwise, as in #1926 decision 6 item 6:
     - part 6 is not written; her "What a failure looks like" covers it;
     - part 9 is the one-line pointer;
     - parts 3–5 carry only what her text lacks.
7. **§3d** (da R8 against sa (iii)): sa's texts govern.
   - "Three things differ" stays three.
   - da's error-block sentence is not added, because her lapse pointer covers it.
   - The Cancel line takes her wording.
   - da's rollout list goes into the issue as its closing criterion. A one-off reading in a standing runbook goes stale.
8. **Where (ii) is posted:**
   - On the new issue, which it closes, mirrored on #1732.
   - The next amendment transcribes it, as (22) transcribed 6c's reading.
9. **The prefilled address** (sa: "the member last signed in"; da: whose address shows at a shared computer is unmeasured).
   - It cannot change Klas's choice, since no one gets past the page without the password. So the question goes as sa wrote it.
   - The (23) sentence is hers. Before C4 the session sends her da's point, report-only: in the reading, the signed-in member and the last-signed-in member were the same person.
   - She keeps the sentence or marks it declared. The session never adapts it.

### Motivering mot principer
- **SRP and CCP at PR level (Martin 2017, ch. 7 and 13).** One change-reason, one PR. The constant, its mirror, its pins and their record change and revert together.
- **DRY of knowledge (Hunt/Thomas 1999).**
  - One decision gets one question.
  - One lapse set has one home, (23); §3d points to it.
  - The one-off reading lives in the issue.
- **Fitness functions (Ford/Parsons/Kua 2017).** The literal pins and N1 make the host, and fail-closed behaviour under image skew, executable. The mirror alone lets a consistent revert through.
- **Test what the user meets (Cohn 2009; DoD 4).** Test users meet the no-grant path first, and N2 reads it before they do.
- **Measurement validity.** Form (b) is earned by a measured refusal, never inferred from Supabase. That is the lesson of (20) → (21).

### Avvisade alternativ
- **Both texts as two questions:** one decision asked twice, with a contradictory pair the session cannot reconcile.
- **da's text alone:**
  - its option 1 does not authorise the secret move;
  - its option 3 re-asks a decided question;
  - sa's slots do not fit it.
- **Filing before the answer:** a backlog row that pre-empts Klas, possibly under the wrong title and the wrong `mvp`.
- **Under answer B, a docs-only PR:** ADR 0065 rules it out.
- **Under answer B, waiting for "the next auth-code PR"** (da R9): an unbounded wait on a known-false heading. Klas's 2026-09-05 directive names the channel.
- **(i) on a side stack:**
  - the registered callback is `:3000`;
  - a fresh DB would measure a first-link login, not the box's state.
- **Form (b) on any 1022, or on Supabase's word:** it sends the secret to an endpoint that no discovery document names, on an unmeasured premise.

### Trade-offs accepterade
- **A few more minutes of Klas's time:** a possible sign-in for Minor 6, plus the grant removal and one more login for N2.
- **test-writer as a fourth reader** of a small test delta.
- **Under answer B, (22)'s false heading stays on `main`** until the next session's PR. It is a Minor and widens nothing.
- **For up to about 2 h after merge, the image skew refuses LinkedIn starts.** It fails closed, and (ii) step 1 reads it.

### In-block-fixar
- **Answer A:** C1–C4 (decision 5), with Minor 5, Minor 6, N1, N2 and N3 placed as decision 3 says.
- **Before the panel:**
  - diff each sa text, once its ‹› are filled, against her report;
  - measure Klas's words as substrings of their source.
- **Answer B:** the parked set, written into `current-work.md` in the same turn.

### Följd-PR
- **Conditional only:** `fix(auth): LinkedIn's authorization request returns to the documented host`, at sa's lapse (1)–(4). Its home is (23), and its reader is Klas.

### Issues att fila
- **Answer A:** decision 4's issue, filed after the answer. Net 0.
- **Answer B:** none. Net −1.
- **Named skips:** decision 5.

### Referenser
- Books:
  - Martin, *Clean Architecture* (2017), ch. 7 and 13;
  - Hunt/Thomas, *The Pragmatic Programmer* (1999);
  - Ford/Parsons/Kua (2017);
  - Cohn (2009).
- Spec: CLAUDE.md §1.5, §6.5, §9.1, §9.2 and §9.6; AGENTS.md §1.6, §5 (`Tests:`, `Comments:`), §8 and §12; ADR 0065.
- ADR 0142 Amendments (14), (20), (21) and (22); `vps-deploy-stack.md` §3d.
- Prior reports: `docs/reviews/2026-10-03-1926-form-security-auditor.md` (Fråga 1) and `docs/reviews/2026-10-03-1926-form-cto.md`.
- Klas-direktiv 2026-09-05 (memory).

Verdict-table line: "senior-cto-advisor, LinkedIn api-host form routing (follow-up to #1926, report-only): security-auditor's question goes to Klas alone and verbatim. dotnet-architect's option 3 is not put (Klas declined it 2026-10-03 as #1926 q1 (c)) and is disclosed in one session line. Order: question → probe (i′) → C1 → stack ownership measured → (i) + N2 → docs → PR → panel → merge-day reading → merge → (ii). Under answer A, Minors 5 and 6, N1, N2 and N3 are in-block. Under answer B they are parked per Klas-direktiv 2026-09-05. One issue, under A only (`fix(auth)`, area:auth, P2, BE+FE, mvp); net filing 0. One PR, `automerge` at creation; panel sa + code-reviewer + dotnet-architect, with test-writer as the session's addition; design-reviewer not triggered. Disagreements resolved: 9."

**Eskalering till Klas: nej, ingen egen.** Frågan är security-auditors. Sessionen vidarebefordrar den ordagrant (beslut 1), tillsammans med sessionens enradiga upplysning om dotnet-architects alternativ 3.

Files:
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\sessions\2026-10-03-1926b-linkedin-api-host-form-brief.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\reviews\2026-10-03-1926b-linkedin-api-host-form-security-auditor.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\reviews\2026-10-03-1926b-linkedin-api-host-form-dotnet-architect.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\reviews\2026-10-03-1926-form-cto.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\reviews\2026-10-03-1926-form-security-auditor.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\decisions\0142-passwordless-auth-one-page-code-or-link-oauth-ready.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\docs\sessions\2026-10-03-1926b-closeout-linkedin-api-host.md`
- `C:\DOTNET-UTB\JobbPilot\.claude\worktrees\jobb-card-density-1828-79e055\src\Jobbliggaren.Infrastructure\Auth\ExternalLogins\LinkedInIdentityProvider.cs`
- `C:\Users\zebac\.claude\projects\C--DOTNET-UTB-JobbPilot\memory\feedback_small_runbook_fixes_ride_the_next_sessions_own_pr_never_a_standalone_pr.md`
- `C:\Users\zebac\.claude\projects\C--DOTNET-UTB-JobbPilot\memory\feedback_escalations_accumulate_instead_of_forcing_the_decision.md`
