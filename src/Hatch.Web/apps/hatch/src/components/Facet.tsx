import { facetClass } from '../lib/facet';

/** The caret `Menu`'s trigger draws, so a select and the Types control close on
    the same shape. */
export function Caret() {
  return (
    <svg className="hatch-facet__caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
      <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export type FacetOption = { value: string; text: string };

/** A native `<select>` wearing the bar's look. The select is laid over the whole
    box, transparent, so a press anywhere inside the border opens the browser's
    own picker - a label press only forwards focus, and no Safari can open a
    select from script. A transparent select draws no text, so the facet draws
    the selected option itself beneath it, every option stacked in one cell so
    the width stays the widest, as a native select's does. */
export function Facet({
  label,
  lit,
  value,
  options,
  onChange,
}: {
  label: string;
  lit: boolean;
  value: string;
  options: FacetOption[];
  onChange: (value: string) => void;
}) {
  return (
    <label className={facetClass(lit)}>
      <span className="hatch-facet__label">{label}</span>
      <span className="hatch-facet__value" aria-hidden="true">
        {options.map((option) => (
          <span
            key={option.value}
            className={
              option.value === value ? 'hatch-facet__option hatch-facet__option--shown' : 'hatch-facet__option'
            }
          >
            {option.text}
          </span>
        ))}
      </span>
      <Caret />
      <select value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.text}
          </option>
        ))}
      </select>
    </label>
  );
}
