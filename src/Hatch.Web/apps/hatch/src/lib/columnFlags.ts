import type { Status } from '../types';

export interface ColumnFlag {
  label: string;
  /** The hover sentence on the header cell; the Done column's needs none. */
  title?: string;
}

/** Every boolean field of `Status`: the flags a column carries. */
export type StatusFlag = {
  [K in keyof Status]-?: Status[K] extends boolean ? K : never;
}[keyof Status];

/**
 * Every flag the Statuses screen draws a header for, and the one place it
 * lists them.
 *
 * A `Record` over `StatusFlag`, so a boolean added to `Status` does not compile
 * until it has a label here - and lib/docsCoverage.columnFlags.test.ts fails
 * until the label is named on the-board-and-its-columns.
 */
const FLAGS = {
  isTerminal: { label: 'Done column' },
  isDeferred: {
    label: 'Deferred',
    title: 'Parked work. Not drawn on the board and not dragged into - the issue page is the only way in.',
  },
  isWip: { label: 'WIP', title: 'Whether a story, bug or epic here counts towards its limit above.' },
  expressSkips: {
    label: 'Express skips',
    title:
      'An express issue standing here is carried on to the next column with no session, as long as it has no unanswered question. The terminal column is never entered.',
  },
  parentPulls: {
    label: 'Parent pulls',
    title:
      'A child standing here is carried on to the next column with no session while its parent stands in the implementation column.',
  },
  agentFiles: {
    label: 'Agent files here',
    title:
      'An issue filed by an agent is born in this column rather than the leftmost one. One column at a time - ticking this unticks whichever column held it - and with none ticked, agent-filed issues are born leftmost.',
  },
  isImplementation: {
    label: 'Code written here',
    title:
      "The column where code gets written: a dependency holds the move into it, and a story's open tasks hold the move out of it. One column at a time - ticking this unticks whichever column held it - and with none ticked, Hatch uses the column just left of review.",
  },
  isProtected: {
    label: 'Protected',
    title:
      "Only an owner or approver of the issue's project may move work into this column by hand - never a key, never a hop, never a dispatch.",
  },
  mergesPullRequest: {
    label: 'Merges PR',
    title:
      "The runner merges an issue's pull request once it stands here, current and green, with no session. Needs Protected ticked first.",
  },
} satisfies Record<StatusFlag, ColumnFlag>;

/** The order the headers are drawn in, left to right. */
const ORDER: StatusFlag[] = [
  'isTerminal',
  'isDeferred',
  'isWip',
  'expressSkips',
  'parentPulls',
  'agentFiles',
  'isImplementation',
  'isProtected',
  'mergesPullRequest',
];

export const COLUMN_FLAGS: ColumnFlag[] = ORDER.map((flag) => FLAGS[flag]);
