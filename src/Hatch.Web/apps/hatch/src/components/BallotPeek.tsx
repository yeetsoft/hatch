import { useEffect, useMemo, useState } from 'react';
import { getAssignees, getIssue, getStatuses } from '../api/client';
import { IssuePeek } from './IssuePeek';
import { message } from '../lib/errors';
import { cardFromIssue } from '../lib/peekCard';
import type { AssigneeDirectory, IssueCard, Project, Status } from '../types';

/** The issue the Ballot is previewing, and how many questions it was drawn with. */
export interface BallotPeekTarget {
  key: string;
  openQuestions: number;
}

/** What was read for one key. `key` is the guard: an answer is drawn only while
    it is the key still asked for. */
interface Read {
  key: string;
  card: IssueCard;
}

/**
 * The board's peek, opened from a key alone.
 *
 * The Ballot has no cards, so this reads the issue, makes the card the peek
 * wants (`cardFromIssue`) and hands it over - the one dialog, so the board's
 * and this one cannot drift apart. It is given no way to move, remand or
 * delete: those ride on the board's own move path, with its WIP refusal, its
 * contribution confirmation and its Undo, none of which exists here.
 *
 * The columns and the assignee directory are read the first time something is
 * previewed and kept, so only the first press pays for them. The card is a
 * snapshot: answering the last question does not make the dialog follow the
 * Ballot behind it, which reloads and may drop the issue's node.
 */
export function BallotPeek({
  target,
  projects,
  onAnswered,
  onClose,
  onError,
}: {
  target: BallotPeekTarget | null;
  projects: Project[];
  /** A question was answered, or a level changed: the Ballot reloads. */
  onAnswered: () => void;
  onClose: () => void;
  /** The sentence for an issue that could not be read, or null once it is
      cleared by the next press. */
  onError: (error: string | null) => void;
}) {
  const [read, setRead] = useState<Read | null>(null);
  const [statuses, setStatuses] = useState<Status[] | null>(null);
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);
  const [directoryAsked, setDirectoryAsked] = useState(false);

  const key = target?.key ?? null;
  const openQuestions = target?.openQuestions ?? 0;

  useEffect(() => {
    if (!key) {
      setRead(null);
      return;
    }
    let current = true;
    onError(null);

    getIssue(key)
      .then((issue) => {
        if (current) setRead({ key, card: cardFromIssue(issue, openQuestions) });
      })
      .catch((err: unknown) => {
        if (!current) return;
        onError(message(err));
        onClose();
      });

    return () => {
      current = false;
    };
    // `openQuestions` is the snapshot taken at the press, and a Ballot reload
    // changing it must not read the issue again under an open dialog.
  }, [key]);

  /* The first press only. A failure is kept as null, as the board keeps its
     own: the dialog still opens, it just offers no level changes. */
  useEffect(() => {
    if (!key || directoryAsked) return;
    setDirectoryAsked(true);
    getStatuses().then(setStatuses).catch(() => setStatuses([]));
    getAssignees().then(setDirectory).catch(() => setDirectory(null));
  }, [key, directoryAsked]);

  const projectsByKey = useMemo(() => new Map(projects.map((p) => [p.key, p])), [projects]);

  // Only a card read for the key still on screen is drawn, and only once the
  // columns are in: until then the dialog is not open yet.
  const card = read && read.key === key && statuses ? read.card : null;

  return (
    <IssuePeek
      card={card}
      status={card ? statuses?.find((s) => s.id === card.statusId) : undefined}
      statuses={statuses ?? []}
      directory={directory}
      project={card ? (projectsByKey.get(card.projectKey) ?? null) : null}
      projectsByKey={projectsByKey}
      onExpedited={onAnswered}
      onClose={onClose}
    />
  );
}
