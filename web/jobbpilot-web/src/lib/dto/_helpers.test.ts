import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { z } from "zod";
import {
  DtoParseError,
  assertNever,
  pagedResult,
  pagedResultWithTotalPages,
  parseResponse,
  responseToResult,
  type ApiResult,
} from "./_helpers";

const sampleSchema = z.object({
  id: z.string(),
  count: z.number(),
});

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

function invalidJsonResponse(): Response {
  return new Response("not-json-at-all", {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

describe("parseResponse", () => {
  let errorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    errorSpy.mockRestore();
  });

  it("returns parsed data on valid response", async () => {
    const res = jsonResponse({ id: "abc", count: 3 });
    const data = await parseResponse(res, sampleSchema, "test");
    expect(data).toEqual({ id: "abc", count: 3 });
  });

  it("throws DtoParseError on invalid JSON body", async () => {
    const res = invalidJsonResponse();
    await expect(parseResponse(res, sampleSchema, "test")).rejects.toThrow(
      DtoParseError
    );
    expect(errorSpy).toHaveBeenCalledOnce();
  });

  it("throws DtoParseError on schema mismatch (wrong type)", async () => {
    const res = jsonResponse({ id: "abc", count: "three" });
    await expect(parseResponse(res, sampleSchema, "test")).rejects.toThrow(
      DtoParseError
    );
  });

  it("throws DtoParseError when required field missing", async () => {
    const res = jsonResponse({ id: "abc" });
    await expect(parseResponse(res, sampleSchema, "test")).rejects.toThrow(
      DtoParseError
    );
  });

  it("ignores extra fields (non-strict default per ADR 0020)", async () => {
    const res = jsonResponse({ id: "abc", count: 3, extra: "ignored" });
    const data = await parseResponse(res, sampleSchema, "test");
    expect(data).toEqual({ id: "abc", count: 3 });
  });

  it("includes context in thrown error", async () => {
    const res = jsonResponse({ id: "abc" });
    try {
      await parseResponse(res, sampleSchema, "GET /api/test");
      expect.fail("should have thrown");
    } catch (err) {
      expect(err).toBeInstanceOf(DtoParseError);
      expect((err as DtoParseError).context).toBe("GET /api/test");
    }
  });

  it("logs structured error with context on schema mismatch", async () => {
    const res = jsonResponse({ id: 123, count: 3 });
    await expect(
      parseResponse(res, sampleSchema, "GET /test")
    ).rejects.toThrow();
    expect(errorSpy).toHaveBeenCalledWith(
      "DTO parse failed: shape mismatch",
      expect.objectContaining({
        context: "GET /test",
        issues: expect.any(Array),
      })
    );
  });

  it("redacts `received` value from logged issues (PII safety)", async () => {
    // Backend råkar returnera känsligt värde i fel fält (här: nummer-id där
    // string förväntades). `received` ska inte hamna i strukturerad logg.
    const res = jsonResponse({ id: "user@example.com", count: "not-a-number" });
    await expect(
      parseResponse(res, sampleSchema, "GET /test")
    ).rejects.toThrow();

    expect(errorSpy).toHaveBeenCalled();
    const loggedPayload = errorSpy.mock.calls[0]?.[1] as {
      issues: Array<Record<string, unknown>>;
    };
    for (const issue of loggedPayload.issues) {
      expect(issue).not.toHaveProperty("received");
    }
  });
});

describe("pagedResult", () => {
  const itemSchema = z.object({ id: z.string() });
  const schema = pagedResult(itemSchema);

  it("accepts valid paged result", () => {
    const result = schema.safeParse({
      items: [{ id: "a" }, { id: "b" }],
      totalCount: 2,
      page: 1,
      pageSize: 20,
    });
    expect(result.success).toBe(true);
  });

  it("rejects when items violate item-schema (full validation, not opt-in)", () => {
    const result = schema.safeParse({
      items: [{ id: "a" }, { id: 123 }],
      totalCount: 2,
      page: 1,
      pageSize: 20,
    });
    expect(result.success).toBe(false);
  });

  it("rejects negative totalCount", () => {
    const result = schema.safeParse({
      items: [],
      totalCount: -1,
      page: 1,
      pageSize: 20,
    });
    expect(result.success).toBe(false);
  });

  it("rejects page=0 (must be positive)", () => {
    const result = schema.safeParse({
      items: [],
      totalCount: 0,
      page: 0,
      pageSize: 20,
    });
    expect(result.success).toBe(false);
  });
});

describe("pagedResultWithTotalPages", () => {
  const schema = pagedResultWithTotalPages(z.object({ id: z.string() }));

  it("accepts valid shape with totalPages", () => {
    const result = schema.safeParse({
      items: [{ id: "a" }],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    });
    expect(result.success).toBe(true);
  });

  it("rejects when totalPages missing", () => {
    const result = schema.safeParse({
      items: [{ id: "a" }],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    expect(result.success).toBe(false);
  });
});

describe("responseToResult", () => {
  const schema = z.object({ id: z.string() });
  let errorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
  });

  afterEach(() => {
    errorSpy.mockRestore();
  });

  function mkResponse(
    body: unknown,
    status = 200,
    bodyType: "json" | "raw" = "json"
  ): Response {
    const init = { status, headers: { "Content-Type": "application/json" } };
    if (bodyType === "raw") {
      return new Response(body as string, init);
    }
    return new Response(JSON.stringify(body), init);
  }

  it("returns { kind: 'ok', data } on 200 + valid shape", async () => {
    const res = mkResponse({ id: "abc" });
    const result = await responseToResult(res, schema, "test");
    expect(result.kind).toBe("ok");
    if (result.kind === "ok") expect(result.data).toEqual({ id: "abc" });
  });

  it("returns { kind: 'unauthorized' } on 401", async () => {
    const res = mkResponse(null, 401);
    const result = await responseToResult(res, schema, "test");
    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("returns { kind: 'forbidden' } on 403", async () => {
    const res = mkResponse(null, 403);
    const result = await responseToResult(res, schema, "test");
    expect(result).toEqual({ kind: "forbidden" });
  });

  it("returns { kind: 'notFound' } on 404 when includeNotFound: true", async () => {
    const res = mkResponse(null, 404);
    const result = await responseToResult(res, schema, "test", {
      includeNotFound: true,
    });
    expect(result).toEqual({ kind: "notFound" });
  });

  it("returns { kind: 'error' } on 404 when includeNotFound omitted", async () => {
    const res = mkResponse(null, 404);
    const result = await responseToResult(res, schema, "test");
    expect(result).toEqual({ kind: "error" });
  });

  it("returns { kind: 'error' } on 500", async () => {
    const res = mkResponse(null, 500);
    const result = await responseToResult(res, schema, "test");
    expect(result).toEqual({ kind: "error" });
  });

  it("returns { kind: 'error' } on shape mismatch", async () => {
    const res = mkResponse({ id: 123 });
    const result = await responseToResult(res, schema, "test");
    expect(result).toEqual({ kind: "error" });
  });

  it("returns { kind: 'error' } on invalid JSON body", async () => {
    const res = mkResponse("not-json", 200, "raw");
    const result = await responseToResult(res, schema, "test");
    expect(result).toEqual({ kind: "error" });
  });

  it("prioritizes 401 over notFound option", async () => {
    const res = mkResponse(null, 401);
    const result = await responseToResult(res, schema, "test", {
      includeNotFound: true,
    });
    expect(result).toEqual({ kind: "unauthorized" });
  });
});

describe("assertNever", () => {
  it("throws when called (runtime safety net)", () => {
    expect(() => assertNever("unexpected" as never)).toThrow();
  });

  it("supports exhaustive switch over ApiResult kinds", () => {
    function render<T>(result: ApiResult<T>): string {
      switch (result.kind) {
        case "ok":
          return "ok";
        case "unauthorized":
          return "unauthorized";
        case "forbidden":
          return "forbidden";
        case "notFound":
          return "notFound";
        case "error":
          return "error";
        default:
          return assertNever(result);
      }
    }
    expect(render<string>({ kind: "ok", data: "x" })).toBe("ok");
    expect(render<string>({ kind: "unauthorized" })).toBe("unauthorized");
    expect(render<string>({ kind: "forbidden" })).toBe("forbidden");
    expect(render<string>({ kind: "notFound" })).toBe("notFound");
    expect(render<string>({ kind: "error" })).toBe("error");
  });
});
