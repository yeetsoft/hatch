/* A description editor's draft, kept apart from the component the way
   useAutoGrow.ts keeps its arithmetic apart from the layout effect that uses
   it - so the decision AC5 asks for (a stored value moving under a dirty
   draft) is a pure function, testable with no DOM. */

import { normalizeEol } from './text';

export interface Draft {
  /** The stored value this draft was last reconciled against. */
  known: string;
  /** What is in the box. */
  text: string;
  /** `known` moved under a dirty draft, and it has not been resolved. */
  changed: boolean;
}

export const openDraft = (value: string): Draft => ({ known: value, text: value, changed: false });

export const editDraft = (draft: Draft, text: string): Draft => ({ ...draft, text });

// Line endings alone are not an edit: the editor's text is always `\n`, and a
// stored description may not be.
export const isDirty = (draft: Draft): boolean => normalizeEol(draft.text) !== normalizeEol(draft.known);

// A clean draft takes the new stored value the way it always has - silently,
// since nothing of the editor's own is lost. A dirty one keeps its text and is
// flagged, since one of the two would otherwise vanish with no report.
export function receiveKnown(draft: Draft, value: string): Draft {
  if (value === draft.known) return draft;
  return isDirty(draft) ? { ...draft, known: value, changed: true } : openDraft(value);
}

// Drops what was typed - Cancel and "take the new version" are the same action.
export const revert = (draft: Draft): Draft => openDraft(draft.known);
