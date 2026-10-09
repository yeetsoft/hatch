/* The sentences read mode draws for the fields that have no component of
   their own to render them - the same reason lib/assignee.ts and lib/wip.ts
   exist apart from their pages. Pure functions over the issue, no DOM, so
   §6.8 and §6.9's wording is a thing a test can pin.

   priorityFact and expressFact echo PriorityControl's and ExpressControl's
   own non-person fallback text on purpose - see IssueFacts.tsx - rather than
   being imported from there, because one is `directory`-gated inside a live
   control and the other never sees a directory at all. */

import type { Assignee } from '../types';
import type { Priority } from '../components/PriorityControl';
import { DEFAULT_EPIC_WIP_LIMIT } from './wip';

export function assigneeFact(assignee: Assignee | null): string {
  if (!assignee) return '—';
  return `${assignee.name} (${assignee.kind})`;
}

/* charAt(0).toUpperCase() happens to match every one of LEVELS' six `word`s
   (Normal, Expedited, Emergency, Low, Economy, Paused) - none has an
   irregular capitalization - so there is no shared word table to import and
   drift against. */
export function priorityFact(priority: Priority, priorityFrom: string | null): string {
  const word = priority.charAt(0).toUpperCase() + priority.slice(1);
  return priorityFrom ? `${word} · inherited from ${priorityFrom}` : word;
}

export function expressFact(express: boolean): string {
  return express ? 'Express' : 'Not express';
}

export function modelFact(modelOverride: string | null): string {
  return modelOverride ?? 'the playbook decides';
}

export function effortFact(effortOverride: string | null): string {
  return effortOverride ?? 'the playbook decides';
}

export function wipLimitFact(wipLimit: number | null): string {
  return wipLimit === null ? `${DEFAULT_EPIC_WIP_LIMIT} (default)` : String(wipLimit);
}
