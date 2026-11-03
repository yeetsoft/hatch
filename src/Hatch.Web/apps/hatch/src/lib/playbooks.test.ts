/* The coverage cases below mirror the dispatcher: Dispatch.Match and
   EfHatchPlaybook.Covers, pinned on the server by
   src/Hatch.Api.Tests/Hatch/DispatchTests.cs. A change to the rule on either
   side is a change to both. */

import { describe, expect, it } from 'vitest';
import {
  boardRowNote,
  briefLimitDraft,
  briefLimitRequest,
  budgetDraft,
  budgetRequest,
  cellsLeft,
  coverageCells,
  coverageOf,
  deniedToolsDraft,
  deniedToolsRequest,
  fileRowLabel,
  isOverridden,
  isReviewPlaybook,
  isUncheckedEdit,
  leftNote,
  matchesProjectFilter,
  notReadNote,
  playbookLabel,
  projectLabel,
  transitionLabel,
} from './playbooks';
import type { CoverageRow } from './playbooks';
import type { IssueType, Playbook, PlaybookFileRowDto, PlaybookShape } from '../types';

const move = { fromStatusId: 3, toStatusId: 4, fromStatusName: 'In Progress', toStatusName: 'In Review' };
const review = { fromStatusId: 4, toStatusId: 4, fromStatusName: 'In Review', toStatusName: 'In Review' };

describe('isReviewPlaybook', () => {
  it('is a row whose two ends are the same column', () => {
    expect(isReviewPlaybook(review)).toBe(true);
  });

  it('is not an ordinary move', () => {
    expect(isReviewPlaybook(move)).toBe(false);
  });
});

describe('transitionLabel', () => {
  it('names an ordinary move by its two ends', () => {
    expect(transitionLabel(move)).toBe('In Progress to In Review');
  });

  it('names the review playbook by its column and what it is for', () => {
    expect(transitionLabel(review)).toBe('In Review review');
  });
});

describe('the budget field', () => {
  it('shows no cap as a blank box', () => {
    expect(budgetDraft(null)).toBe('');
  });

  it('shows a held budget as its number', () => {
    expect(budgetDraft(3)).toBe('3');
  });

  it('trims the field for the wire', () => {
    expect(budgetRequest(' 3 ')).toBe('3');
  });

  it('a blank field clears the budget', () => {
    expect(budgetRequest('  ')).toBe('');
  });
});

describe('the brief limit field', () => {
  it('shows no cap as a blank box', () => {
    expect(briefLimitDraft(null)).toBe('');
  });

  it('shows a held brief limit as its number', () => {
    expect(briefLimitDraft(3)).toBe('3');
  });

  it('trims the field for the wire', () => {
    expect(briefLimitRequest(' 3 ')).toBe('3');
  });

  it('a blank field clears the brief limit', () => {
    expect(briefLimitRequest('  ')).toBe('');
  });
});

describe('the denied tools field', () => {
  it('shows no rules as a blank box', () => {
    expect(deniedToolsDraft([])).toBe('');
  });

  it('shows each rule on its own line', () => {
    expect(deniedToolsDraft(['Bash', 'WebFetch'])).toBe('Bash\nWebFetch');
  });

  it('a blank field clears the rules', () => {
    expect(deniedToolsRequest('')).toEqual([]);
  });

  it('splits the field into rules for the wire', () => {
    expect(deniedToolsRequest('Bash\nWebFetch')).toEqual(['Bash', 'WebFetch']);
  });

  it('drops blank lines and trims the rest', () => {
    expect(deniedToolsRequest('Bash\n\n  WebFetch  \n')).toEqual(['Bash', 'WebFetch']);
  });
});

describe('projectLabel', () => {
  it('reads a board-wide row as "All projects"', () => {
    expect(projectLabel({ projectKey: null, projectName: null })).toBe('All projects');
  });

  it('reads a project row as "KEY — Name"', () => {
    expect(projectLabel({ projectKey: 'AB', projectName: 'Alphabet' })).toBe('AB — Alphabet');
  });
});

describe('playbookLabel', () => {
  it('reads a board-wide row as "board-wide"', () => {
    expect(playbookLabel({ projectKey: null })).toBe('board-wide');
  });

  it("reads a project row as the project's own", () => {
    expect(playbookLabel({ projectKey: 'AER' })).toBe("AER's own");
  });
});

describe('fileRowLabel', () => {
  it('names the trunk and the short sha', () => {
    expect(fileRowLabel({ trunk: 'main', sha: 'abcdef0123456789' })).toBe(
      '.hatch/playbooks.yaml on main at abcdef0',
    );
  });
});

describe('coverage', () => {
  const T = 1;
  const U = 2;
  const row = (
    fromStatusId: number,
    toStatusId: number,
    types: IssueType[],
    shape: PlaybookShape = 'any',
  ): CoverageRow => ({ fromStatusId, toStatusId, types, shape });

  describe('coverageCells', () => {
    it('Covers: no types listed covers every type', () => {
      expect(coverageCells(row(T, U, [], 'leaf')).map((c) => c.type)).toEqual([
        'epic',
        'reflection',
        'story',
        'task',
        'bug',
      ]);
    });

    it('Covers: shape any covers leaf and parent', () => {
      expect(coverageCells(row(T, U, ['task'], 'any'))).toEqual([
        { type: 'task', shape: 'leaf' },
        { type: 'task', shape: 'parent' },
      ]);
    });

    it('Covers: shape leaf covers only leaves', () => {
      expect(coverageCells(row(T, U, ['task'], 'leaf'))).toEqual([{ type: 'task', shape: 'leaf' }]);
    });

    it('Covers: shape parent covers only parents', () => {
      expect(coverageCells(row(T, U, ['task'], 'parent'))).toEqual([{ type: 'task', shape: 'parent' }]);
    });

    it('Covers: the order and repeats of the listed types play no part', () => {
      expect(coverageCells(row(T, U, ['bug', 'task', 'bug'], 'leaf'))).toEqual(
        coverageCells(row(T, U, ['task', 'bug'], 'leaf')),
      );
    });
  });

  describe('coverageOf', () => {
    it('Match: an own row with no types overrides a board row naming one type', () => {
      const board = row(T, U, ['task']);
      const own = [row(T, U, [])];

      expect(coverageOf(board, own)).toBe('overridden');
      expect(cellsLeft(board, own)).toEqual([]);
    });

    it('Match: two own rows together cover what neither covers alone', () => {
      const board = row(T, U, ['task', 'bug']);

      expect(coverageOf(board, [row(T, U, ['task'])])).toBe('partly');
      expect(coverageOf(board, [row(T, U, ['task']), row(T, U, ['bug'])])).toBe('overridden');
    });

    it('Covers: an own leaf row leaves the parent half of an any row standing', () => {
      const board = row(T, U, ['story'], 'any');
      const own = [row(T, U, ['story'], 'leaf')];

      expect(coverageOf(board, own)).toBe('partly');
      expect(cellsLeft(board, own)).toEqual([{ type: 'story', shape: 'parent' }]);
    });

    it('Match: an own row for a type the board row does not name leaves it live', () => {
      const board = row(T, U, ['epic']);
      const own = [row(T, U, ['task'])];

      expect(coverageOf(board, own)).toBe('live');
      expect(cellsLeft(board, own)).toEqual(coverageCells(board));
    });

    it('Match: a candidate must share the exact (from, to)', () => {
      const board = row(T, U, ['task']);

      expect(coverageOf(board, [row(U, T, [])])).toBe('live');
      expect(coverageOf(board, [row(T, 3, [])])).toBe('live');
      expect(coverageOf(board, [row(3, U, [])])).toBe('live');
      expect(coverageOf(board, [row(T, T, []), row(U, U, [])])).toBe('live');
    });

    it('Match: with no own rows the board row is live', () => {
      expect(coverageOf(row(T, U, ['task']), [])).toBe('live');
    });

    it('Match: the own layer beats the board layer on coverage alone', () => {
      // A narrower own row still wins what it covers; a wider board row keeps the rest.
      const board = row(T, U, [], 'any');
      const own = [row(T, U, ['task'], 'leaf')];

      expect(coverageOf(board, own)).toBe('partly');
      expect(cellsLeft(board, own)).toHaveLength(9);
      expect(coverageOf(row(T, U, ['task'], 'leaf'), [row(T, U, [], 'any')])).toBe('overridden');
    });

    it('Match: the review playbook is an ordinary transition with equal ends', () => {
      const board = row(U, U, ['task']);

      expect(coverageOf(board, [row(U, U, ['task'])])).toBe('overridden');
      expect(coverageOf(board, [row(T, U, ['task'])])).toBe('live');
    });
  });

  describe('leftNote', () => {
    const note = (board: CoverageRow, own: CoverageRow[]) => leftNote(board, cellsLeft(board, own));

    it('words the parent half left by an own leaf row', () => {
      expect(note(row(T, U, ['story']), [row(T, U, ['story'], 'leaf')])).toBe('for parents only');
    });

    it('words the leaf half left by an own parent row', () => {
      expect(note(row(T, U, ['story']), [row(T, U, ['story'], 'parent')])).toBe('for leaves only');
    });

    it('words one type left', () => {
      expect(note(row(T, U, ['task', 'bug']), [row(T, U, ['task'])])).toBe('for bugs only');
    });

    it('lists several types in ISSUE_TYPES order, whatever order the row lists them in', () => {
      expect(note(row(T, U, ['bug', 'task', 'story']), [row(T, U, ['story'])])).toBe('for tasks and bugs only');
    });

    it('says stories, not storys', () => {
      expect(note(row(T, U, ['epic', 'story']), [row(T, U, ['epic'])])).toBe('for stories only');
    });

    it('narrows type and shape at once', () => {
      const board = row(T, U, ['story', 'task', 'bug']);
      const own = [row(T, U, ['story']), row(T, U, ['task', 'bug'], 'leaf')];

      expect(note(board, own)).toBe('for parent tasks and bugs only');
    });

    it('names each shape separately when what is left is not a rectangle', () => {
      expect(note(row(T, U, ['task', 'bug']), [row(T, U, ['task'], 'leaf')])).toBe('for leaf bugs; parent tasks and bugs');
    });

    it('never reads a partly overridden every-type row as unnarrowed', () => {
      expect(note(row(T, U, []), [row(T, U, ['task'], 'leaf')])).toBe(
        'for leaf epics, reflections, stories and bugs; parent epics, reflections, stories, tasks and bugs',
      );
    });

    it('is null for a live row', () => {
      expect(note(row(T, U, ['task']), [])).toBeNull();
      expect(note(row(T, U, ['task']), [row(U, T, [])])).toBeNull();
    });

    it('is null for an overridden row', () => {
      expect(note(row(T, U, ['task']), [row(T, U, [])])).toBeNull();
    });

    it('is null when left is empty', () => {
      expect(leftNote(row(T, U, ['task']), [])).toBeNull();
    });
  });

  describe('the same answers from either wire shape', () => {
    const fileRow = (r: CoverageRow): PlaybookFileRowDto => ({
      ...r,
      fromStatusName: 'From',
      toStatusName: 'To',
      prompt: 'Do the thing.',
      model: 'opus',
      effort: 'high',
      budget: 1,
      briefLimit: 2,
      deniedTools: ['Bash'],
      isCheck: false,
      quietLimitSeconds: null,
    });
    const playbook = (r: CoverageRow): Playbook => ({
      ...fileRow(r),
      id: 7,
      defaultPrompt: null,
      updatedAt: '2026-01-01T00:00:00Z',
      projectId: 9,
      projectKey: 'AB',
      projectName: 'Alphabet',
    });

    const cases: [string, CoverageRow, CoverageRow[], 'overridden' | 'partly' | 'live'][] = [
      ['overridden', row(T, U, ['task']), [row(T, U, [])], 'overridden'],
      ['partly', row(T, U, ['story']), [row(T, U, ['story'], 'leaf')], 'partly'],
      ['live', row(T, U, ['epic']), [row(T, U, ['task'])], 'live'],
    ];

    it.each(cases)('a %s row reads the same from a file row and from a playbook', (_name, board, own, expected) => {
      expect(coverageOf(fileRow(board), own.map(fileRow))).toBe(expected);
      expect(coverageOf(playbook(board), own.map(playbook))).toBe(expected);
      expect(cellsLeft(fileRow(board), own.map(fileRow))).toEqual(cellsLeft(playbook(board), own.map(playbook)));
    });

    it('a review row reads the same from either shape', () => {
      const board = row(U, U, ['task']);

      expect(coverageOf(playbook(board), [fileRow(row(U, U, ['task']))])).toBe('overridden');
      expect(coverageOf(playbook(board), [fileRow(row(T, U, ['task']))])).toBe('live');
    });
  });
});

describe('boardRowNote', () => {
  const T = 1;
  const U = 2;
  const row = (
    fromStatusId: number,
    toStatusId: number,
    types: IssueType[],
    shape: PlaybookShape = 'any',
  ): CoverageRow => ({ fromStatusId, toStatusId, types, shape });

  it('reads overridden by the project label once nothing is left', () => {
    const board = row(T, U, ['task']);
    const own = [row(T, U, [])];

    expect(boardRowNote(board, own, "AER's own")).toBe("overridden by AER's own");
  });

  it('reads overridden by the file for a repo-sourced project', () => {
    const board = row(T, U, ['task']);
    const own = [row(T, U, [])];

    expect(boardRowNote(board, own, 'the file')).toBe('overridden by the file');
  });

  it("reads leftNote's own words when partly overridden", () => {
    const board = row(T, U, ['story']);
    const own = [row(T, U, ['story'], 'leaf')];

    expect(boardRowNote(board, own, "AER's own")).toBe('for parents only');
  });

  it('is null for a row still fully live', () => {
    expect(boardRowNote(row(T, U, ['task']), [], "AER's own")).toBeNull();
  });
});

describe('notReadNote', () => {
  it('names the project reading its own file', () => {
    expect(notReadNote('AER')).toBe('not read while AER reads its file');
  });
});

describe('matchesProjectFilter', () => {
  const boardWide = { projectKey: null };
  const projectA = { projectKey: 'A' };
  const projectB = { projectKey: 'B' };

  it('keeps every row when the filter is cleared', () => {
    expect(matchesProjectFilter(boardWide, '')).toBe(true);
    expect(matchesProjectFilter(projectA, '')).toBe(true);
    expect(matchesProjectFilter(projectB, '')).toBe(true);
  });

  it('keeps the board-wide row and the matching project, and drops the other', () => {
    expect(matchesProjectFilter(boardWide, 'A')).toBe(true);
    expect(matchesProjectFilter(projectA, 'A')).toBe(true);
    expect(matchesProjectFilter(projectB, 'A')).toBe(false);
  });
});

describe('isUncheckedEdit', () => {
  it('is a check with no denied tools', () => {
    expect(isUncheckedEdit({ isCheck: true, deniedTools: [] })).toBe(true);
  });

  it('is not a check that denies at least one tool', () => {
    expect(isUncheckedEdit({ isCheck: true, deniedTools: ['Bash'] })).toBe(false);
  });

  it('is not a row that is not a check at all', () => {
    expect(isUncheckedEdit({ isCheck: false, deniedTools: [] })).toBe(false);
  });
});

describe('isOverridden', () => {
  it('is not a row with no prescribed text', () => {
    expect(isOverridden({ prompt: 'Do the thing.', defaultPrompt: null })).toBe(false);
  });

  it('is not a row whose prompt equals its prescribed text', () => {
    expect(isOverridden({ prompt: 'Do the thing.', defaultPrompt: 'Do the thing.' })).toBe(false);
  });

  it('is a row whose prompt differs from its prescribed text', () => {
    expect(isOverridden({ prompt: 'Do the thing.', defaultPrompt: 'Do it differently.' })).toBe(true);
  });

  it('compares against an empty prescribed text rather than treating it as none', () => {
    expect(isOverridden({ prompt: 'Do the thing.', defaultPrompt: '' })).toBe(true);
  });
});
