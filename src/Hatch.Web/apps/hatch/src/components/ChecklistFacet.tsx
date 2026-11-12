import { Menu } from '@hatch/ui';
import { Caret } from './Facet';
import { facetClass } from '../lib/facet';
import { ChecklistPanel } from './ChecklistPanel';

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
}: {
  label: string;
  summary: string;
  groupLabel: string;
  options: { value: string; text: string; count?: number }[];
  chosen: string[];
  onChange: (next: string[]) => void;
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
      <ChecklistPanel groupLabel={groupLabel} options={options} chosen={chosen} onChange={onChange} />
    </Menu>
  );
}
