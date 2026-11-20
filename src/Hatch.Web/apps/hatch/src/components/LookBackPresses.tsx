import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@hatch/ui';
import { addComment, overturnQuestion, promoteQuestion } from '../api/client';
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
  ambitionKey,
  onDone,
}: {
  issueKey: string;
  questionId: number;
  options: QuestionOption[] | null;
  /** The label the work went ahead on, or null where none was recommended. */
  wentAheadOn: string | null;
  /** The key of the ambition above this question's issue, or null or
      undefined where none stands above it - the checkbox draws only where
      there is an ambition to promote into. */
  ambitionKey?: string | null;
  /** Keep landed, or the person pressed Next after an overturn or a promotion. */
  onDone: () => void;
}) {
  const [overturning, setOverturning] = useState(false);
  const [chosen, setChosen] = useState<string | null>(null);
  const [body, setBody] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filed, setFiled] = useState<string | null>(null);
  const [promoting, setPromoting] = useState(false);
  const [principle, setPrinciple] = useState('');
  const [promoted, setPromoted] = useState<string | null>(null);
  // Set once the question's own answer (Keep or Overturn) has landed and a
  // promotion was asked for - whatever the promotion itself does next, the
  // answer is not retried, and the checkbox is not offered again.
  const [answered, setAnswered] = useState(false);

  // What overturning can say instead: everything but the option that was taken.
  const others = (options ?? []).filter((o) => o.label !== wentAheadOn);
  // Derived, as in Asked: the pressed option is the one the box says exactly.
  const answer = body.trim() || chosen;

  async function promoteAfterAnswer() {
    setAnswered(true);
    try {
      const result = await promoteQuestion(issueKey, questionId, principle.trim());
      setPromoted(result.ambitionKey);
    } catch (err) {
      // Never clears `principle`: the answer already landed, so this is the
      // only thing left to retry.
      setError(message(err));
    }
  }

  async function keep() {
    if (wentAheadOn === null) return;
    setSaving(true);
    setError(null);
    try {
      await addComment(issueKey, { body: wentAheadOn, kind: 'answer', answersId: questionId });
      if (promoting) await promoteAfterAnswer();
      else onDone();
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
      if (promoting) await promoteAfterAnswer();
    } catch (err) {
      // Never clears `chosen` or `body`: the person's decision stays to be sent again.
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  if (filed !== null || answered) {
    return (
      <div className="hatch-lookback-presses">
        {filed !== null && (
          <p>
            Filed <Link to={`/issues/${filed}`}>{filed}</Link> to bring the work in line.
          </p>
        )}
        {promoted !== null && (
          <p>
            Now a principle of <Link to={`/issues/${promoted}`}>{promoted}</Link>.
          </p>
        )}
        {error && <p className="text-danger">{error}</p>}
        <Button variant="primary" onClick={onDone}>
          Next
        </Button>
      </div>
    );
  }

  return (
    <div className="hatch-lookback-presses">
      {ambitionKey && (
        <label>
          <input
            type="checkbox"
            checked={promoting}
            onChange={(e) => {
              const checked = e.target.checked;
              setPromoting(checked);
              if (checked && !principle) {
                const given = (overturning ? answer : wentAheadOn)?.replace(/\s+/g, ' ').trim();
                if (given) setPrinciple(given);
              }
            }}
          />
          Make this a principle
        </label>
      )}
      {promoting && (
        <div className="hatch-answer-box">
          <input
            type="text"
            value={principle}
            aria-label="Principle"
            placeholder="The principle, in a sentence."
            onChange={(e) => setPrinciple(e.target.value)}
          />
        </div>
      )}
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
