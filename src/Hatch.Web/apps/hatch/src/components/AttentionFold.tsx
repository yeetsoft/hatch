import { useState, type MouseEvent, type ReactNode } from 'react';
import { sectionCollapsed } from '../lib/attention';

/**
 * Safari and Firefox on macOS do not focus a pressed button on their own;
 * without this, NavAttention's own `onBlur` sees the focus leave and closes
 * the panel the press was meant to open.
 */
const keepFocus = (e: MouseEvent<HTMLButtonElement>) => {
  e.preventDefault();
  e.currentTarget.focus();
};

/**
 * One Human or Agent section: a heading and the rows beneath it. With
 * nothing to show - no rows and no fact the empty state carries - the
 * heading becomes a disclosure button that starts closed and, on press,
 * shows the children (the caller's own empty-state paragraph or list). A
 * section with rows or a fact draws its heading plain and its children
 * always, exactly as it always has.
 *
 * `startOpen` only matters for the collapsible shape - it is the initial
 * value of `open`, so a static-markup test can see the empty words a press
 * would otherwise be needed to reveal.
 */
export function AttentionSection({
  title,
  rows,
  fact,
  startOpen,
  className,
  children,
}: {
  title: string;
  rows: number;
  fact?: boolean;
  startOpen?: boolean;
  className?: string;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(startOpen ?? false);
  const sectionClassName = `hatch-attention-section${className ? ` ${className}` : ''}`;

  if (!sectionCollapsed(rows, fact)) {
    return (
      <section className={sectionClassName}>
        <h3 className="hatch-attention-heading">{title}</h3>
        {children}
      </section>
    );
  }

  return (
    <section className={sectionClassName}>
      <h3 className="hatch-attention-heading">
        <button
          type="button"
          className="hatch-attention-fold"
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
          onMouseDown={keepFocus}
        >
          {title}
        </button>
      </h3>
      {open && children}
    </section>
  );
}

/**
 * One group of sections - Human or Agent. With every section inside it
 * empty, the `h2` becomes the same kind of disclosure button `AttentionSection`
 * draws for a `h3`, and its children only draw once opened. Not used until
 * HA-2193 wires the panel's two groups through it, but written and exported
 * now so that ticket only has to call it.
 */
export function AttentionGroup({
  title,
  collapsed,
  startOpen,
  children,
}: {
  title: string;
  collapsed: boolean;
  startOpen?: boolean;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(startOpen ?? false);

  if (!collapsed) {
    return (
      <div className="hatch-attention-group">
        <h2 className="hatch-attention-group-heading">{title}</h2>
        {children}
      </div>
    );
  }

  return (
    <div className="hatch-attention-group">
      <h2 className="hatch-attention-group-heading">
        <button
          type="button"
          className="hatch-attention-fold"
          aria-expanded={open}
          onClick={() => setOpen((o) => !o)}
          onMouseDown={keepFocus}
        >
          {title}
        </button>
      </h2>
      {open && children}
    </div>
  );
}
