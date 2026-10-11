import { z } from "zod";

/**
 * The services observation of `GET /api/v1/admin/overview/services` (#1982): three rows, each a state and the
 * instants it was judged from, plus the three limits behind the verdicts as numbers the page words itself. The
 * backend sends no label, reason, server name, latency or free text, and a key outside the contract fails the parse
 * (strict objects, as the Backup source's), so the browser's copy cannot grow.
 */
const instant = z.iso.datetime({ offset: true });
const limit = z.number().int().positive().max(86_400);

export const workerStateSchema = z.enum(["Alive", "Silent", "NoServer", "Unknown"]);
export const volatileStoreStateSchema = z.enum(["Reachable", "Unreachable", "Unknown"]);
export const jobImportStateSchema = z.enum(["Recorded", "Overdue", "NoneRecorded", "Unknown"]);

/**
 * A row's fields exist exactly in the states that have them, and are null in every other. A row that says otherwise
 * is not an observation, so it fails the whole source instead of being rendered.
 */
function fieldsMatchState(
  ctx: z.core.$RefinementCtx,
  state: string,
  fields: Readonly<Record<string, unknown>>,
  present: ReadonlyArray<string>,
) {
  for (const [field, value] of Object.entries(fields)) {
    const needed = present.includes(field);
    if (needed !== (value !== null)) {
      ctx.addIssue({ code: "custom", message: `A ${state} row ${needed ? "needs" : "must not carry"} ${field}.` });
    }
  }
}

const backgroundJobsSchema = z
  .strictObject({ state: workerStateSchema, sampledAt: instant.nullable(), heartbeatAt: instant.nullable() })
  .superRefine(({ state, sampledAt, heartbeatAt }, ctx) => {
    const present = state === "Alive" || state === "Silent" ? ["sampledAt", "heartbeatAt"]
      : state === "NoServer" ? ["sampledAt"] : [];
    fieldsMatchState(ctx, state, { sampledAt, heartbeatAt }, present);
  });

const volatileStoreSchema = z
  .strictObject({ state: volatileStoreStateSchema, sampledAt: instant.nullable() })
  .superRefine(({ state, sampledAt }, ctx) => {
    fieldsMatchState(ctx, state, { sampledAt }, state === "Unknown" ? [] : ["sampledAt"]);
  });

const jobImportSchema = z
  .strictObject({
    state: jobImportStateSchema,
    sampledAt: instant.nullable(),
    lastRunAt: instant.nullable(),
    errors: z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER).nullable(),
  })
  .superRefine(({ state, sampledAt, lastRunAt, errors }, ctx) => {
    const present = state === "Recorded" || state === "Overdue" ? ["sampledAt", "lastRunAt", "errors"]
      : state === "NoneRecorded" ? ["sampledAt"] : [];
    fieldsMatchState(ctx, state, { sampledAt, lastRunAt, errors }, present);
  });

export const servicesObservationSchema = z.strictObject({
  staleAfterSeconds: limit,
  workerAliveWithinSeconds: limit,
  importOverdueAfterSeconds: limit,
  backgroundJobs: backgroundJobsSchema,
  volatileStore: volatileStoreSchema,
  jobImport: jobImportSchema,
});
export type ServicesObservationDto = z.infer<typeof servicesObservationSchema>;
