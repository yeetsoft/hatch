import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Modal, ProjectMark } from '@hatch/ui';
import { getIssue, patchIssue, setPriority, setExpress } from '../api/client';
import { DescriptionEditor } from './DescriptionEditor';
import { PriorityControl } from './PriorityControl';
import { ExpressControl } from './ExpressControl';
import { MomentChip } from './MomentChip';
import { PullRequestLink } from './PullRequestLink';
import { StatusPicker } from './StatusPicker';
import { TypeBadge } from './TypeBadge';
import { appHref } from '../lib/basename';
import { askToDelete } from '../lib/deletion';
import { isSettled } from '../lib/columns';
import { message } from '../lib/errors';
import { projectLogoUrl } from '../lib/projectLogo';
import { useStandalone } from '../lib/viewport';
import type { AssigneeDirectory, IssueCard, Project, Status } from '../types';

/** What the peek had to ask for, and the card it asked about. `description`
    undefined is "not here yet", which is what the Loading line reads off; so is
    `pullRequestUrl`, where null is the server saying there is none. */
interface Asked {
  key: string | null;
  description?: string;
  pullRequestUrl?: string | null;
  loadError?: string;
  saveError?: string;
  /** What the server last said about the level in effect, the issue's own
      level and where the effective one came from, or undefined while the
      card's own value stands. */
  priority?: 'normal' | 'expedited' | 'emergency' | 'low' | 'economy' | 'paused';
  priorityOwn?: 'normal' | 'expedited' | 'emergency' | 'low' | 'economy' | 'paused';
  priorityFrom?: string | null;
  priorityError?: string;
  priorityBusy?: boolean;
  /** A move is out for this card: the status picker's pill stays disabled
      until it answers. */
  moving?: boolean;
  moveError?: string;
  /** A delete is out for this card: its button is busy until it answers. */
  deleting?: boolean;
  deleteError?: string;
  /** The same, for express. */
  express?: boolean;
  expressError?: string;
  expressing?: boolean;
}

/**
 * What a card says when you click it: everything the board already knew, at
 * once, plus the one field people open a card to read.
 *
 * Key, type, title, column, parent and dates came down with the board
 * (IssueCardDto), so the dialog paints the moment it opens. The description is
 * the exception, and it is fetched here rather than shipped with every card:
 * putting it on the board would slow the request every visit makes to save one
 * on the clicks that actually want a brief. So it arrives a moment later, under
 * its own heading, and can be fixed where it is read - skimming a column and
 * correcting a brief are then the same gesture.
 *
 * The comments and the history are still deliberately not fetched: they are the
 * reason the issue page exists, and so are the title, the type, the column, the
 * parent and the dates. This dialog adds one read, not a second issue page, and
 * it still hands over two ways to go further: the full issue in this tab, or in
 * a new one.
 *
 * That one read also brings the pull request, which shows as the chip the issue
 * page draws, in the row under the key. It rides on the fetch rather than on
 * the card because only this dialog reads it: putting it on IssueCardDto would
 * widen every board payload for a field no card draws. Like the description it
 * is absent until the read answers, so a card that is still loading, or could
 * not be read, shows no chip.
 *
 * It is laid out against the modal's `footer`: the three ways out sit under the
 * body rather than at the end of it, so a card whose brief runs to a page still
 * opens on a dialog you can close. The description gets a box of its own inside
 * that - a paragraph's worth, the same in both views - because the field people
 * open a card to skim should not be the field that fills the dialog.
 *
 * The status pill doubles as the way to move the card: pressed, it opens the
 * same column list the issue page's status bar offers (`StatusPicker`,
 * `StatusSteps`). A move goes through `onMove` rather than growing its own
 * request here, so it is the board's one move path - the same one a drag
 * takes - that earns the drop's Undo chicklet and its offer to close what is
 * under a card sent into a terminal column or onto the shelf. The delete goes
 * up through `onDelete` for the same reason: the board owns closing the peek
 * and repainting, so the dialog only asks, and shows a refusal.
 */
export function IssuePeek({
  card,
  status,
  statuses,
  directory,
  project,
  onExpedited,
  onMove,
  onDelete,
  onClose,
}: {
  card: IssueCard | null;
  /** The column it is sitting in, or undefined if the board has moved underneath. */
  status?: Status;
  /** Every column, deferred ones included - `board.statuses` - for the status
      picker's band. */
  statuses: Status[];
  /** Everybody who could own an issue, and who the caller is - fetched once by
      the board and handed down, so a dialog opening does not cost a request to
      find out whether the reader is a person. Null while it is still loading,
      or where it could not be read. */
  directory: AssigneeDirectory | null;
  /** The card's own project, resolved by the board - fetched once by the
      board and handed down, the same convention as `directory`. Null draws no
      mark, exactly as today. */
  project?: Project | null;
  /** Something on this card changed on the server: the board reloads. */
  onExpedited: () => void;
  /** Move the card to another column, the same path a drag takes. Rejects with
      the server's own sentence on a refusal. */
  onMove: (key: string, statusId: number) => Promise<void>;
  /** Delete the card. The board closes the peek and reloads on success; rejects
      with the server's own sentence on a refusal. */
  onDelete: (key: string) => Promise<void>;
  onClose: () => void;
}) {
  const key = card?.key ?? null;
  /* Everything that had to be asked for, and the card it was asked for.

     One slot rather than three, because which card the answers belong to is
     the thing that has to be right: a response is applied only while its own
     key is still the key on screen, and that guard is the state itself rather
     than a flag beside it, so there is nothing to fall out of step with what
     is drawn. It covers the fetch and the save alike - a save has no cleanup
     function to hang a cancellation flag on, and two stale rules that differ
     is the drift this component is otherwise removing. Without it, a request
     resolving after the operator has clicked a different card paints the old
     issue's brief on the new one. */
  const [asked, setAsked] = useState<Asked>({ key });

  /* Reset while rendering rather than in an effect, which is the same shape as
     the draft reconciliation in DescriptionEditor: the new card's title and
     the old card's description never reach the screen together, because React
     re-renders on this before it commits. An effect would clear it a frame
     late. BoardPage mounts the peek permanently with card={null} while it is
     closed, so nothing here ever unmounts on its own and this is the only
     thing that clears it. */
  if (asked.key !== key) setAsked({ key });

  /** Records an answer, if the card it was asked about is still the one on screen. */
  const apply = useCallback((about: string, answer: Partial<Asked>) => {
    setAsked((prev) => (prev.key === about ? { ...prev, ...answer } : prev));
  }, []);

  useEffect(() => {
    if (!key) return;
    getIssue(key)
      .then((issue) => apply(key, {
          description: issue.description,
          pullRequestUrl: issue.pullRequestUrl,
        }),
      )
      .catch((err: unknown) => apply(key, { loadError: message(err) }));
  }, [key, apply]);

  /* Never throws: DescriptionEditor awaits this and reads the answer, and a
     rejection would leave it reading busy for ever. The board is not reloaded -
     no card draws a description, so there is nothing out there for this to
     change. */
  const save = useCallback(
    async (next: string): Promise<boolean> => {
      if (!key) return false;
      apply(key, { saveError: undefined });
      try {
        const issue = await patchIssue(key, { description: next });
        apply(key, { description: issue.description, pullRequestUrl: issue.pullRequestUrl });
        return true;
      } catch (err) {
        apply(key, { saveError: message(err) });
        return false;
      }
    },
    [key, apply],
  );

  /* This one first, or further, or no longer. Its own endpoint - the write is
     closed to an API key - and it repaints from the answer rather than from
     the press: the server's value is what is drawn, and a refusal leaves the
     control saying what the issue still holds with the sentence beside it.
     The board behind the dialog is reloaded too, because the float moves the
     card. */
  const changePriority = useCallback(
    async (next: 'normal' | 'expedited' | 'emergency' | 'low' | 'economy' | 'paused') => {
      if (!key) return;
      apply(key, { priorityBusy: true, priorityError: undefined });
      try {
        const issue = await setPriority(key, next);
        apply(key, {
          priority: issue.priority,
          priorityOwn: issue.priorityOwn,
          priorityFrom: issue.priorityFrom,
          priorityBusy: false,
        });
        onExpedited();
      } catch (err) {
        apply(key, { priorityBusy: false, priorityError: message(err) });
      }
    },
    [key, apply, onExpedited],
  );

  /* The board's own move path - the optimistic repaint, the request and the
     reload all live in BoardPage's `send`/`commit`, so a refusal has already
     reloaded the board by the time it reaches here. This only tracks the one
     thing that is this dialog's alone: the pill is busy while the request is
     out, and the server's sentence is shown beside the chips on a refusal. */
  const move = useCallback(
    async (statusId: number) => {
      if (!key) return;
      apply(key, { moving: true, moveError: undefined });
      try {
        await onMove(key, statusId);
        apply(key, { moving: false });
      } catch (err) {
        apply(key, { moving: false, moveError: message(err) });
      }
    },
    [key, apply, onMove],
  );

  /* Asks first, with the same native confirm and the same sentence as the
     issue page. On success the board has already closed the peek, so there is
     nothing to apply; `apply` is key-guarded in any case. */
  const remove = useCallback(async () => {
    if (!key) return;
    apply(key, { deleting: true, deleteError: undefined });
    const done = await askToDelete(key, (q) => confirm(q), onDelete);
    if (done.outcome === 'cancelled') apply(key, { deleting: false });
    else if (done.outcome === 'refused') apply(key, { deleting: false, deleteError: done.error });
  }, [key, apply, onDelete]);

  /* Carried past a column marked Express skips, with no session, or no
     longer. Its own endpoint - the write is closed to an API key - and
     otherwise exactly `expedite`. */
  const express = useCallback(
    async (next: boolean) => {
      if (!key) return;
      apply(key, { expressing: true, expressError: undefined });
      try {
        const issue = await setExpress(key, next);
        apply(key, { express: issue.express, expressing: false });
        onExpedited();
      } catch (err) {
        apply(key, { expressing: false, expressError: message(err) });
      }
    },
    [key, apply, onExpedited],
  );

  const standalone = useStandalone();

  // Rendered unconditionally so the dialog's own open/closed handling - focus,
  // escape, the scrim - is the one that runs. Its title needs a card, though,
  // so a closed peek has nothing to say. Below every hook: the rules of hooks
  // are an error here.
  if (!card) return <Modal open={false} onClose={onClose} title="" />;

  // Shipped or shelved: either way nobody is waiting on the date any more, so
  // the due chip stops warning. The same call the issue page makes.
  const stopped = isSettled(status);
  const head = <h4 className="hatch-section-title">Description</h4>;

  return (
    <Modal
      open
      onClose={onClose}
      title={
        <span className="hatch-peek-key">
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
          {card.key}
        </span>
      }
      footer={
        <div className="hatch-form-actions">
          {/* Leftmost, away from the primary "Open the issue". */}
          <Button variant="danger" loading={asked.deleting ?? false} onClick={() => void remove()}>
            Delete
          </Button>
          <Button onClick={onClose}>Close</Button>
          {/* An anchor rather than a <Link>: target="_blank" opens a second
              document, which React Router does not route. appHref is what keeps
              that second document on the right prefix - see lib/basename.ts.
              Also conditional on useStandalone(): in the installed app there is
              no second document to open into, and "Open the issue" beside it is
              the only way out there is. */}
          {!standalone && (
            <Button as="a" href={appHref(`/issues/${card.key}`)} target="_blank" rel="noreferrer">
              New tab ↗
            </Button>
          )}
          <Button as={Link} variant="primary" to={`/issues/${card.key}`} onClick={onClose}>
            Open the issue
          </Button>
        </div>
      }
    >
      <div className="hatch-peek">
        <div className="hatch-peek-meta">
          <TypeBadge type={card.type} />
          {status && (
            <StatusPicker
              key={card.key}
              issueKey={card.key}
              status={status}
              statuses={statuses}
              busy={asked.moving ?? false}
              onMove={(statusId) => void move(statusId)}
            />
          )}
          {card.parentKey && (
            <Link to={`/issues/${card.parentKey}`} onClick={onClose}>
              ↳ {card.parentKey}
            </Link>
          )}
          {/* No onClose, unlike the parent link: this leaves Hatch for a new
              tab, and the card is meant to still be here when they come back. */}
          <PullRequestLink url={asked.pullRequestUrl ?? null} />
          {asked.moveError && <span className="text-danger">{asked.moveError}</span>}
          {asked.deleteError && <span className="text-danger">{asked.deleteError}</span>}
        </div>

        <p className="hatch-peek-title">{card.title}</p>

        {/* The same control the issue page draws, so a card's level changes
            without leaving the board. The card's own value until the server has
            said otherwise: the board is reloaded on a press, but the dialog
            stays open over it, and a control that waited for the refetch to
            catch up would read as not having noticed. */}
        <div className="hatch-peek-priority">
          <PriorityControl
            issueKey={card.key}
            priority={asked.priority ?? card.priority}
            priorityOwn={asked.priorityOwn ?? card.priorityOwn}
            priorityFrom={asked.priorityFrom !== undefined ? asked.priorityFrom : card.priorityFrom}
            directory={directory}
            busy={asked.priorityBusy ?? false}
            onChange={(next) => void changePriority(next)}
          />
          {asked.priorityError && <span className="text-danger">{asked.priorityError}</span>}
          <ExpressControl
            express={asked.express ?? card.express}
            directory={directory}
            busy={asked.expressing ?? false}
            onChange={(next) => void express(next)}
          />
          {asked.expressError && <span className="text-danger">{asked.expressError}</span>}
        </div>

        {(card.readyAt || card.dueAt) && (
          <div className="hatch-card-dates">
            <MomentChip kind="ready" value={card.readyAt} />
            <MomentChip kind="due" value={card.dueAt} muted={stopped} />
          </div>
        )}

        <div className="hatch-peek-description">
          {asked.loadError ? (
            <>
              <div className="hatch-section-head">{head}</div>
              <p className="text-danger">{asked.loadError}</p>
            </>
          ) : asked.description === undefined ? (
            <>
              <div className="hatch-section-head">{head}</div>
              <p className="text-muted">Loading…</p>
            </>
          ) : (
            /* The key is load-bearing: it is what gives a second card a fresh
               draft and a fresh preview flag rather than the first card's. */
            <DescriptionEditor
              key={card.key}
              title={head}
              value={asked.description}
              onSave={save}
              error={asked.saveError ?? null}
              editorClassName="hatch-peek-box"
              previewClassName="hatch-peek-box"
              rows={8}
            />
          )}
        </div>
      </div>
    </Modal>
  );
}
