import { Menu } from '@hatch/ui';
import type { Assignee } from '../types';
import { UNASSIGNED, assigneeToken } from '../lib/assignee';
import { filedBySummary } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import { facetClass } from '../lib/facet';
import type { FacetOption } from './Facet';
import { ChecklistPanel } from './ChecklistPanel';
import { filedByChange } from './FiledByFacet';

/**
 * The assignee choices, "anyone" first.
 *
 * "Unassigned" is its own row above the identities rather than derived from the
 * cards, because it is the one choice that is a fact about absence -
 * assigneeFacets can only report who is there.
 */
export function assigneeOptions(assignees: Assignee[]): FacetOption[] {
  return [
    { value: '', text: '— anyone —' },
    { value: UNASSIGNED, text: 'Unassigned' },
    ...assignees.map((assignee) => ({ value: assigneeToken(assignee), text: assignee.name })),
  ];
}

/**
 * The "…" button at the end of the desktop filter row: a popover holding the
 * filters used too seldom to earn room in a row that must fit one line - today
 * the Assignee and Filed by. The button is lit while any filter behind it is set, so a
 * filtered board never looks unfiltered, and its tooltip names what is set.
 *
 * Assignee is radio rows and not a native <select>: a native picker opened inside a panel
 * that closes on an outside press and on blur is a risk in Safari, and the
 * Types panel already proves the rows. Filed by is the same checklist Types is
 * (`ChecklistPanel`), under its own heading.
 */
export function MoreFilters({
  filter,
  assignees,
  filedBy,
  onChange,
}: {
  filter: CardFilter;
  assignees: Assignee[];
  /** Who filed the cards on the whole board, each with a count. */
  filedBy: { name: string; count: number }[];
  onChange: (next: CardFilter) => void;
}) {
  const options = assigneeOptions(assignees);
  const chosen = options.find((option) => option.value === filter.assignee);
  const names = filedBy.map((f) => f.name);
  const assigneeSet = filter.assignee !== '';
  const filedBySet = filter.filedBy !== null;
  const set = assigneeSet || filedBySet;
  const named = [
    assigneeSet && `Assignee: ${chosen?.text ?? filter.assignee}`,
    filedBySet && `Filed by: ${filedBySummary(filter.filedBy, names.length)}`,
  ].filter(Boolean);
  const title = set ? `More filters - ${named.join('; ')}` : 'More filters';

  return (
    <Menu
      label="More filters"
      hover={false}
      trigger={(props) => (
        <button {...props} className={`${facetClass(set)} hatch-more-filters`} title={title}>
          <svg className="hatch-more-filters-glyph" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
            <circle cx="5" cy="12" r="2" />
            <circle cx="12" cy="12" r="2" />
            <circle cx="19" cy="12" r="2" />
          </svg>
        </button>
      )}
    >
      <p className="hatch-more-filters-title">Assignee</p>
      <div className="hatch-types-panel" role="group" aria-label="Assignee">
        {options.map((option) => (
          <label key={option.value} className="hatch-types-row">
            <input
              type="radio"
              name="hatch-filter-assignee"
              checked={option.value === filter.assignee}
              onChange={() => onChange({ ...filter, assignee: option.value })}
            />
            <span>{option.text}</span>
          </label>
        ))}
      </div>
      <p className="hatch-more-filters-title">Filed by</p>
      <ChecklistPanel
        groupLabel="Filed by"
        options={filedBy.map((f) => ({ value: f.name, text: f.name, count: f.count }))}
        chosen={filter.filedBy ?? names}
        onChange={(next) => onChange(filedByChange(filter, next, names))}
      />
    </Menu>
  );
}
