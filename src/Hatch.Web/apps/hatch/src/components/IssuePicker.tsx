/* Picking one issue out of sixty - or, with `lookup`, out of every issue the
   house holds.

   A native <select> does prefix typeahead on the option's leading text, so
   typing `745` finds nothing when every option starts with the project key -
   the only thing that matches is typing the project key and then the number
   faster than the browser's typeahead timeout. This is a button that opens a
   popup holding a search box and a list, driven by the keyboard end to end.

   Mostly self-contained, like IssuePeek: it knows nothing about issues beyond
   the cards it is handed, and every ordering and matching decision it makes
   over them is made in lib/issuePicker.ts, which is where they are tested.
   `lookup`, where a caller offers it, is the one way in which it does reach
   past the cards it was handed - the request itself lives in
   lib/usePickerLookup.ts, and what the answer adds, says and settles in
   lib/pickerLookup.ts. What is here is state and events.

   It stays in apps/hatch rather than @hatch/ui, since this is its only
   consumer - promoting it there is a move to make once something else needs
   the same idea. */

import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { pickerRows } from '../lib/issuePicker';
import { lookupView, mergePickerRows, pickerNote, settleHighlight } from '../lib/pickerLookup';
import type { PickedRow } from '../lib/pickerLookup';
import { usePickerLookup } from '../lib/usePickerLookup';
import type { IssueCard } from '../types';

/** The row that clears the value. Pinned first, and never filtered out. */
const NONE = '— none —';

export interface IssuePickerProps {
  /** The key in force, or null for none. It need not be among `candidates`: a
      retype does not re-check the parent (StageEditAsync only resolves
      ParentKey when the patch names it), so an issue can hold a parent its own
      type would now refuse, and that key still has to be drawn. */
  value: string | null;
  /** The legal candidates, in any order - the component orders them. */
  candidates: IssueCard[];
  /** The accessible name. "Parent" at the only call site today. */
  label: string;
  /** What the popup says where the candidates would be when there are none. */
  emptyMessage: string;
  /** Writes the choice: the key, or '' for the clear - the shape patchIssue
      already takes. `picked` is the row taken, or null for the clear, for a
      caller that needs more than the key; most ignore it. The caller owns
      the request and the refusal; this awaits only to know when the press is
      over, so `onChange` must not throw. */
  onChange: (key: string, picked: PickedRow | null) => Promise<unknown>;
  /** Whether the pinned clear row is offered. False on a picker whose press
      adds something rather than replacing what is there - there is nothing to
      clear, and removing is a button on the row that was added. */
  allowNone?: boolean;
  /** What the clear row says. Defaults to `— none —`; a field whose empty
      state means "any" rather than "none" says so here. Also the face when
      `value` is null and no `placeholder` is set. */
  noneLabel?: string;
  /** The face when `value` is null. Defaults to the clear row's own label,
      which is the right face for a field that holds one value and reads as
      empty, and the wrong one for a control that adds. */
  placeholder?: string;
  /** A project whose candidates are listed before every other project's. Set
      by the two Parent fields, where the list spans projects and the issue's
      own is the likelier answer; unset, the list is project A to Z. */
  homeProject?: string;
  /** Offers a server lookup for what the page's own candidates do not cover -
      an issue closed before the board read it, say. Unset, the picker
      behaves exactly as it always has: candidates alone, fetching nothing.
      `projectIds` is the lookup's scope, none meaning every project; `accept`
      is run over a found row the way the candidates were already filtered,
      since a row the lookup turns up may be a relative the page cannot see
      and so never excluded by `candidates` at all. */
  lookup?: { projectIds: number[]; accept: (row: PickedRow) => boolean };
  /** The row `value` names, kept by the caller rather than found among
      `candidates`. Needed where a caller can lose a looked-up pick out from
      under this component - the New issue dialog unmounts this picker on a
      type change or a Cancel, so `lastTaken` below is gone by the time it
      remounts, and without this the face would read a bare key rather than
      `KEY — title`. Unset where the caller holds nothing past what this
      component already tracks, which is every other call site today. */
  valueRow?: PickedRow | null;
}

export function IssuePicker({
  value,
  candidates,
  label,
  emptyMessage,
  onChange,
  allowNone = true,
  noneLabel = NONE,
  placeholder,
  homeProject,
  lookup,
  valueRow,
}: IssuePickerProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  // Nullable: when nothing matches what was typed there is nothing to take,
  // and Enter must do nothing until ↓ moves the highlight onto — none —.
  const [highlight, setHighlight] = useState<number | null>(null);
  // Its own state rather than a `busy` prop, following DescriptionEditor: this
  // is exactly the span of the promise it is already awaiting, and two
  // booleans for one await is two things that can come to disagree.
  const [saving, setSaving] = useState(false);
  // The row `take` last took, kept only so the face can read `KEY — title`
  // for a row a lookup found - `candidates` never held it, and never will.
  const [lastTaken, setLastTaken] = useState<PickedRow | null>(null);

  // Open and offered a lookup at all; going false is also what tells the hook
  // to forget the last answer, so the next opening starts clean.
  const finished = usePickerLookup(query, open && lookup !== undefined, lookup?.projectIds ?? []);

  const container = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const active = useRef<HTMLLIElement>(null);
  /* Set while a save this control started is in flight. The trigger is
     disabled for that span, and a browser blurs an element it disables - so
     without this, taking a row with Enter would leave focus on the document
     and the next Tab would start again from the top of the page. */
  const returning = useRef(false);

  const base = useId();
  const listId = `${base}-list`;
  const rowId = (index: number) => `${base}-row-${index}`;

  /* null is the — none — row. Where it is offered it is always first and is
     never filtered, so clearing the value is always one press away and every
     index below is arithmetic on one list - `offset` is the whole of the
     difference when it is not. */
  const offset = allowNone ? 1 : 0;

  const pageRows = pickerRows(candidates, query, homeProject);
  const view = lookup ? lookupView(query, finished) : { rows: [], looking: false };
  const merged: PickedRow[] = lookup ? mergePickerRows(pageRows, view.rows, lookup.accept) : pageRows;
  const rows: (PickedRow | null)[] = allowNone ? [null, ...merged] : merged;
  const note = pickerNote({
    lookup: lookup !== undefined,
    candidates: candidates.length,
    rows: merged.length,
    typed: query,
    looking: view.looking,
  });

  /* A lookup's answer lands with no keystroke behind it, so nothing moves the
     highlight for it the way `typed` does for its own. Settled here instead,
     onto the first real row once one exists where there was none, or clamped
     onto the last row if the list it was on has since shrunk. */
  useEffect(() => {
    if (!open) return;
    setHighlight((h) => settleHighlight(h, rows.length, offset));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows.length, offset, open]);

  function openList() {
    const all = pickerRows(candidates, '', homeProject);
    const at = all.findIndex((c) => c.key === value);
    setQuery(''); // The last query is not remembered: a remembered filter is a list that opens lying about how many candidates there are.
    // The row in force, or the top of the list - which is — none — where it is
    // offered, and nowhere at all on an empty list, since Enter must never
    // reach past the end of one.
    setHighlight(at === -1 ? (rows.length > 0 ? 0 : null) : at + offset);
    setOpen(true);
  }

  function close() {
    setOpen(false);
    trigger.current?.focus();
  }

  function typed(next: string) {
    setQuery(next);
    /* The first candidate, never — none —. A pinned first row plus "the
       highlight sits on the first row that matches" is a control where typing
       `745` and pressing Enter clears the value: — none — is pinned rather
       than matched, so ↑ is what reaches it. */
    setHighlight(pickerRows(candidates, next, homeProject).length > 0 ? offset : null);
  }

  // One row at a time, stopping at the ends rather than wrapping - a wrap makes
  // "am I at the bottom" unanswerable without counting, and this list can be
  // sixty rows. From nowhere, ↓ lands on the first row and ↑ stays nowhere.
  const down = () =>
    setHighlight((h) => (rows.length === 0 ? null : h === null ? 0 : Math.min(h + 1, rows.length - 1)));
  const up = () => setHighlight((h) => (h === null ? null : Math.max(h - 1, 0)));

  async function take(row: PickedRow | null) {
    const key = row?.key ?? '';
    close();
    // Already in force: the server would no-op it, but a control that spends a
    // round trip and a page reload to do nothing feels broken on a slow link.
    if (key === (value ?? '')) return;

    setLastTaken(row);
    returning.current = true;
    setSaving(true);
    try {
      await onChange(key, row);
    } finally {
      setSaving(false);
    }
  }

  // Where the focus the disable took goes back. Keyed on `saving` rather than
  // done in `take`, because the button is still disabled on the render that
  // clears it and a disabled button cannot be focused.
  useEffect(() => {
    if (saving || !returning.current) return;
    returning.current = false;
    trigger.current?.focus();
  }, [saving]);

  function onKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault(); // so the caret does not jump to the end of the query
      down();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      up();
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (highlight !== null) void take(rows[highlight]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation(); // the picker used the key up - a Modal around it must not close too
      close();
    }
  }

  /* A pointer outside it closes it. Clicking a non-focusable area of the page
     fires no blur at all, which is why this exists as well as the focusout
     below. */
  useEffect(() => {
    if (!open) return;
    const outside = (e: PointerEvent) => {
      if (!container.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', outside);
    return () => document.removeEventListener('pointerdown', outside);
  }, [open]);

  /* `nearest` so the list does not jump when the row is already visible. A ref
     the highlighted row claims rather than document.getElementById: useId
     produces ids holding colons, which are legal in HTML and not legal in a
     selector without escaping. */
  useLayoutEffect(() => {
    if (open) active.current?.scrollIntoView({ block: 'nearest' });
  }, [open, highlight, query]);

  const chosen = candidates.find((c) => c.key === value) ?? null;
  // The caller's own memory of a looked-up pick, where it has one - see
  // `valueRow` above.
  const fromCaller = valueRow && valueRow.key === value ? valueRow : null;
  // A row the picker took but `candidates` was never handed - a lookup's find
  // - still reads as `KEY — title` rather than a bare key, as long as it is
  // still the value in force.
  const taken = lastTaken && lastTaken.key === value ? lastTaken : null;
  const found = chosen ?? fromCaller ?? taken;
  const face = value === null ? (placeholder ?? noneLabel) : found ? `${found.key} — ${found.title}` : value;

  return (
    <div
      className="hatch-issue-picker"
      ref={container}
      /* React's onBlur is focusout, so it bubbles: Tab out of the search box
         moves focus to the next control on the page natively and the popup
         goes with it, with no Tab handling and no preventDefault near it. */
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget)) setOpen(false);
      }}
    >
      <button
        type="button"
        ref={trigger}
        className="hatch-issue-picker-button"
        role="combobox"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        // The name is the label and nothing else: the face and the hint stay out of it.
        aria-label={label}
        title={label}
        disabled={saving}
        onClick={() => (open ? close() : openList())}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') {
            e.preventDefault();
            openList();
          }
        }}
      >
        {face}
      </button>

      {open && (
        <div className="hatch-issue-picker-popup">
          {/* type="text", not type="search": Escape inside a search input clears
              it in WebKit and Blink before the keydown reaches us, and base.css
              enumerates the input types it styles on purpose. */}
          <input
            type="text"
            className="hatch-issue-picker-search"
            // The popup is mounted fresh on every open, so this focuses each time.
            autoFocus
            value={query}
            aria-label={`Filter ${label.toLowerCase()} candidates`}
            aria-controls={listId}
            aria-autocomplete="list"
            // On a textbox, pointing at an option is what announces the
            // highlight without moving focus off the query.
            aria-activedescendant={highlight === null ? undefined : rowId(highlight)}
            onChange={(e) => typed(e.target.value)}
            onKeyDown={onKeyDown}
          />

          {note === 'empty' && <p className="hatch-issue-picker-note">{emptyMessage}</p>}
          {note === 'looking' && <p className="hatch-issue-picker-note">Looking…</p>}
          {note === 'nothing' && <p className="hatch-issue-picker-note">Nothing matches “{query.trim()}”.</p>}

          <ul className="hatch-issue-picker-list" id={listId} role="listbox" aria-label={label}>
            {rows.map((row, index) => {
              const key = row?.key ?? '';
              const current = key === (value ?? '');
              const classes = ['hatch-issue-picker-row'];
              if (index === highlight) classes.push('is-highlighted');
              if (current) classes.push('is-current');

              return (
                <li
                  key={key || NONE}
                  id={rowId(index)}
                  role="option"
                  aria-selected={current}
                  ref={index === highlight ? active : null}
                  className={classes.join(' ')}
                  /* The press must not blur the search box: a blur closes the
                     popup and the click then lands on a node that is no longer
                     there. */
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => void take(row)}
                  onMouseEnter={() => setHighlight(index)}
                >
                  {row ? (
                    <>
                      <span className="hatch-issue-picker-key">{row.key}</span>
                      {row.title}
                      {/* aria-hidden: aria-selected above already says this row
                          is the one in force, and a tick read out as well says
                          it twice. This is the same fact, for an eye. */}
                      {current && (
                        <span className="hatch-issue-picker-mark" aria-hidden="true">
                          ✓
                        </span>
                      )}
                    </>
                  ) : (
                    noneLabel
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
