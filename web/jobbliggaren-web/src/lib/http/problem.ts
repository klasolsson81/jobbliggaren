import { z } from "zod";

const problemBodySchema = z.object({
  // Capped: the value is only ever compared against an exact whitelist, never rendered, and a cap
  // keeps a hostile or broken upstream from handing megabytes to a string comparison.
  title: z.string().max(200).optional(),
  errors: z.record(z.string(), z.array(z.string())).optional(),
});

/** An error body in either shape the backend produces. See {@link readProblemBody}. */
export type ProblemBody = z.infer<typeof problemBodySchema>;

/**
 * #1171 — reads an error body that may be EITHER of the two shapes the backend emits on 400, in ONE
 * read: ProblemDetails with a `title` (a `DomainError` mapped by the endpoint or the kind-mapper) or
 * the ValidationException shape `{ errors: { Field: [messages] } }` that `ValidationBehavior` writes
 * BEFORE the handler runs.
 *
 * It exists because a caller that needs to tell those apart cannot use {@link readProblemTitle}
 * twice — the first call consumes the body — and a second `res.json()` throws on a real Response.
 * Callers that only need the title should keep using {@link readProblemTitle}; this is the wider
 * reader, not a replacement.
 *
 * Same discipline as its sibling: never throws, and the values are ONLY for comparison against an
 * exact whitelist. Backend text (`detail`, or the messages inside `errors`) is never rendered — it
 * can carry server internals.
 *
 * Consumes the response body — call at most once per response.
 */
export async function readProblemBody(res: Response): Promise<ProblemBody | null> {
  try {
    const parsed = problemBodySchema.safeParse(await res.json());
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}

/**
 * #616 — reads the ProblemDetails `title` (the backend's machine error code, e.g.
 * "Auth.PwnedPassword") from an error response. Never throws: non-JSON bodies and shapes
 * without a `title` resolve to null.
 *
 * The title is ONLY for comparison against an exact whitelist at the call site — callers map a
 * recognized code to localized copy from `messages/` and must never render backend text
 * (`detail`) directly.
 *
 * Consumes the response body — call at most once per response, and not alongside another body
 * read of the same response.
 */
export async function readProblemTitle(res: Response): Promise<string | null> {
  // Delegates rather than parsing again. The two were byte-equivalent on every branch (missing title,
  // wrong type, non-JSON), so two schemas and two try/catches over the same body were two places for
  // one truth to drift — and a caller had to choose between them for no reason. Both names stay: this
  // is the common case and reads better at the call site.
  return (await readProblemBody(res))?.title ?? null;
}

