/* The docs sidebar's search field: typing lists pages and headings under it,
   the keyboard drives the list, and taking a result opens that page at that
   heading. The query is `?q=` on whatever docs address the reader is on, so a
   search is a link; the field itself holds a draft and the address follows it
   (SearchPage's shape, for its reason: react-router wraps a navigation's state
   update in a transition, and an input fed from it late drops characters).

   Not a <OmniBar>: that is a dialog with a scrim, a focus guard and a
   dismissal that gives focus back. This is an ordinary control in the
   sidebar - it keeps focus, a click elsewhere just moves on, and Escape
   clears the field rather than dismissing anything.

   Every search and keyboard decision is lib/docsSearch.ts's (HA-1063); what is
   here is state and events, the same split OmniBar draws against lib/goTo.ts. */

import { useEffect, useId, useMemo, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  docSearchStatus,
  docTarget,
  keepDocHighlight,
  moveDocHighlight,
  resultId,
  searchDocs,
  withDocQuery,
  type DocResult,
} from '../lib/docsSearch';
import type { Mark } from '../lib/goTo';

const DEBOUNCE_MS = 300;

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

export function DocsSearch() {
  const [params, setParams] = useSearchParams();
  const urlQ = params.get('q') ?? '';

  const [draft, setDraft] = useState(urlQ);
  const [lastQ, setLastQ] = useState(urlQ);
  /* A result id, not an index, and null for "the first row" - keepDocHighlight's
     reason (docsSearch.ts): a result list that changed under the highlight
     must not leave it pointing at a row that is gone. */
  const [highlight, setHighlight] = useState<string | null>(null);
  // The address's q changed from outside (Back, a pasted link) - adjusted
  // during render, the way SearchPage re-syncs its own draft.
  if (urlQ !== lastQ) {
    setLastQ(urlQ);
    setDraft(urlQ);
    setHighlight(null);
  }

  useEffect(() => {
    if (draft === urlQ) return;
    const timer = setTimeout(() => {
      setParams((current) => withDocQuery(current, draft), { replace: true });
    }, DEBOUNCE_MS);
    return () => clearTimeout(timer);
    // urlQ, not params: params is a new object every render, so depending on it
    // would restart the timer on any re-render and could starve it. The write
    // takes the current params from the updater instead, so it is never stale.
  }, [draft, urlQ, setParams]);

  /* A result was taken: carried in the navigation's own state rather than
     this component's, since three parents render it (DocsLanding, DocsArticle,
     DocsNotFound) and a phone's <details key={pathname}> remounts it on every
     navigation - a remounted useState would reopen the list on the page the
     reader just landed on. Closed only while the field still shows what was
     taken: the first keystroke makes the draft differ and reopens it, and the
     address write that follows carries no state, clearing the flag. A shared
     link or a cold load has no state, and opens. */
  const taken = (useLocation().state as { docsSearchTaken?: boolean } | null)?.docsSearchTaken === true;

  const navigate = useNavigate();
  const base = useId();
  const listId = `${base}-list`;

  const { results, total } = useMemo(() => searchDocs(draft), [draft]);
  const active = keepDocHighlight(results, highlight);
  const open = draft.trim() !== '' && !(taken && draft === urlQ);

  function take(result: DocResult) {
    navigate(docTarget(result, params, draft), { state: { docsSearchTaken: true } });
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
      setDraft('');
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
        value={draft}
        aria-label="Search docs"
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && active !== null ? active : undefined}
        onChange={(e) => {
          setDraft(e.target.value);
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
                  href={docTarget(result, params, draft)}
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
        {docSearchStatus(draft, results.length, total)}
      </span>
    </div>
  );
}
