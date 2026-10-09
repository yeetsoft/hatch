import { useState, type ReactNode } from 'react';
import { Card } from '@hatch/ui';
import { readSectionStates, sectionOpens, writeSectionState } from '../lib/issueSections';

/**
 * The shape every collapsible section on the issue page shares: a `<Card>`
 * wrapping a `<details>` whose `<summary>` is the heading.
 *
 * `open` is held in its own state, seeded once from the stored choice (or the
 * default), rather than re-derived every render - the same reason
 * `PhoneColumn` (pages/BoardPage.tsx) holds its own: the issue page polls
 * (`mayRefresh`, lib/refresh.ts), and a poll landing while a section is open
 * must not re-close it.
 *
 * `<details>` rather than conditional rendering is deliberate: a closed
 * `<details>` keeps its children mounted, so collapsing the description
 * mid-edit and reopening it finds the draft still there. Do not replace this
 * with `{open && children}`.
 */
export function IssueSection({
  id,
  title,
  count,
  defaultOpen = true,
  children,
}: {
  /** The section's storage key - kebab-case, matching the row in the table
      this component was built off. */
  id: string;
  title: ReactNode;
  /** Drawn in parens after the title, the way History's event count already
      is. Undefined draws nothing. */
  count?: number;
  defaultOpen?: boolean;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(() => sectionOpens(readSectionStates(), id, defaultOpen));

  return (
    <Card>
      <details
        className="hatch-issue-section"
        open={open}
        onToggle={(e) => {
          const next = e.currentTarget.open;
          setOpen(next);
          writeSectionState(id, next);
        }}
      >
        <summary className="hatch-section-title">
          {title}
          {count !== undefined && ` (${count})`}
        </summary>
        {children}
      </details>
    </Card>
  );
}
