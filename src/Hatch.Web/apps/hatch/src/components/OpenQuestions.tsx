import { useState } from 'react';
import { Button, Card } from '@hatch/ui';
import { addComment } from '../api/client';
import { message } from '../lib/errors';
import { renderMarkdown } from '../lib/markdown';
import type { AskedQuestion } from '../lib/questions';
import type { QuestionOption } from '../types';

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
  onError,
}: {
  issueKey: string;
  questions: AskedQuestion[];
  obviated: boolean;
  onAnswered: () => void;
  onError: (message: string) => void;
}) {
  if (questions.length === 0) return null;

  return (
    <Card as="section" className={obviated ? undefined : 'hatch-waiting'}>
      <h2 className="hatch-section-title">
        {obviated ? `Asked (${questions.length})` : `Waiting on you (${questions.length})`}
      </h2>
      <p className="text-muted">
        {obviated
          ? `${issueKey} shipped with these open. An answer is welcome, not needed.`
          : `${issueKey} will not be picked up again until these are answered.`}
      </p>

      <ul className="hatch-question-list">
        {questions.map((question) => (
          <Asked key={question.id} issueKey={issueKey} question={question} onAnswered={onAnswered} onError={onError} />
        ))}
      </ul>
    </Card>
  );
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
function Asked({
  issueKey,
  question,
  onAnswered,
  onError,
}: {
  issueKey: string;
  question: AskedQuestion;
  onAnswered: () => void;
  onError: (message: string) => void;
}) {
  const [body, setBody] = useState('');
  const [saving, setSaving] = useState(false);

  // Derived rather than held beside `body`: the highlight is "the box says
  // exactly this option", and a second piece of state saying the same thing is
  // a second thing that can come to disagree with the first.
  const chosen = question.options?.find((o) => o.label === body)?.label ?? null;

  async function submit() {
    setSaving(true);
    try {
      await addComment(issueKey, { body, kind: 'answer', answersId: question.id });
      setBody('');
      onAnswered();
    } catch (err) {
      onError(message(err));
    } finally {
      setSaving(false);
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
    </li>
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
