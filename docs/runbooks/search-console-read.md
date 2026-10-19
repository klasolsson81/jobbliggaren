# Runbook — the Search Console read: setup, reading and revocation

**What it is.** One GitHub Actions workflow on `main` reads Search Console aggregates once a week:
- one permission check;
- five `searchAnalytics.query` requests with no `dimensions`, one per page type plus the site total;
- two `sitemaps.list` requests.

It writes only to its run's job summary. The design is ADR 0177. The governance, meaning what is outside ADR 0166's trigger 5 and what fires it again, is the ADR 0166 amendment for this path, triggers 13 and 14.

**Who:**
- **Klas** does the setup in §1 and the revocation in §4.
- **A session** records only what §1.12 lists.
- **No agent session** opens the Search Console or Google Cloud Console UI, through the browser pane, Claude in Chrome or Codex. Those pages show query rows and the users list, which is the same channel as the API (ADR 0177 D5).

## §0 Fixed values

These are bound by the provider's attribute condition. Changing any of them is ADR 0166 D10 trigger 13, read by Klas before the change is made.

| What | Value | Source |
|---|---|---|
| Workflow file | `.github/workflows/search-console-read.yml`, on `main` | ADR 0177 |
| `workflow_ref` claim | `klasolsson81/jobbliggaren/.github/workflows/search-console-read.yml@refs/heads/main` | GitHub OIDC reference |
| `repository_id` | `1214215182` | `gh api repos/klasolsson81/jobbliggaren --jq .id`, 2026-10-18 |
| `repository_owner_id` | `229617477` | `gh api repos/klasolsson81/jobbliggaren --jq .owner.id`, 2026-10-18 |
| Search Console property | `sc-domain:jobbliggaren.se` (the Domain property) | step 1 confirms it |
| OAuth scope | `https://www.googleapis.com/auth/webmasters.readonly` | ADR 0177 |
| Search Console permission | **Restricted** (Begränsad). Never Full, never Owner. | ADR 0177 |

## §1 Setup (Klas, once)

Run the commands in **Cloud Shell**, signed in with the Google account that should own the project. Every value below is fixed except `PROJECT_ID`, which must be globally unique. If `jobbliggaren-sc-read` is taken, add a suffix of digits.

After each step, note **yes** (done as written) or **no** (it failed or asked for something not written here). On a no, stop and tell the session which step it was, and nothing more.

**1. Confirm the property.** In Search Console, the property is the **Domain** property `jobbliggaren.se`, not the URL-prefix property `https://jobbliggaren.se/`. If only a URL-prefix property exists, stop: the read path is pinned to `sc-domain:`.

**2. A dedicated project.** It holds nothing else, and it has no billing account.
```sh
PROJECT_ID=jobbliggaren-sc-read
gcloud projects create "$PROJECT_ID" --name="Jobbliggaren SC read"
gcloud config set project "$PROJECT_ID"
PROJECT_NUMBER=$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')
```

**3. The APIs.** These are the Search Console API plus the APIs Google's Workload Identity Federation page requires for creating the pool and for impersonation: IAM, Resource Manager, IAM Service Account Credentials and Security Token Service. Nothing else.
```sh
gcloud services enable searchconsole.googleapis.com iam.googleapis.com \
  cloudresourcemanager.googleapis.com iamcredentials.googleapis.com sts.googleapis.com
```
If enabling asks for a billing account, answer **no** to this step and stop.

**4. One pool.**
```sh
gcloud iam workload-identity-pools create github-actions \
  --location=global --display-name="GitHub Actions"
```

**5. One provider.**
- The mapping carries `google.subject` and exactly the six claims the condition reads. Never `actor`.
- The condition is copied exactly. The numeric ids are used and not the names, because names can be re-registered.
```sh
gcloud iam workload-identity-pools providers create-oidc search-console-read \
  --location=global --workload-identity-pool=github-actions \
  --display-name="search-console-read workflow" \
  --issuer-uri="https://token.actions.githubusercontent.com" \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository_id=assertion.repository_id,attribute.repository_owner_id=assertion.repository_owner_id,attribute.ref=assertion.ref,attribute.workflow_ref=assertion.workflow_ref,attribute.event_name=assertion.event_name,attribute.runner_environment=assertion.runner_environment" \
  --attribute-condition="assertion.repository_owner_id == '229617477' && assertion.repository_id == '1214215182' && assertion.ref == 'refs/heads/main' && assertion.workflow_ref == 'klasolsson81/jobbliggaren/.github/workflows/search-console-read.yml@refs/heads/main' && assertion.event_name in ['schedule', 'workflow_dispatch'] && assertion.runner_environment == 'github-hosted'"
```

**6. One service account, with no role and no key.**
```sh
gcloud iam service-accounts create sc-read \
  --display-name="Search Console read (GitHub Actions)"
SA_EMAIL="sc-read@${PROJECT_ID}.iam.gserviceaccount.com"
```

**7. The only binding: the repository may impersonate the account.**
- It is keyed on `attribute.repository_id`, never pool-wide `/*`.
- The member must name the project **number**, not the project id.
```sh
gcloud iam service-accounts add-iam-policy-binding "$SA_EMAIL" \
  --role=roles/iam.workloadIdentityUser \
  --member="principalSet://iam.googleapis.com/projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/github-actions/attribute.repository_id/1214215182"
```

**8. Check that the account has no project role, no key and one binding, and that the pool has one provider.** Each of the first two commands must print nothing. The third must print exactly one `roles/iam.workloadIdentityUser` line, for the `principalSet` above. The fourth must print exactly `search-console-read`.
```sh
gcloud projects get-iam-policy "$PROJECT_ID" --flatten='bindings[].members' \
  --filter="bindings.members:serviceAccount:${SA_EMAIL}" --format='value(bindings.role)'
gcloud iam service-accounts keys list --iam-account="$SA_EMAIL" --managed-by=user --format='value(name)'
gcloud iam service-accounts get-iam-policy "$SA_EMAIL" --flatten='bindings[].members' \
  --format='value(bindings.role,bindings.members)'
gcloud iam workload-identity-pools providers list --location=global --workload-identity-pool=github-actions \
  --format='value(name.basename())'
```
"No key" is a setup record, not an enforced property: no organization policy forbids creating one later. **Never create one.**

**9. Search Console: add the account as Restricted.** Go to Settings → Users and permissions → Add user. Enter the address in `$SA_EMAIL`, choose **Restricted** (Begränsad), and select Add.
- Google documents no service account as a Search Console user. Search Console's help only says users need "a valid Google Account". This step therefore measures the premise.
- If the dialog refuses the address, answer **no** and stop. The session then stops and goes back to senior-cto-advisor and security-auditor.
- **Never** fall back to an OAuth client for your own account, to a JSON key, or to a Full or Owner user.

**10. Optional: the URL Inspection quota.** In the project, open IAM & Admin → Quotas & system limits, and look for the Google Search Console API's URL Inspection quota.
- If it can be edited down to 0, set it to 0 and answer **yes**.
- If it cannot, answer **no**.
- Google's limits page (last updated 2025-08-28) does not say whether a project can lower it, so only this reading decides. Nothing may call the quota a barrier unless this answer is yes.

**11. The four identifiers.** They are not secrets. They go into the workflow file, where any change is a reviewed diff.
```sh
PROJECT_ID=$(gcloud config get-value project)
PROJECT_NUMBER=$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')
SA_EMAIL="sc-read@${PROJECT_ID}.iam.gserviceaccount.com"
echo "project number: $PROJECT_NUMBER"
echo "pool:           github-actions"
echo "provider:       projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/github-actions/providers/search-console-read"
echo "service account: $SA_EMAIL"
```

**12. What you give the session, and nothing else:**
- yes or no for steps 1–10;
- the four lines from step 11.

**Never** give it your Google account address, the Search Console users list, a screenshot of either console, or a token.

## §2 What a run does, and how it is read

- **When:** the workflow runs every Wednesday at 06:13 UTC (`schedule`) and on `workflow_dispatch` from `main`. It takes no inputs.
- **The window:** the latest calendar month whose last day is at least four days before the run, in UTC. Every run within a month therefore prints the same final numbers.
- **The summary** (visible to signed-in GitHub users) shows:
  - the window and the permission level;
  - one row per page type (`home`, `job`, `catalogue`, `other`) and the site total, each with clicks, impressions, CTR, position and Google's aggregation type;
  - the sitemaps table: file, last downloaded, pending, errors, warnings and submitted.
- **Page-type numbers** are aggregated by page, as in the Search Console UI, so they do not sum to the site total.
- **The outcome line** is an annotation, so it can be read without the summary:
  - `search-console-read: accepted, permission siteRestrictedUser, window …`, or
  - `search-console-read refused: <request> <class>`.
  - The classes are `http <code>`, `permission`, `shape`, `keys`, `rows` and `path`. A refusal never prints a value.
- **Writing it down:** `seo-measurement.md` §4 is still written by hand from the summary, by Klas or the session he names. The summary expires with the run's retention, and it is the instrument's output, not the record.

## §3 Recorded setup

| Date (UTC) | Step | Answer | By |
|---|---|---|---|
| 2026-10-18/19 | 1 | yes | Klas |
| 2026-10-18/19 | 2 | yes | Klas |
| 2026-10-18/19 | 3 | yes | Klas |
| 2026-10-18/19 | 4 | yes, on the second run; the first was refused with `PERMISSION_DENIED` minutes after step 3 | Klas |
| 2026-10-18/19 | 5 | yes, on the second run, after step 4 | Klas |
| 2026-10-18/19 | 6 | yes | Klas |
| 2026-10-18/19 | 7 | yes, on the second run, after step 4 | Klas |
| 2026-10-19 | 8 | yes | Klas |
| 2026-10-19 | 9 | yes: Search Console accepted the service account as Restricted | Klas |
| 2026-10-19 | 10 | no: not done | Klas |

The four identifiers are recorded in the workflow file itself.

## §4 Revocation

**When:**
- ADR 0166 D10 trigger 14 fires;
- an action the job runs is suspected of compromise;
- the automation is retired.

**In this order,** in Cloud Shell, with the project id from the workflow's `service_account` (between `sc-read@` and `.iam`):
```sh
PROJECT_ID=jobbliggaren-sc-read
```
1. **Search Console:** Settings → Users and permissions → the service account → Remove access. This ends every Search Console right at once.
2. **Disable the provider:**
   ```sh
   gcloud iam workload-identity-pools providers disable search-console-read \
     --project="$PROJECT_ID" --location=global --workload-identity-pool=github-actions
   ```
3. **At retirement only:** delete the pool, the service account and the project:
   ```sh
   gcloud projects delete "$PROJECT_ID"
   ```

**Then** disable the workflow (`gh workflow disable search-console-read.yml`), so that its schedule stops reporting refusals.

## §5 Never

- A service-account JSON key.
- An OAuth client or refresh token for Klas's account.
- Another Search Console user for the automation.
- A Full or Owner permission.
- The `webmasters` (write) scope.
- Another API, including URL Inspection by script, sitemap submission, the Indexing API, IndexNow, Bing and CrUX.
- Another caller: an agent session, a cloud `/schedule`, a local machine or a second workflow.
- Another provider in the pool.
