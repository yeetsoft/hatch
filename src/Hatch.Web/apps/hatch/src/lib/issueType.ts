import { ISSUE_TYPES } from '../types';
import type { IssueType } from '../types';

export type TypeShapeName = 'diamond' | 'hexagon' | 'circle' | 'square' | 'triangle';

export interface IssueTypeRow {
  name: IssueType;
  /** What `TypeBadge` prints. */
  word: string;
  shape: TypeShapeName;
  /** The face class the control's pill and steps wear. */
  class: string;
  title: string;
}

/** One row per type, in `ISSUE_TYPES` order - the one place that says what a
    type looks like and what it means. */
const ROWS: Record<IssueType, Omit<IssueTypeRow, 'name'>> = {
  epic: {
    word: 'epic',
    shape: 'diamond',
    class: 'epic',
    title: 'A large body of work, broken out into stories.',
  },
  reflection: {
    word: 'reflection',
    shape: 'hexagon',
    class: 'reflection',
    title: 'A look back at what happened, to learn from it.',
  },
  story: {
    word: 'story',
    shape: 'circle',
    class: 'story',
    title: 'Something one person gets once it lands, broken out into tasks.',
  },
  task: {
    word: 'task',
    shape: 'square',
    class: 'task',
    title: 'One piece of work, small enough for one increment.',
  },
  bug: {
    word: 'bug',
    shape: 'triangle',
    class: 'bug',
    title: 'Something that is broken and should work.',
  },
};

export const ISSUE_TYPE_ROWS: IssueTypeRow[] = ISSUE_TYPES.map((name) => ({ name, ...ROWS[name] }));

export const issueTypeRow = (type: IssueType): IssueTypeRow => ISSUE_TYPE_ROWS.find((row) => row.name === type)!;
