import type { ReactNode } from 'react';
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

/** A native `<select>` wearing the bar's look: the wrapper draws the border and
    the name, and the select inside is stripped down to its text. Wrapped rather
    than replaced, so the picker, the keyboard and the mobile sheet stay the
    browser's. */
export function Facet({ label, lit, children }: { label: string; lit: boolean; children: ReactNode }) {
  return (
    <label className={facetClass(lit)}>
      <span className="hatch-facet__label">{label}</span>
      {children}
      <Caret />
    </label>
  );
}
