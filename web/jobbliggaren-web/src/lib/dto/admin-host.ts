import { z } from "zod";

/**
 * The host readings of `GET /api/v1/admin/overview/host` (#1982, ADR 0158): states, instants and numbers,
 * nothing else. zod drops any field the backend might add, so the browser's copy never grows by accident.
 */
const instant = z.iso.datetime({ offset: true });
const bytes = z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER);
const percent = z.number().min(0).max(100);

export const hostMetricStateSchema = z.enum(["Available", "Stale", "Collecting", "NotObservable", "Failed"]);
export type HostMetricState = z.infer<typeof hostMetricStateSchema>;

/**
 * One reading. A value and its sample time exist together, in Available and Stale only; in every other
 * state both are null. A body that says otherwise is not an observation, so it fails the whole source
 * instead of being rendered.
 */
function reading<T>(value: z.ZodType<T>) {
  return z
    .object({ state: hostMetricStateSchema, sampledAt: instant.nullable(), value: value.nullable() })
    .superRefine((metric, ctx) => {
      const hasValue = metric.state === "Available" || metric.state === "Stale";
      const carriesValue = metric.value !== null;
      if (hasValue !== carriesValue || hasValue !== (metric.sampledAt !== null)) {
        ctx.addIssue({ code: "custom", message: `A ${metric.state} reading ${hasValue ? "needs" : "must not carry"} a value and a sample time.` });
      }
    });
}

const cpuValue = z.object({ percent, windowSeconds: z.number().int().positive().max(86_400) });
const memoryValue = z
  .object({ percent, usedBytes: bytes, totalBytes: bytes })
  .refine((memory) => memory.totalBytes > 0 && memory.usedBytes <= memory.totalBytes, "Used memory exceeds the total.");
const diskValue = z
  .object({ percent, freeBytes: bytes, totalBytes: bytes })
  .refine((disk) => disk.totalBytes > 0 && disk.freeBytes <= disk.totalBytes, "Free disk exceeds the total.");

export const hostObservationSchema = z.object({
  readAt: instant,
  staleAfterSeconds: z.number().int().positive().max(86_400),
  cpu: reading(cpuValue),
  memory: reading(memoryValue),
  disk: reading(diskValue),
});
export type HostObservationDto = z.infer<typeof hostObservationSchema>;
