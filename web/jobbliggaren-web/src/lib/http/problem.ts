import { z } from "zod";

const problemTitleSchema = z.object({ title: z.string() });

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
  try {
    const parsed = problemTitleSchema.safeParse(await res.json());
    return parsed.success ? parsed.data.title : null;
  } catch {
    return null;
  }
}
