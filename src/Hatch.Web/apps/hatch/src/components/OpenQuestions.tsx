import { useState, type ReactNode } from 'react';
import { Button, Card } from '@hatch/ui';
import { addComment, setQuestionLevel } from '../api/client';
import { message } from '../lib/errors';
import { renderMarkdown } from '../lib/markdown';
import { canRaise, LEVEL_WORDS, strictestLevel, type AskedQuestion } from '../lib/questions';
import type { QuestionLevel, QuestionOption } from '../types';

const LEVELS: QuestionLevel[] = ['hold', 'build', 'merge'];

/**
 * The questions on this issue that nobody has answered, each with a box to
 * answer it in.
 *
 * A box per question rather than one for the lot: an answer is bound to the
 * question it settles (CommentCreateRequest.answersId), which is what lets two
 * questions on one ticket be decided a day apart, and what makes "is this still
 * waiting" a fact rather than a reading of the thread.
 *
 * Threaded from the comments the page already has - see lib/questions.ts for
 * why this is not a second request.
 */
export function Waiting({
  issueKey,
  questions,
  obviated,
  onAnswered,
  onLeveled,
  onError,
}: {
  issueKey: string;
  questions: AskedQuestion[];
  obviated: boolean;
  onAnswered: () => void;
  onLeveled: () => void;
  onError?: (message: string) => void;
}) {
  if (questions.length === 0) return null;

  return (
    <Card as="section" className={obviated ? undefined : 'hatch-waiting'}>
      <h2 className="hatch-section-title">
        {obviated ? `Asked (${questions.length})` : `Waiting on you (${questions.length})`}
      </h2>
      <p className="text-muted">
        {obviated ? shippedSentence(issueKey, questions) : leadSentence(issueKey, questions)}
      </p>

      <ul className="hatch-question-list">
        {questions.map((question) => (
          <Asked
            key={question.id}
            issueKey={issueKey}
            question={question}
            obviated={obviated}
            onAnswered={onAnswered}
            onLeveled={onLeveled}
            onError={onError}
          />
        ))}
      </ul>
    </Card>
  );
}

/** The card's first line on a ticket that has shipped. A question asked at
    `merge` was never waited for and still waits for a look back; any other is
    welcome, not needed. */
function shippedSentence(issueKey: string, questions: AskedQuestion[]): string {
  return questions.some((q) => q.level === 'merge')
    ? `${issueKey} shipped with these open. A question asked at merge waits for a look back; an answer to any other is welcome, not needed.`
    : `${issueKey} shipped with these open. An answer is welcome, not needed.`;
}

/** The card's first line: what happens to the ticket while these wait, which is
    what the strictest question among them allows. */
function leadSentence(issueKey: string, questions: AskedQuestion[]): string {
  switch (strictestLevel(questions)) {
    case 'build':
      return `${issueKey} will be built on the recommendation and wait in review until these are answered.`;
    case 'merge':
      return `${issueKey} will be built and merged on the recommendation; these wait for a look back.`;
    default:
      return `${issueKey} will not be picked up again until these are answered.`;
  }
}

/**
 * One question, and the answer being typed to it.
 *
 * Where the question offered choices, they are the interface: pressing one puts
 * its label in the box, and the box is still there for the case the asker did
 * not anticipate. That ordering is the whole point - a decision between named
 * things should cost one press, and the free text should be the escape hatch
 * rather than the only door.
 */
export function Asked({
  issueKey,
  question,
  obviated = false,
  onAnswered,
  onLeveled,
  onError,
  after,
}: {
  issueKey: string;
  question: AskedQuestion;
  /** The issue shipped: the level no longer matters, so it is not drawn. */
  obviated?: boolean;
  onAnswered: () => void;
  /** The level changed; the caller reads again what it holds. */
  onLeveled: () => void;
  onError?: (message: string) => void;
  /** Drawn under the options: what a caller adds for a question of its own kind. */
  after?: ReactNode;
}) {
  const [body, setBody] = useState('');
  const [leveling, setLeveling] = useState(false);
  // Never handed to onError: the refusal belongs beside the control that was
  // pressed, and the level stays what the prop says it is, so there is nothing
  // to put back.
  const [levelError, setLevelError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  // Only ever set where onError is absent: a caller that passes onError keeps
  // its own place to show the refusal, and this stays empty rather than the
  // sentence appearing twice.
  const [error, setError] = useState<string | null>(null);

  // Derived rather than held beside `body`: the highlight is "the box says
  // exactly this option", and a second piece of state saying the same thing is
  // a second thing that can come to disagree with the first.
  const chosen = question.options?.find((o) => o.label === body)?.label ?? null;

  async function submit() {
    setSaving(true);
    setError(null);
    try {
      await addComment(issueKey, { body, kind: 'answer', answersId: question.id });
      setBody('');
      onAnswered();
    } catch (err) {
      const m = message(err);
      if (onError) onError(m);
      else setError(m);
    } finally {
      setSaving(false);
    }
  }

  async function pickLevel(level: QuestionLevel) {
    setLeveling(true);
    setLevelError(null);
    try {
      await setQuestionLevel(issueKey, question.id, level);
      onLeveled();
    } catch (err) {
      setLevelError(message(err));
    } finally {
      setLeveling(false);
    }
  }

  return (
    <li className="hatch-question">
      <div className="hatch-comment-head">
        <strong>{question.author}</strong>
        <span className="text-muted">asked {new Date(question.askedAt).toLocaleString()}</span>
      </div>

      <div
        className="hatch-markdown hatch-question-body"
        dangerouslySetInnerHTML={{ __html: renderMarkdown(question.body) }}
      />

      {question.options && (
        <QuestionOptions options={question.options} chosen={chosen} onChoose={(o) => setBody(o.label)} />
      )}

      {after}

      {!obviated && (
        <QuestionLevelControl
          level={question.level}
          canRaise={canRaise(question)}
          busy={leveling}
          error={levelError}
          onPick={(level) => void pickLevel(level)}
        />
      )}

      <div className="hatch-answer-box">
        <input
          type="text"
          value={body}
          aria-label="Your answer"
          placeholder={question.options ? 'Or say something else.' : 'The decision, in a sentence.'}
          onChange={(e) => setBody(e.target.value)}
          // A bare Enter sends: this is one line, an answer and not a piece of
          // writing. An answer with a caveat under it is a comment, and the
          // Comments box under the thread is where a composed reply goes. Not
          // while an input method is composing - there Enter commits the
          // candidate, and is not the operator's to send.
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.nativeEvent.isComposing && body.trim() && !saving) void submit();
          }}
        />
        <Button variant="primary" loading={saving} disabled={!body.trim()} onClick={() => void submit()}>
          Answer
        </Button>
      </div>
      {error && <span className="text-danger">{error}</span>}
    </li>
  );
}

/**
 * A question's level in words, and the three buttons that change it.
 *
 * Presentational: it holds nothing, so the level it shows is always the one the
 * server last said. A question that offers no recommendation has nothing for
 * the work to build on, so it can only hold - `build` and `merge` are not drawn
 * at all rather than drawn and refused.
 */
export function QuestionLevelControl({
  level,
  canRaise,
  busy,
  error,
  onPick,
}: {
  level: QuestionLevel;
  canRaise: boolean;
  busy: boolean;
  error: string | null;
  onPick: (level: QuestionLevel) => void;
}) {
  return (
    <div className="hatch-question-level">
      <p className="hatch-question-level-words">{LEVEL_WORDS[level]}</p>
      <div className="hatch-question-level-row">
        <div className="hatch-question-level-steps" role="group" aria-label="Set level">
          {(canRaise ? LEVELS : LEVELS.filter((l) => l === 'hold')).map((name) => {
            const here = name === level;
            return (
              <button
                key={name}
                type="button"
                className={`hatch-question-level-step${here ? ' here' : ''}`}
                title={LEVEL_WORDS[name]}
                aria-pressed={here}
                disabled={here || busy}
                onClick={() => onPick(name)}
              >
                {name}
              </button>
            );
          })}
        </div>
        {error && <span className="text-danger">{error}</span>}
      </div>
      {!canRaise && (
        <p className="text-muted hatch-question-level-note">
          This question offers no recommendation, so it can only hold.
        </p>
      )}
    </div>
  );
}

/**
 * The answers a question offers.
 *
 * One component for both places they appear, because they are the same list
 * read for different reasons. With `onChoose` they are buttons, in the card
 * that is asking for a decision. Without it they are the record of what the
 * alternatives were, under the question in the thread - which is half of what
 * makes a decision legible later, and would be lost if the options were only
 * ever drawn while somebody could still press them.
 */
export function QuestionOptions({
  options,
  chosen,
  onChoose,
}: {
  options: QuestionOption[];
  chosen?: string | null;
  onChoose?: (option: QuestionOption) => void;
}) {
  return (
    <ul className={`hatch-options${onChoose ? '' : ' static'}`}>
      {options.map((option) => {
        const face = (
          <>
            <span className="hatch-option-label">
              {option.label}
              {option.recommended && <span className="hatch-option-recommended">recommended</span>}
            </span>
            {option.detail && <span className="hatch-option-detail">{option.detail}</span>}
          </>
        );

        return (
          <li key={option.label}>
            {onChoose ? (
              <button
                type="button"
                className={`hatch-option${chosen === option.label ? ' chosen' : ''}`}
                aria-pressed={chosen === option.label}
                onClick={() => onChoose(option)}
              >
                {face}
              </button>
            ) : (
              <div className="hatch-option">{face}</div>
            )}
          </li>
        );
      })}
    </ul>
  );
}
