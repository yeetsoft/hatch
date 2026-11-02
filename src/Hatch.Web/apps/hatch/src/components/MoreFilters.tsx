import { Menu } from '@hatch/ui';
import type { Assignee } from '../types';
import { UNASSIGNED, assigneeToken } from '../lib/assignee';
import type { CardFilter } from '../lib/filter';
import { facetClass } from '../lib/facet';
import type { FacetOption } from './Facet';

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
 * the Assignee. The button is lit while any filter behind it is set, so a
 * filtered board never looks unfiltered, and its tooltip names what is set.
 *
 * Radio rows and not a native <select>: a native picker opened inside a panel
 * that closes on an outside press and on blur is a risk in Safari, and the
 * Types panel already proves the rows.
 */
export function MoreFilters({
  filter,
  assignees,
  onChange,
}: {
  filter: CardFilter;
  assignees: Assignee[];
  onChange: (next: CardFilter) => void;
}) {
  const options = assigneeOptions(assignees);
  const chosen = options.find((option) => option.value === filter.assignee);
  const set = filter.assignee !== '';
  const title = set ? `More filters - Assignee: ${chosen?.text ?? filter.assignee}` : 'More filters';

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
    </Menu>
  );
}
