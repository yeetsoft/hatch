import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@hatch/ui';
import { addComment, overturnQuestion } from '../api/client';
import { QuestionOptions } from './OpenQuestions';
import { message } from '../lib/errors';
import type { QuestionOption } from '../types';

/**
 * The two presses on a question the loop decided without the person: Keep,
 * which answers with the option the work went ahead on, and Overturn, which
 * answers with something else and files one task to bring the work in line.
 *
 * One component for both Ballot views, so they cannot come to disagree. It
 * holds no copy of the question: a press that lands leaves the page to read
 * again through `onDone`, and the card moves on.
 */
export function LookBackPresses({
  issueKey,
  questionId,
  options,
  wentAheadOn,
  onDone,
}: {
  issueKey: string;
  questionId: number;
  options: QuestionOption[] | null;
  /** The label the work went ahead on, or null where none was recommended. */
  wentAheadOn: string | null;
  /** Keep landed, or the person pressed Next after an overturn. */
  onDone: () => void;
}) {
  const [overturning, setOverturning] = useState(false);
  const [chosen, setChosen] = useState<string | null>(null);
  const [body, setBody] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filed, setFiled] = useState<string | null>(null);

  // What overturning can say instead: everything but the option that was taken.
  const others = (options ?? []).filter((o) => o.label !== wentAheadOn);
  // Derived, as in Asked: the pressed option is the one the box says exactly.
  const answer = body.trim() || chosen;

  async function keep() {
    if (wentAheadOn === null) return;
    setSaving(true);
    setError(null);
    try {
      await addComment(issueKey, { body: wentAheadOn, kind: 'answer', answersId: questionId });
      onDone();
    } catch (err) {
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  async function overturn() {
    if (!answer) return;
    setSaving(true);
    setError(null);
    try {
      const result = await overturnQuestion(issueKey, questionId, answer);
      setFiled(result.taskKey);
    } catch (err) {
      // Never clears `chosen` or `body`: the person's decision stays to be sent again.
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  if (filed !== null) {
    return (
      <div className="hatch-lookback-presses">
        <p>
          Filed <Link to={`/issues/${filed}`}>{filed}</Link> to bring the work in line.
        </p>
        <Button variant="primary" onClick={onDone}>
          Next
        </Button>
      </div>
    );
  }

  return (
    <div className="hatch-lookback-presses">
      <div className="hatch-lookback-presses-row">
        <Button variant="primary" loading={saving && !overturning} disabled={wentAheadOn === null || saving} onClick={() => void keep()}>
          Keep
        </Button>
        {!overturning && (
          <Button disabled={saving} onClick={() => setOverturning(true)}>
            Overturn
          </Button>
        )}
      </div>
      {wentAheadOn === null && <p className="text-muted">No option was recommended, so there is nothing to keep.</p>}

      {overturning && (
        <>
          {others.length > 0 && (
            <QuestionOptions
              options={others}
              chosen={body.trim() ? null : chosen}
              onChoose={(o) => {
                setChosen(o.label);
                setBody('');
              }}
            />
          )}
          <div className="hatch-answer-box">
            <input
              type="text"
              value={body}
              aria-label="Your answer"
              placeholder={others.length > 0 ? 'Or say something else.' : 'The decision, in a sentence.'}
              onChange={(e) => setBody(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.nativeEvent.isComposing && answer && !saving) void overturn();
              }}
            />
            <Button variant="primary" loading={saving} disabled={!answer} onClick={() => void overturn()}>
              Overturn
            </Button>
          </div>
        </>
      )}
      {error && <p className="text-danger">{error}</p>}
    </div>
  );
}
