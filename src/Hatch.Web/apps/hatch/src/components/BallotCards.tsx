import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@hatch/ui';
import { addComment, setQuestionLevel } from '../api/client';
import { QuestionLevelControl, QuestionOptions } from './OpenQuestions';
import { questionEmptyWords } from '../lib/attention';
import { cardQueue, holdsWords, optionsRecommendedFirst, rootOf, thenWords } from '../lib/ballotCards';
import { message } from '../lib/errors';
import { renderMarkdown } from '../lib/markdown';
import { canRaise, fromQuestion, LEVEL_WORDS } from '../lib/questions';
import type { BallotNode, BallotQuestion, QuestionLevel } from '../types';

/**
 * The ballot one question at a time: the one holding the most work first.
 *
 * Skip is page state only. A skipped question goes to the back of the queue and
 * comes round again after the last; nothing is written, so a reload starts the
 * queue over in the server's order.
 */
export function BallotCards({
  questions,
  roots,
  onAnswered,
  onClose,
  onList,
}: {
  questions: BallotQuestion[];
  roots: BallotNode[];
  /** An answer or a level landed: the page reads again, and the card moves on. */
  onAnswered: () => void;
  onClose: () => void;
  /** The switch to the list by issue. */
  onList: () => void;
}) {
  const [skipped, setSkipped] = useState<number[]>([]);
  const queue = cardQueue(questions, skipped);

  if (queue.length === 0) {
    return (
      <div className="hatch-ballot-card hatch-ballot-clear">
        <p className="hatch-attention-empty">{questionEmptyWords()}</p>
        <Button onClick={onList}>Show the list by issue</Button>
      </div>
    );
  }

  const current = queue[0];
  const id = current.question.id;
  const rest = queue.slice(1);
  const index = questions.findIndex((q) => q.question.id === id);

  return (
    <BallotCard
      key={id}
      item={current}
      root={rootOf(roots, current.question.issueKey)}
      position={`${index + 1} of ${questions.length}, most blocking first`}
      open={rest.filter((q) => !q.question.lookBack).length}
      lookBack={rest.filter((q) => q.question.lookBack).length}
      canSkip={rest.length > 0}
      onSkip={() => setSkipped([...skipped.filter((i) => i !== id), id])}
      onAnswered={onAnswered}
      onClose={onClose}
    />
  );
}

function BallotCard({
  item,
  root,
  position,
  open,
  lookBack,
  canSkip,
  onSkip,
  onAnswered,
  onClose,
}: {
  item: BallotQuestion;
  root: BallotNode | null;
  position: string;
  open: number;
  lookBack: number;
  canSkip: boolean;
  onSkip: () => void;
  onAnswered: () => void;
  onClose: () => void;
}) {
  const { question } = item;
  const asked = fromQuestion(question);
  const [body, setBody] = useState('');
  const [other, setOther] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [leveling, setLeveling] = useState(false);
  const [levelError, setLevelError] = useState<string | null>(null);

  async function send(text: string) {
    setSaving(true);
    setError(null);
    try {
      await addComment(question.issueKey, { body: text, kind: 'answer', answersId: question.id });
      onAnswered();
    } catch (err) {
      setError(message(err));
    } finally {
      setSaving(false);
    }
  }

  async function pickLevel(level: QuestionLevel) {
    setLeveling(true);
    setLevelError(null);
    try {
      await setQuestionLevel(question.issueKey, question.id, level);
      onAnswered();
    } catch (err) {
      setLevelError(message(err));
    } finally {
      setLeveling(false);
    }
  }

  return (
    <section className="hatch-ballot-card" aria-label="Question">
      <header className="hatch-ballot-card-bar">
        <strong>Ballot</strong>
        <span className="text-muted hatch-ballot-card-position">{position}</span>
        <Button onClick={onClose}>
          Close
        </Button>
      </header>

      <div className="hatch-ballot-card-chain">
        {root && root.key !== question.issueKey && <span className="hatch-ballot-card-root">{root.title}</span>}
        <Link to={`/issues/${question.issueKey}`} className="hatch-ballot-card-issue">
          <span className="hatch-ballot-key">{question.issueKey}</span> {question.issueTitle}
        </Link>
      </div>

      <div
        className="hatch-markdown hatch-question-body"
        dangerouslySetInnerHTML={{ __html: renderMarkdown(question.body) }}
      />

      <div className="hatch-ballot-card-pills">
        <span className="hatch-ballot-card-pill">{holdsWords(item.holds)}</span>
        <span className="hatch-ballot-card-pill">{LEVEL_WORDS[question.level]}</span>
      </div>

      {question.options && (
        <QuestionOptions
          options={optionsRecommendedFirst(question.options)}
          onChoose={(option) => {
            if (!saving) void send(option.label);
          }}
        />
      )}

      {other ? (
        <div className="hatch-answer-box">
          <input
            type="text"
            value={body}
            aria-label="Your answer"
            placeholder="The decision, in a sentence."
            onChange={(e) => setBody(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.nativeEvent.isComposing && body.trim() && !saving) void send(body);
            }}
          />
          <Button variant="primary" loading={saving} disabled={!body.trim()} onClick={() => void send(body)}>
            Answer
          </Button>
        </div>
      ) : (
        <Button className="hatch-ballot-card-other" onClick={() => setOther(true)}>
          Say something else
        </Button>
      )}
      {error && <p className="text-danger">{error}</p>}

      <QuestionLevelControl
        level={question.level}
        canRaise={canRaise(asked)}
        busy={leveling}
        error={levelError}
        onPick={(level) => void pickLevel(level)}
      />

      <footer className="hatch-ballot-card-foot">
        <span className="text-muted">{thenWords(open, lookBack)}</span>
        <Button disabled={!canSkip} onClick={onSkip}>
          Skip
        </Button>
      </footer>
    </section>
  );
}
