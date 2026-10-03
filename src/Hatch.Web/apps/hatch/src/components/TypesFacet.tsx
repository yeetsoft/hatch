import { Menu } from '@hatch/ui';
import { ISSUE_TYPES } from '../types';
import type { IssueType } from '../types';
import { toggleType, typesSummary } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import { Caret } from './Facet';
import { facetClass } from '../lib/facet';

/**
 * Which types the board draws: a control that says its value and opens a list
 * of checkboxes. Not a row of chips, which read as "three of four lit" and
 * made the default look like something had been switched off.
 *
 * On `Menu` for the panel's behaviour - Escape, an outside press, focus and
 * ARIA - with hover off, because a panel you tick things in must not open under
 * a passing pointer.
 */
export function TypesFacet({
  filter,
  counts,
  onChange,
}: {
  filter: CardFilter;
  /** Cards of each type on the whole board, hidden ones included. */
  counts: Record<IssueType, number>;
  onChange: (next: CardFilter) => void;
}) {
  const all = filter.types.length === ISSUE_TYPES.length;

  return (
    <Menu
      label="Types"
      hover={false}
      trigger={(props) => (
        <button {...props} className={facetClass(!all)}>
          <span className="hatch-facet__label">Types</span>
          {typesSummary(filter.types)}
          <Caret />
        </button>
      )}
    >
      <div className="hatch-types-panel" role="group" aria-label="Issue types">
        {ISSUE_TYPES.map((type) => {
          const on = filter.types.includes(type);
          const last = on && filter.types.length === 1;
          return (
            <label key={type} className="hatch-types-row" title={last ? 'Keep at least one type' : undefined}>
              <input type="checkbox" checked={on} disabled={last} onChange={() => onChange(toggleType(filter, type))} />
              <span className="hatch-types-name">{type}</span>
              <span className="hatch-types-count">{counts[type]}</span>
            </label>
          );
        })}
        <div className="hatch-types-footer">
          <button
            type="button"
            className="hatch-filter-clear"
            disabled={all}
            onClick={() => onChange({ ...filter, types: [...ISSUE_TYPES] })}
          >
            All
          </button>
        </div>
      </div>
    </Menu>
  );
}
