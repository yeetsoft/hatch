/* The box on Dreams: open, borderless, with a rule under it and Post beneath.
   Source is folded behind a small control until it is asked for or typed in.
   The posted text falls into the rule as the box empties - a ghost copy, since
   the box itself is already empty by then. */

import { useRef, useState } from 'react';
import { Button } from '@hatch/ui';
import { MarkdownEditor } from './MarkdownEditor';
import { fadesOnPost, sourceOpen, submitDream } from '../lib/dreamComposer';
import { currentPlatform, postShortcutLabel } from '../lib/shortcuts';

export interface DreamComposerProps {
  /** Rejects with the server's reason; the composer shows it and keeps the draft. */
  onPost: (body: string, source: string | null) => Promise<void>;
}

export function DreamComposer({ onPost }: DreamComposerProps) {
  const [body, setBody] = useState('');
  const [source, setSource] = useState('');
  const [sourcePressed, setSourcePressed] = useState(false);
  const [posting, setPosting] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);
  const [ghost, setGhost] = useState<string | null>(null);
  const sourceInput = useRef<HTMLInputElement>(null);

  const open = sourceOpen(sourcePressed, source);

  async function post() {
    if (!body.trim() || posting) return;
    setPosting(true);
    setRefusal(null);

    const outcome = await submitDream(body, source, posting, onPost);
    if (outcome.kind === 'refused') {
      // The draft stays as typed, so a refusal costs nothing to retry.
      setRefusal(outcome.reason);
    } else if (outcome.kind === 'posted') {
      setBody('');
      setSource('');
      setSourcePressed(false);
      if (fadesOnPost(window.matchMedia('(prefers-reduced-motion: reduce)').matches)) setGhost(outcome.body);
    }
    setPosting(false);
  }

  return (
    <div className="hatch-dream-composer">
      <div className="hatch-dream-composer__box">
        <MarkdownEditor
          value={body}
          onChange={setBody}
          rows={3}
          deferred
          ariaLabel="Dream"
          placeholder="What are you dreaming about?"
          className="hatch-dream-box"
          onCtrlEnter={() => void post()}
        />
        {ghost !== null && (
          <div className="hatch-dream-composer__ghost" aria-hidden="true" onAnimationEnd={() => setGhost(null)}>
            {ghost}
          </div>
        )}
      </div>

      {refusal && (
        <p className="text-danger" role="alert">
          {refusal}
        </p>
      )}

      <div className="hatch-dream-composer__actions">
        <Button variant="primary" className="hatch-gradient-fill" loading={posting} disabled={!body.trim()} onClick={() => void post()}>
          Post
        </Button>
        <span className="hatch-dream-composer__hint">{postShortcutLabel(currentPlatform())}</span>
        <button
          type="button"
          className="hatch-dream-composer__source-toggle"
          aria-expanded={open}
          onClick={() => {
            // A typed source keeps it open; only an empty one folds.
            if (source !== '') return;
            if (sourcePressed) {
              setSourcePressed(false);
              return;
            }
            setSourcePressed(true);
            // The input mounts on this render; focus it once it has.
            requestAnimationFrame(() => sourceInput.current?.focus());
          }}
        >
          Source
        </button>
      </div>

      {open && (
        <input
          ref={sourceInput}
          type="text"
          className="hatch-dream-composer__source"
          aria-label="Source"
          placeholder="Where it came from (optional)"
          value={source}
          onChange={(e) => setSource(e.target.value)}
        />
      )}
    </div>
  );
}
