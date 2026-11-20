import { useId, useState } from 'react';
import type { ReactNode } from 'react';

/**
 * A row of the goose's page that opens in place: a button that is the whole
 * head of the row, and a region under it. Its actions sit beside the button and
 * never inside it - a button never holds a button, nor a link.
 *
 * The body is not drawn until the first press, so what it fetches is fetched
 * only for a row somebody opened, and it stays drawn (hidden) once closed, so
 * opening it again is not a second read.
 */
export function GooseRow({ head, actions, children }: { head: ReactNode; actions?: ReactNode; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [opened, setOpened] = useState(false);
  const bodyId = useId();

  return (
    <div className="hatch-goose-row">
      <div className="hatch-goose-bar">
        <button
          type="button"
          className="hatch-goose-toggle"
          aria-expanded={open}
          aria-controls={bodyId}
          onClick={() => {
            setOpen(!open);
            setOpened(true);
          }}
        >
          {head}
        </button>
        {actions && <span className="hatch-goose-actions">{actions}</span>}
      </div>
      {opened && (
        <div id={bodyId} className="hatch-goose-body" hidden={!open}>
          {children}
        </div>
      )}
    </div>
  );
}
