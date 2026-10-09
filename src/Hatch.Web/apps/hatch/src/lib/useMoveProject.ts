/* What happens between pressing *Move...* on an issue and the PATCH landing.

   useCloseSubtree is handed an already-computed offer - the board or page
   walks board.issues itself before the hook ever sees it. This hook has no
   such offer waiting for it: a press of Move... hands it only the issue, so
   open() does its own fetch to build one - the subtree (GET ?ancestorKey=,
   the server's own Rollup.DescendantIdsAsync) and then, per descendant, its
   pullRequestUrl, which IssueCard does not carry. */

import { useCallback, useMemo, useState } from 'react';
import { getIssue, patchIssue, searchIssues } from '../api/client';
import { message } from './errors';
import { moveSummary } from './moveProject';
import type { MoveCandidate, MoveSummary } from './moveProject';
import type { Issue } from '../types';

export interface MoveOffer {
  issue: MoveCandidate & { projectId: number; parentKey: string | null };
  descendants: MoveCandidate[];
  projectId: number | null;
  moveDescendants: boolean;
}

export interface MoveProject {
  offer: MoveOffer | null;
  loading: boolean;
  busy: boolean;
  error: string | null;
  summary: MoveSummary | null;
  open: (issue: Issue) => void;
  setProjectId: (id: number) => void;
  setMoveDescendants: (value: boolean) => void;
  close: () => void;
  confirm: () => void;
}

export function useMoveProject(onMoved: (moved: Issue) => void): MoveProject {
  const [offer, setOffer] = useState<MoveOffer | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const open = useCallback((issue: Issue) => {
    setError(null);
    setOffer({
      issue: {
        key: issue.key,
        title: issue.title,
        claim: issue.claim,
        pullRequestUrl: issue.pullRequestUrl,
        projectId: issue.projectId,
        parentKey: issue.parentKey,
      },
      descendants: [],
      projectId: null,
      moveDescendants: true,
    });
    setLoading(true);

    void (async () => {
      try {
        const cards = await searchIssues({ ancestorKey: issue.key });
        const full = await Promise.all(cards.map((card) => getIssue(card.key)));
        const descendants: MoveCandidate[] = full.map((d) => ({
          key: d.key,
          title: d.title,
          claim: d.claim,
          pullRequestUrl: d.pullRequestUrl,
        }));
        setOffer((current) => (current ? { ...current, descendants } : current));
      } catch (err) {
        setError(message(err));
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const setProjectId = useCallback((id: number) => {
    setOffer((current) => (current ? { ...current, projectId: id } : current));
  }, []);

  const setMoveDescendants = useCallback((value: boolean) => {
    setOffer((current) => (current ? { ...current, moveDescendants: value } : current));
  }, []);

  const close = useCallback(() => {
    setOffer(null);
    setBusy(false);
    setError(null);
  }, []);

  const confirm = useCallback(() => {
    if (!offer || !offer.projectId || busy) return;

    setBusy(true);
    setError(null);

    void (async () => {
      try {
        const updated = await patchIssue(offer.issue.key, {
          projectId: offer.projectId,
          moveDescendants: offer.moveDescendants,
        });
        close();
        onMoved(updated);
      } catch (err) {
        setError(message(err));
      } finally {
        setBusy(false);
      }
    })();
  }, [offer, busy, close, onMoved]);

  const summary = useMemo(
    () => (offer ? moveSummary(offer.issue, offer.descendants, offer.moveDescendants) : null),
    [offer],
  );

  return { offer, loading, busy, error, summary, open, setProjectId, setMoveDescendants, close, confirm };
}
