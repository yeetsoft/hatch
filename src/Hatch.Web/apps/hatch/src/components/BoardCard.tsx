import { Link } from 'react-router-dom';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ProjectMark, markVars, useCallout } from '@hatch/ui';
import { ClaimBadge } from './ClaimBadge';
import { FlowBand } from './FlowBand';
import { MomentChip } from './MomentChip';
import { TypeGlyph } from './TypeGlyph';
import { statusVars } from '../lib/color';
import { projectLogoUrl } from '../lib/projectLogo';
import { isPlainClick } from '../lib/pointer';
import type { FlowBandKind } from '../lib/queue';
import type { Spot } from '../lib/spotlight';
import { countWords, storyCount } from '../lib/storiesOnEpics';
import { truncate } from '../lib/text';
import type { IssueCard, Project, Status } from '../types';

export interface CardProps {
  card: IssueCard;
  /** Its ready date has not arrived. Shown only when a column's fold is open. */
  waiting?: boolean;
  /** In a column that means the work shipped, where a due date has nothing left to warn about. */
  terminal?: boolean;
  /** The card's own project, resolved by the board - null draws no mark, the
      same key the board always drew before this had anything to add. */
  project?: Project | null;
  /** The epic's own stories, rank order - absent or empty for every other card. */
  stories?: IssueCard[];
  /** Every column, for a story's own dot colour and title - absent only where `stories` is too. */
  statuses?: Status[];
}

interface BoardCardProps {
  /** The console found this one: draw the ring. */
  found?: boolean;
  /** Absent at phone width (BoardPage's `PhoneColumn`): a tap falls through to
      the `<Link>`'s own navigation, which is AC2's "goes to the issue page" -
      the peek is a desk affordance, a modal over a board still visible behind
      it, and phone width shows no board behind it to peek over. */
  onPeek?: (card: IssueCard) => void;
  /** How many ancestors in this card's lineage group are drawn above it in the
      column - 0 at the top of its own stack. See lib/lineage.ts. Absent from
      CardPreview on purpose: the preview follows the cursor, not a place in a
      column, and takes no indent. */
  depth?: number;
  /** Where this card sits in the work queue, or null/absent for none - see
      lib/queue.ts's `flowBand`. Absent from CardProps on purpose: the preview
      follows the cursor across columns its row does not describe. */
  band?: { kind: FlowBandKind; words: string } | null;
  /** What the callout over the band says - `flowSentence`, null/absent for none. */
  sentence?: string | null;
  /** HA-973's paint for HA-972's spotlight - absent from CardPreview on
      purpose: the card under the cursor during a drag never wears one. */
  spot?: Spot;
}

/** Owed an answer, and drawn as such wherever the card is drawn. */
const askingClass = (card: IssueCard) => (card.openQuestions > 0 ? ' asking' : '');

/** Somebody said this one first, or emergency-first. Wherever the card is
    drawn, including the preview under the cursor - a card being moved is
    exactly the card where knowing which one goes first matters. */
const priorityClass = (card: IssueCard) => (card.priority === 'normal' ? '' : ` ${card.priority}`);

/** Whether the marks row has anything to draw - the same register the
    assignee chip already used alone, now covering the whole row: a card
    with none of these carries no empty row. */
const hasMarks = (card: IssueCard) =>
  card.priority !== 'normal' ||
  card.express ||
  !!card.claim ||
  card.openQuestions > 0 ||
  card.held ||
  !!card.stalledAt ||
  !!card.assignee;

/**
 * One card. A <Link> as well as a draggable, so middle-click, copy-link and
 * open-in-new-tab all work - an issue key is meant to be passed around, and a
 * div with an onClick would make the one gesture that matters impossible.
 *
 * A plain left click opens the summary instead of navigating (see
 * lib/pointer.ts for where that line is drawn), because the question a board
 * raises is usually "what is this one" rather than "take me to it" - and
 * answering it without leaving the board is the difference between a glance and
 * a round trip.
 *
 * The pointer sensor on the board requires a few pixels of movement before a
 * drag starts, which is what lets the same element be a link, a card and a
 * handle at once.
 */
export function BoardCard({
  card,
  waiting = false,
  terminal = false,
  project = null,
  stories = [],
  statuses = [],
  found = false,
  onPeek,
  depth = 0,
  band = null,
  sentence = null,
  spot,
}: CardProps & BoardCardProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: card.key });
  // Called whether or not there is a band - a hook cannot sit behind one. Only
  // the listeners and the description are conditional, so with flow-viz off
  // nothing is attached. The key is what Callout.Group's onOpenChange reports
  // as open, which is how BoardPage spotlights the board.
  const tip = useCallout(card.key);
  const nested = depth > 0;

  return (
    <Link
      ref={setNodeRef}
      to={`/issues/${card.key}`}
      className={`hatch-card${isDragging ? ' dragging' : ''}${waiting ? ' waiting' : ''}${askingClass(card)}${priorityClass(card)}${found ? ' found' : ''}${nested ? ' nested' : ''}${spot ? ` hatch-spot-${spot}` : ''}`}
      // How the board finds the element again: the console's take scrolls to it and focuses it.
      data-issue-key={card.key}
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
        ...(nested ? { '--card-depth': String(depth) } : null),
      }}
      onClick={(e) => {
        if (!isPlainClick(e) || !onPeek) return;
        e.preventDefault();
        onPeek(card);
      }}
      {...(band ? tip.focus : null)}
      {...attributes}
      {...listeners}
      // After the spreads: dnd-kit's attributes carry an aria-describedby of
      // their own, which a plain prop set earlier would lose to.
      aria-describedby={[attributes['aria-describedby'], band ? tip.describedBy : null].filter(Boolean).join(' ') || undefined}
    >
      <CardFace card={card} waiting={waiting} terminal={terminal} project={project} stories={stories} statuses={statuses} />
      <FlowBand band={band} sentence={sentence ?? undefined} tip={band ? tip : undefined} />
    </Link>
  );
}

/**
 * The card that follows the cursor during a drag.
 *
 * Without one, dnd-kit leaves the original card in place at 40% opacity and
 * nothing moves under the hand - the board looked, correctly, like it had not
 * understood the gesture. This is the same face, drawn in an overlay layer that
 * is positioned by the cursor rather than by the column it came from.
 */
export function CardPreview({ card, waiting = false, terminal = false, project = null, stories = [], statuses = [] }: CardProps) {
  return (
    <div className={`hatch-card hatch-card-preview${askingClass(card)}${priorityClass(card)}`}>
      <CardFace card={card} waiting={waiting} terminal={terminal} project={project} stories={stories} statuses={statuses} />
    </div>
  );
}

/**
 * What is drawn on a card, wherever it is drawn.
 *
 * The title is cut twice over: to a fixed number of characters here, and to a
 * fixed number of lines in CSS. The second is what makes every card the same
 * height; the first is what keeps a pasted paragraph out of the DOM, the
 * tooltip, and the drag preview even though the clamp would have hidden it.
 */
function CardFace({
  card,
  waiting,
  terminal,
  project,
  stories,
  statuses,
}: Required<Omit<CardProps, 'card'>> & { card: IssueCard }) {
  const { total, done } = storyCount(stories, statuses);
  return (
    <>
      <TypeGlyph type={card.type} />
      <div className="hatch-card-head">
        {project && (
          <ProjectMark
            size="sm"
            letters={project.key}
            color={project.color}
            icon={project.icon}
            logoUrl={projectLogoUrl(project)}
            title={project.name}
          />
        )}
        <span
          className={`hatch-card-key${project ? ' hatch-card-key--tinted' : ''}`}
          style={project ? markVars(project.color) : undefined}
        >
          {card.key}
        </span>
      </div>
      {hasMarks(card) && (
        <div className="hatch-card-marks">
          {/* This one first - a fact about which card to read next. */}
          {card.priority !== 'normal' && (
            <span
              className={`hatch-card-expedited${card.priority !== 'expedited' ? ` ${card.priority}` : ''}`}
              title={
                card.priority === 'emergency'
                  ? 'Emergency - the highest priority, first above everything expedited'
                  : card.priority === 'expedited'
                    ? 'Expedited - this one goes first'
                    : card.priority === 'low'
                      ? 'Low - reached after everything normal, and only while the session window is on pace'
                      : card.priority === 'economy'
                        ? 'Economy - worked only with usage that would otherwise go spare'
                        : 'Paused - a person set this aside; the loop leaves it where it stands'
              }
            >
              {card.priority === 'emergency'
                ? '🚨'
                : card.priority === 'expedited'
                  ? '↑'
                  : card.priority === 'low'
                    ? '↓'
                    : card.priority === 'economy'
                      ? '🌙'
                      : '⏸'}
            </span>
          )}
          {/* Express, beside expedite and drawn the same way - its own glyph and
              no stripe, because the stripe on the card is expedite's alone. A
              card that is both carries both marks. */}
          {card.express && (
            <span
              className="hatch-card-express"
              title="Express - carried past a column marked Express skips, with no session"
            >
              »
            </span>
          )}
          {card.preApproval === 'preapproved' && (
            <span
              className="hatch-card-preapproved"
              title="Pre-approved - the approve press given in advance: carried into the merge column, or closed, with no session"
            >
              ✓
            </span>
          )}
          {/* Something is holding this one right now. On the face rather than on
              the page, so the drag preview carries it too - a card being moved is
              exactly the card where knowing a runner is mid-increment on it
              matters. */}
          <ClaimBadge claim={card.claim} />
          {/* Drawn on the card and not only on the issue page, because a question
              nobody can see is a question nobody answers - and this card is the
              reason the column below it has stopped moving. */}
          {card.openQuestions > 0 && (
            <span
              className="hatch-card-asking"
              title={`${card.openQuestions} unanswered question${card.openQuestions === 1 ? '' : 's'}`}
            >
              ?{card.openQuestions > 1 && <span className="hatch-card-asking-count">{card.openQuestions}</span>}
            </span>
          )}
          {/* Its own badge, distinct from the asking one above - after HA-341 a
              held or stalled issue is no longer a question, and a reader who
              sees the asking badge should still be able to trust that somebody
              is actually being asked something. Held takes precedence, the same
              order markState resolves in, so a card never wears both. */}
          {card.held && (
            <span className="hatch-card-held" title={`Held${card.stalledWhy ? ` - ${card.stalledWhy}` : ''}`}>
              🔒
            </span>
          )}
          {!card.held && card.stalledAt && (
            <span className="hatch-card-stalled" title={`Stalled${card.stalledWhy ? ` - ${card.stalledWhy}` : ''}`}>
              ⏳
            </span>
          )}
          {/* Nothing at all where there is no assignee - not an empty chip and
              not a dash. Most of the board owns nothing, and a placeholder on
              every card would be noise the eye has to skip past to find the
              three that do. */}
          {card.assignee && (
            <span
              className="hatch-card-assignee"
              title={
                card.assignee.kind === 'person'
                  ? `Assigned to ${card.assignee.name} - an unattended pass leaves it alone`
                  : `Assigned to the API key ${card.assignee.name}`
              }
            >
              {card.assignee.name}
            </span>
          )}
        </div>
      )}
      <div className="hatch-card-title" title={card.title}>
        {truncate(card.title)}
      </div>
      {card.parentKey && <div className="hatch-card-parent">↳ {card.parentKey}</div>}
      {stories.length > 0 && (
        <div className="hatch-card-stories" aria-label={`${total} ${total === 1 ? 'story' : 'stories'}, ${done} done`}>
          {stories.map((story) => {
            const column = statuses.find((s) => s.id === story.statusId);
            return (
              <span
                key={story.key}
                className="hatch-card-story-dot"
                style={statusVars(column?.color)}
                title={`${story.key} - ${column?.name ?? ''}`}
              />
            );
          })}
          <span className="hatch-card-stories-words">{countWords(total, done)}</span>
        </div>
      )}
      {(card.dueAt || waiting) && (
        <div className="hatch-card-dates">
          {/* Only a waiting card shows its ready date. Once it has passed it has
              nothing left to say, and every card on the board would carry one. */}
          {waiting && <MomentChip kind="ready" value={card.readyAt} />}
          <MomentChip kind="due" value={card.dueAt} muted={terminal} />
        </div>
      )}
    </>
  );
}
