import { describe, expect, it } from "vitest";
import { readBounded } from "./read-bounded";

function streamOf(chunks: readonly number[][]): { stream: ReadableStream<Uint8Array>; cancelled: () => boolean } {
  let cancelled = false;
  let index = 0;
  const stream = new ReadableStream<Uint8Array>({
    pull(controller) {
      const next = chunks[index++];
      if (next === undefined) controller.close();
      else controller.enqueue(new Uint8Array(next));
    },
    cancel() {
      cancelled = true;
    },
  });
  return { stream, cancelled: () => cancelled };
}

describe("readBounded", () => {
  it("joins the chunks of a body within the limit", async () => {
    const { stream } = streamOf([[1, 2], [3], [4, 5]]);
    expect(await readBounded(stream, 5)).toEqual(new Uint8Array([1, 2, 3, 4, 5]));
  });

  it("refuses a body one byte over the limit and cancels the reader", async () => {
    const source = streamOf([[1, 2, 3], [4, 5, 6]]);
    expect(await readBounded(source.stream, 5)).toBeNull();
    expect(source.cancelled()).toBe(true);
  });

  it("answers null for an absent body", async () => {
    expect(await readBounded(null, 10)).toBeNull();
  });

  it("reads an empty body as zero bytes", async () => {
    const { stream } = streamOf([]);
    expect(await readBounded(stream, 10)).toEqual(new Uint8Array([]));
  });
});
