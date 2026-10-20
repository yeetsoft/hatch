/* The docs sidebar's search field: typing lists pages and headings under it,
   the keyboard drives the list, and taking a result opens that page at that
   heading. The query is this component's own state - HA-1062 moves it into
   the address, and nothing here should make that harder than replacing one
   useState.

   Not a <OmniBar>: that is a dialog with a scrim, a focus guard and a
   dismissal that gives focus back. This is an ordinary control in the
   sidebar - it keeps focus, a click elsewhere just moves on, and Escape
   clears the field rather than dismissing anything.

   Every search and keyboard decision is lib/docsSearch.ts's (HA-1063); what is
   here is state and events, the same split OmniBar draws against lib/goTo.ts. */

import { useId, useMemo, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  docSearchStatus,
  keepDocHighlight,
  moveDocHighlight,
  resultId,
  searchDocs,
  type DocResult,
} from '../lib/docsSearch';
import type { Mark } from '../lib/goTo';

export interface DocsSearchProps {
  /** Renders already open with this query, for a test (and for HA-1062, which
      will drive it from the address instead of a bare useState) - the app
      itself never passes it. */
  initialQuery?: string;
}

/** The text with its matched runs lit. Copied from OmniBar's own `Marked`
    rather than imported: that one is local to its file on purpose. */
function Marked({ text, marks }: { text: string; marks: Mark[] }): ReactNode {
  const parts: ReactNode[] = [];
  let at = 0;
  for (const mark of marks) {
    if (mark.start > at) parts.push(text.slice(at, mark.start));
    parts.push(<mark key={mark.start}>{text.slice(mark.start, mark.end)}</mark>);
    at = mark.end;
  }
  if (at < text.length) parts.push(text.slice(at));
  return parts;
}

function docPath(result: DocResult): string {
  return result.anchor ? `/docs/${result.slug}#${result.anchor}` : `/docs/${result.slug}`;
}

export function DocsSearch({ initialQuery = '' }: DocsSearchProps = {}) {
  const [query, setQuery] = useState(initialQuery);
  /* A result id, not an index, and null for "the first row" - keepDocHighlight's
     reason (docsSearch.ts): a result list that changed under the highlight
     must not leave it pointing at a row that is gone. */
  const [highlight, setHighlight] = useState<string | null>(null);
  /* Set once a result is taken, so the list closes while the field keeps its
     text; cleared on the next keystroke rather than the query, which would
     lose it instead. */
  const [taken, setTaken] = useState(false);

  const navigate = useNavigate();
  const base = useId();
  const listId = `${base}-list`;

  const { results, total } = useMemo(() => searchDocs(query), [query]);
  const active = keepDocHighlight(results, highlight);
  const open = query.trim() !== '' && !taken;

  function take(result: DocResult) {
    setTaken(true);
    navigate(docPath(result));
  }

  function onKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault(); // so the caret does not jump to the end of the field
      setHighlight(moveDocHighlight(results, active, 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlight(moveDocHighlight(results, active, -1));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const result = results.find((r) => resultId(r) === active);
      if (result) take(result);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      setQuery('');
    }
  }

  return (
    <div className="hatch-docs-search">
      {/* type="text", not type="search": Escape inside a search input clears it
          in WebKit and Blink before the keydown reaches us (OmniBar.tsx:155). */}
      <input
        type="text"
        className="hatch-docs-search-input"
        autoComplete="off"
        autoCapitalize="off"
        spellCheck={false}
        value={query}
        aria-label="Search docs"
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && active !== null ? active : undefined}
        onChange={(e) => {
          setQuery(e.target.value);
          setTaken(false);
          setHighlight(null);
        }}
        onKeyDown={onKeyDown}
      />

      {open && (
        <ul className="hatch-docs-search-list" id={listId} role="listbox" aria-label="Docs search results">
          {results.map((result) => {
            const id = resultId(result);
            const isActive = id === active;

            return (
              <li
                key={id}
                id={id}
                role="option"
                aria-selected={isActive}
                className={`hatch-docs-search-row${isActive ? ' is-active' : ''}`}
              >
                <a
                  href={docPath(result)}
                  tabIndex={-1}
                  // The press must not take focus off the field - a blur here
                  // has nothing to undo, but the next keystroke is one query
                  // away only if the field still has focus.
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={(e) => {
                    e.preventDefault();
                    take(result);
                  }}
                >
                  <span className="hatch-docs-search-title">{result.title}</span>
                  {result.heading !== null && <span className="hatch-docs-search-heading">{result.heading}</span>}
                  <span className="hatch-docs-search-snippet">
                    <Marked text={result.snippet} marks={result.marks} />
                  </span>
                </a>
              </li>
            );
          })}
        </ul>
      )}

      <span className="hatch-docs-search-status" aria-live="polite">
        {docSearchStatus(query, results.length, total)}
      </span>
    </div>
  );
}
