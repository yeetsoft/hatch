import { useId } from 'react';
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
import { isPlainClick, type ClickIntent } from '../lib/pointer';
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
  /** The epic's stories are hidden - only meaningful once `stories.length > 0`.
      Default false (expanded). Absent from CardPreview, which draws no lines
      at all and so has nothing to collapse. */
  collapsed?: boolean;
  /** Pressed on the strip, with the card's own key - BoardPage's own state
      decides what `collapsed` is next. Absent wherever `onToggle` would have
      nothing to toggle: CardPreview, and every card with no stories. */
  onToggle?: (key: string) => void;
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
 *
 * An epic carrying stories (HA-1560) is the one card that is not only the
 * <Link>: `useSortable`'s `setNodeRef` moves to a wrapper `<div>`, and the
 * <Link> becomes `setActivatorNodeRef` instead - the drag handle, carrying
 * `attributes`/`listeners` as it always has, but no longer the only thing on
 * the card. The toggle strip and the story lines below it sit beside the
 * <Link> rather than inside it, so a press on either of them is never a press
 * on the activator and never starts a drag. Every card with no stories still
 * renders the bare <Link> it always has (AC6).
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
  collapsed = false,
  onToggle,
}: CardProps & BoardCardProps) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({
    id: card.key,
  });
  // Called whether or not there is a band - a hook cannot sit behind one. Only
  // the listeners and the description are conditional, so with flow-viz off
  // nothing is attached. The key is what Callout.Group's onOpenChange reports
  // as open, which is how BoardPage spotlights the board.
  const tip = useCallout(card.key);
  const nested = depth > 0;
  // Always called, even on a card with no stories, where it goes unused -
  // a hook cannot sit behind the branch below.
  const storiesId = useId();

  const classes = (carrying: boolean) =>
    `hatch-card${carrying ? ' hatch-card--carrying' : ''}${isDragging ? ' dragging' : ''}${waiting ? ' waiting' : ''}${askingClass(card)}${priorityClass(card)}${found ? ' found' : ''}${nested ? ' nested' : ''}${spot ? ` hatch-spot-${spot}` : ''}`;
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    ...(nested ? { '--card-depth': String(depth) } : null),
  };
  const onFaceClick = (e: ClickIntent & { preventDefault: () => void }) => {
    if (!isPlainClick(e) || !onPeek) return;
    e.preventDefault();
    onPeek(card);
  };
  // After the spreads: dnd-kit's attributes carry an aria-describedby of
  // their own, which a plain prop set earlier would lose to.
  const describedBy =
    [attributes['aria-describedby'], band ? tip.describedBy : null].filter(Boolean).join(' ') || undefined;

  if (stories.length === 0) {
    return (
      <Link
        ref={setNodeRef}
        to={`/issues/${card.key}`}
        className={classes(false)}
        // How the board finds the element again: the console's take scrolls to it and focuses it.
        data-issue-key={card.key}
        style={style}
        onClick={onFaceClick}
        {...(band ? tip.focus : null)}
        {...attributes}
        {...listeners}
        aria-describedby={describedBy}
      >
        <CardFace card={card} waiting={waiting} terminal={terminal} project={project} stories={stories} statuses={statuses} />
        <FlowBand band={band} sentence={sentence ?? undefined} tip={band ? tip : undefined} />
      </Link>
    );
  }

  const { total, done } = storyCount(stories, statuses);

  return (
    <div ref={setNodeRef} className={classes(true)} style={style}>
      <Link
        ref={setActivatorNodeRef}
        to={`/issues/${card.key}`}
        className="hatch-card-face"
        data-issue-key={card.key}
        onClick={onFaceClick}
        {...(band ? tip.focus : null)}
        {...attributes}
        {...listeners}
        aria-describedby={describedBy}
      >
        <CardFace
          card={card}
          waiting={waiting}
          terminal={terminal}
          project={project}
          stories={stories}
          statuses={statuses}
          showStrip={false}
        />
        <FlowBand band={band} sentence={sentence ?? undefined} tip={band ? tip : undefined} />
      </Link>
      <button
        type="button"
        className="hatch-card-stories"
        aria-expanded={!collapsed}
        aria-controls={storiesId}
        aria-label={`${total} ${total === 1 ? 'story' : 'stories'}, ${done} done`}
        onClick={() => onToggle?.(card.key)}
      >
        <StoryStrip stories={stories} statuses={statuses} />
      </button>
      {!collapsed && <StoryLines id={storiesId} stories={stories} statuses={statuses} onPeek={onPeek} />}
    </div>
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
  showStrip = true,
}: Required<Omit<CardProps, 'card'>> & { card: IssueCard; showStrip?: boolean }) {
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
      {stories.length > 0 && showStrip && (
        <div className="hatch-card-stories" aria-label={`${total} ${total === 1 ? 'story' : 'stories'}, ${done} done`}>
          <StoryStrip stories={stories} statuses={statuses} />
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

/**
 * The dots-and-count a story strip draws, with no wrapper of its own - the
 * non-interactive `<div>` (CardPreview, and CardFace's own strip) and the
 * toggle `<button>` (BoardCard, once there is something to collapse) each
 * supply their own element and their own `aria-*`, and share only this.
 */
export function StoryStrip({ stories, statuses }: { stories: IssueCard[]; statuses: Status[] }) {
  const { total, done } = storyCount(stories, statuses);
  return (
    <>
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
    </>
  );
}

/**
 * Whether a click on a story line is the app's to take over - the same
 * question `isPlainClick` settles for the card's own `<Link>`, curried over
 * the story and the peek callback so the event handler is a pure function of
 * the two: a test calls `lineClick(story, onPeek)` once and exercises the
 * result against a plain object, with no DOM in the run at all.
 *
 * A modified click, or no `onPeek` to call (the phone, which draws no lines),
 * is left to the browser's own navigation to `/issues/{key}`.
 */
export const lineClick =
  (story: IssueCard, onPeek?: (card: IssueCard) => void) =>
  (event: ClickIntent & { preventDefault: () => void }): void => {
    if (!isPlainClick(event) || !onPeek) return;
    event.preventDefault();
    onPeek(story);
  };

/**
 * One line per story, each its own `<Link>` and a sibling of the card's own -
 * never nested inside it, since an anchor inside an anchor is never valid
 * markup and the epic's own click would otherwise win every time.
 */
export function StoryLines({
  id,
  stories,
  statuses,
  onPeek,
}: {
  id: string;
  stories: IssueCard[];
  statuses: Status[];
  onPeek?: (card: IssueCard) => void;
}) {
  return (
    <ul id={id} className="hatch-card-story-lines">
      {stories.map((story) => {
        const column = statuses.find((s) => s.id === story.statusId);
        return (
          <li key={story.key} style={statusVars(column?.color)}>
            <Link
              className="hatch-card-story-line"
              to={`/issues/${story.key}`}
              title={`${story.key} - ${column?.name ?? ''}`}
              onClick={lineClick(story, onPeek)}
            >
              <span className="hatch-card-story-icon" />
              <span className="hatch-card-story-title">{truncate(story.title)}</span>
              <span className="hatch-card-story-column">{column?.name ?? ''}</span>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}
