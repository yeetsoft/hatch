import { Menu } from '@hatch/ui';
import { Caret } from './Facet';
import { facetClass } from '../lib/facet';
import { toggleChoice } from '../lib/checklist';

/**
 * A control that says its value and opens a list of checkboxes. Not a row of
 * chips, which read as "three of four lit" and made the default look like
 * something had been switched off.
 *
 * On `Menu` for the panel's behaviour - Escape, an outside press, focus and
 * ARIA - with hover off, because a panel you tick things in must not open under
 * a passing pointer.
 */
export function ChecklistFacet({
  label,
  summary,
  groupLabel,
  options,
  chosen,
  onChange,
  lockTitle,
  allowNone = false,
}: {
  label: string;
  summary: string;
  groupLabel: string;
  options: { value: string; text: string; count?: number }[];
  chosen: string[];
  onChange: (next: string[]) => void;
  lockTitle: string;
  /**
   * Draws None beside All and lets the last ticked box be unticked. Scaffold
   * for HA-1843, which HA-1844 turns on for the reports and deletes.
   */
  allowNone?: boolean;
}) {
  const lit = chosen.length < options.length;

  return (
    <Menu
      label={label}
      hover={false}
      trigger={(props) => (
        <button {...props} className={facetClass(lit)}>
          <span className="hatch-facet__label">{label}</span>
          {summary}
          <Caret />
        </button>
      )}
    >
      <div className="hatch-types-panel" role="group" aria-label={groupLabel}>
        {options.map((option) => {
          const on = chosen.includes(option.value);
          const last = !allowNone && on && chosen.length === 1;
          return (
            <div key={option.value} className="hatch-types-item">
              <label className="hatch-types-label" title={last ? lockTitle : undefined}>
                <input
                  type="checkbox"
                  checked={on}
                  disabled={last}
                  onChange={() => onChange(toggleChoice(chosen, option.value, options.map((o) => o.value)))}
                />
                <span className="hatch-types-name">{option.text}</span>
                {option.count !== undefined && <span className="hatch-types-count">{option.count}</span>}
              </label>
              <button
                type="button"
                className="hatch-filter-clear hatch-types-only"
                aria-label={`Only ${option.text}`}
                onClick={() => onChange([option.value])}
              >
                Only
              </button>
            </div>
          );
        })}
        <div className="hatch-types-footer">
          <button
            type="button"
            className="hatch-filter-clear"
            disabled={!lit}
            onClick={() => onChange(options.map((o) => o.value))}
          >
            All
          </button>
          {allowNone && (
            <button type="button" className="hatch-filter-clear" disabled={chosen.length === 0} onClick={() => onChange([])}>
              None
            </button>
          )}
        </div>
      </div>
    </Menu>
  );
}
