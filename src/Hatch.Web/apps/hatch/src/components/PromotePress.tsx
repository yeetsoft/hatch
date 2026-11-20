import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@hatch/ui';
import { promoteQuestion } from '../api/client';
import { message } from '../lib/errors';
import type { Comment } from '../types';

/** The newest answer to a question, or null where it has none - the one a
    promote press offers up, the same way the server picks when no sentence
    is sent. Shared by the issue page and this component's own test. */
export function promotableAnswer(answers: Comment[]): Comment | null {
  if (answers.length === 0) return null;
  return answers.reduce((newest, a) =>
    a.createdAt > newest.createdAt || (a.createdAt === newest.createdAt && a.id > newest.id) ? a : newest,
  );
}

/**
 * The press that turns an answered question's answer into a principle of
 * the ambition above it: closed, a button; open, the sentence prefilled
 * from the answer, and Promote. One component for the issue page and the
 * ballot's look-back cards, so the two cannot come to draw it differently.
 *
 * Renders nothing where `ambitionKey` is null or undefined - a question with
 * no ambition above it has nothing to promote into.
 */
export function PromotePress({
  issueKey,
  questionId,
  ambitionKey,
  answer,
}: {
  issueKey: string;
  questionId: number;
  ambitionKey: string | null | undefined;
  answer: string;
}) {
  const [open, setOpen] = useState(false);
  const [text, setText] = useState(() => answer.replace(/\s+/g, ' ').trim());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);

  if (!ambitionKey) return null;

  async function promote() {
    setSaving(true);
    setError(null);
    try {
      const result = await promoteQuestion(issueKey, questionId, text);
      setDone(result.ambitionKey);
    } catch (err) {
      // Never clears `text`: the person's sentence stays to be sent again.
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  if (done !== null) {
    return (
      <p>
        Now a principle of <Link to={`/issues/${done}`}>{done}</Link>.
      </p>
    );
  }

  if (!open) {
    return (
      <Button onClick={() => setOpen(true)}>
        Make this a principle
      </Button>
    );
  }

  return (
    <div className="hatch-promote-press">
      <div className="hatch-answer-box">
        <input
          type="text"
          value={text}
          aria-label="Principle"
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.nativeEvent.isComposing && !saving) void promote();
          }}
        />
        <Button variant="primary" loading={saving} onClick={() => void promote()}>
          Promote
        </Button>
      </div>
      {error && <p className="text-danger">{error}</p>}
    </div>
  );
}
