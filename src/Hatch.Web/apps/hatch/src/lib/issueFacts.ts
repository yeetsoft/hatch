/* The sentences read mode draws for the fields that have no component of
   their own to render them - the same reason lib/assignee.ts and lib/wip.ts
   exist apart from their pages. Pure functions over the issue, no DOM, so
   §6.8 and §6.9's wording is a thing a test can pin. */

import type { Assignee } from '../types';

export function assigneeFact(assignee: Assignee | null): string {
  if (!assignee) return '—';
  return `${assignee.name} (${assignee.kind})`;
}

export function modelFact(modelOverride: string | null): string {
  return modelOverride ?? 'the playbook decides';
}

export function effortFact(effortOverride: string | null): string {
  return effortOverride ?? 'the playbook decides';
}

export function wipLimitFact(wipLimit: number | null): string {
  return wipLimit === null ? 'no limit' : String(wipLimit);
}
