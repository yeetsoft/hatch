import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Badge, Button, Card, Field, PageHeader, ProjectMark } from '@hatch/ui';
import {
  addComment,
  addDependency,
  clearClaim,
  deleteIssue,
  getAssignees,
  getBoard,
  getComments,
  getDreams,
  getEvents,
  getIssueCapabilities,
  getIssueDreams,
  getIssueServes,
  getIssue,
  getIssuePlan,
  getNextWorkUnder,
  getRunners,
  getWorkLog,
  liftRunnerOmission,
  omitRunner,
  patchIssue,
  patchIssuePlaybook,
  removeDependency,
  setAssignee,
  setPriority,
  setLocalModel,
  setHumanWork,
  setPreApproval,
  setHold,
  setOnePr,
  setIssueRunnerTags,
  setIssueDangerous,
  setWipLimit,
} from '../api/client';
import { AssigneeField } from '../components/AssigneeField';
import { QuestionOptions, Waiting } from '../components/OpenQuestions';
import { OmittedRunners } from '../components/OmittedRunners';
import { RunnerTagsField } from '../components/RunnerTagsField';
import { NoCodeField } from '../components/NoCodeField';
import { OnePrControl, type OnePrSetting } from '../components/OnePrControl';
import { ClaimPanel } from '../components/ClaimPanel';
import { StallPanel } from '../components/StallPanel';
import { IssueScopeNotice } from '../components/IssueScopeNotice';
import { ClearClaimDialog } from '../components/ClearClaimDialog';
import { Choice } from '../components/Choice';
import { CloseSubtreeDialog } from '../components/CloseSubtreeDialog';
import { RemandDialog } from '../components/RemandDialog';
import { Command } from '../components/Command';
import { DescriptionEditor } from '../components/DescriptionEditor';
import { IssueFacts } from '../components/IssueFacts';
import { IssuePicker } from '../components/IssuePicker';
import { IssueSection } from '../components/IssueSection';
import { IssueSubheader } from '../components/IssueSubheader';
import { MarkdownEditor } from '../components/MarkdownEditor';
import { MessageState } from '../components/MessageState';
import { NewestFirstTitle } from '../components/NewestFirstGlyph';
import { StatusMeter } from '../components/StatusMeter';
import { StatusPill } from '../components/StatusPill';
import { IssueStatusBar } from '../components/IssueStatusBar';
import { ContributionBadge } from '../components/ContributionMark';
import { ContributionMoveDialog } from '../components/ContributionMoveDialog';
import { WipOverrideDialog } from '../components/WipOverrideDialog';
import { MomentField } from '../components/MomentField';
import { MoveProjectDialog } from '../components/MoveProjectDialog';
import { ProseClamp } from '../components/ProseClamp';
import { ReleaseNote } from '../components/ReleaseNote';
import { TypeBadge } from '../components/TypeBadge';
import { ChildComposer } from '../components/ChildComposer';
import { TypeControl } from '../components/TypeControl';
import { WorkLog } from '../components/WorkLog';
import { assigneeHint } from '../lib/assignee';
import { onePrSaid } from '../lib/onePr';
import { childTypes } from '../lib/childTypes';
import { parentCandidates, parentEmptyMessage, parentHint } from '../lib/parents';
import { repoSourcedOverrideHint, shortSha } from '../lib/playbookFiles';
import { capabilityStateLabel } from '../lib/capabilities';
import { submitOmission } from '../lib/runnerOmissions';
import { joinTags, runnerTagsDraft, runnerTagsFromLabel, runnerTagsRequest, sameTags } from '../lib/runnerTags';
import { dangerousDraft, dangerousRequest } from '../lib/dangerous';
import { originChain } from '../lib/contribution';
import { confirmMove, moveBlocked } from '../lib/protectedColumns';
import { closeOffer } from '../lib/closeSubtree';
import type { CloseOffer } from '../lib/closeSubtree';
import { isObviated, isSettled, tileFill } from '../lib/columns';
import { dependencyCandidates } from '../lib/dependencies';
import { filedFromCandidates } from '../lib/filedFrom';
import { deleteQuestion } from '../lib/deletion';
import { firstLine } from '../lib/dreams';
import { describe } from '../lib/events';
import { message } from '../lib/errors';
import { agoPhrase } from '../lib/claim';
import { DatedAt } from '../components/DatedAt';
import { heldBy } from '../lib/stall';
import { claimMessages, messageState, watchEvery, watching } from '../lib/messages';
import { mayRefresh } from '../lib/refresh';
import { waitingChild } from '../lib/next';
import { projectLogoUrl } from '../lib/projectLogo';
import { fromComment, openQuestions } from '../lib/questions';
import { writeNoCode } from '../lib/noCode';
import { writeReleaseNote } from '../lib/releaseNote';
import { newestFirst } from '../lib/thread';
import { useCloseSubtree } from '../lib/useCloseSubtree';
import { useRemand } from '../lib/useRemand';
import { remandOffer } from '../lib/remand';
import { useConsoleMoves } from '../lib/useConsole';
import type { MoveResult } from '../lib/useConsole';
import { useMoveProject } from '../lib/useMoveProject';
import { hasMultipleProjects, useProjects } from '../lib/useProjects';
import { useCoarsePointer } from '../lib/viewport';
import { useMoveConfirmation } from '../lib/useMoveConfirmation';
import { useWipOverride } from '../lib/useWipOverride';
import { wipLimitDraft, wipLimitRequest } from '../lib/wip';
import { overridden, wipRefusal } from '../lib/wipOverride';
import { LEGAL_PARENT_TYPES, PLAYBOOK_EFFORTS, PLAYBOOK_MODELS, RUNS_CHILDREN } from '../types';
import type {
  AssigneeDirectory,
  AssigneeRequest,
  Board,
  Capability,
  ChildRollup,
  Comment,
  Dream,
  Issue,
  IssueCard,
  IssueEvent,
  IssueRollup,
  IssueType,
  LocalModel,
  HumanWork,
  Project,
  RunnerTagSource,
  ServedCapability,
  Status,
  Work,
  WorkLog as WorkLogData,
} from '../types';

export function IssuePage() {
  const { key = '' } = useParams();
  const navigate = useNavigate();

  const [issue, setIssue] = useState<Issue | null>(null);
  const [board, setBoard] = useState<Board | null>(null);
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);
  const { projects, byKey: projectsByKey } = useProjects();
  const move = useMoveProject((moved) => void navigate(`/issues/${moved.key}`));
  const [comments, setComments] = useState<Comment[]>([]);
  const [events, setEvents] = useState<IssueEvent[]>([]);
  const [dreams, setDreams] = useState<Dream[]>([]);
  const [remandedDreams, setRemandedDreams] = useState<Dream[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [rollup, setRollup] = useState<IssueRollup | null>(null);
  const [rollupError, setRollupError] = useState<string | null>(null);
  const [next, setNext] = useState<NextAnswer | null>(null);
  const [workLog, setWorkLog] = useState<WorkLogData | null>(null);
  const [workLogError, setWorkLogError] = useState<string | null>(null);
  const [capabilities, setCapabilities] = useState<Capability[] | null>(null);
  const [capabilitiesError, setCapabilitiesError] = useState<string | null>(null);
  const [serves, setServes] = useState<ServedCapability[] | null>(null);
  /* The speed bump in front of taking a ticket off a runner, and whether the
     clear is in flight. Two flags rather than one, because the dialog stays up
     while the request runs and says so on its own button. */
  const [clearingClaim, setClearingClaim] = useState(false);
  const [claimClearing, setClaimClearing] = useState(false);
  /* A priority press is already out: the picker stays open and stops
     answering, `aria-busy`, until the server replies - `PriorityControl`'s own
     flag, the way `StatusPicker`'s pill carries one. */
  const [priorityBusy, setPriorityBusy] = useState(false);
  /* A one-PR press is already out - the same shape as `priorityBusy`. */
  const [onePrBusy, setOnePrBusy] = useState(false);
  /* The release note's own refusal - drawn inside the section, not in the
     page's `error`. */
  const [releaseNoteError, setReleaseNoteError] = useState<string | null>(null);
  /* A pre-approval press is already out - PreApprovalControl's own flag, the
     same shape priorityBusy is. */
  const [preApprovalBusy, setPreApprovalBusy] = useState(false);
  /* A local-model save is already out - the same shape as `preApprovalBusy`. */
  const [localModelBusy, setLocalModelBusy] = useState(false);
  /* A human-work save is already out - the same shape as `preApprovalBusy`. */
  const [humanWorkBusy, setHumanWorkBusy] = useState(false);
  /* A runner-tags save is already out - the same shape as `preApprovalBusy`. */
  const [runnerTagsBusy, setRunnerTagsBusy] = useState(false);
  /* A dangerous-reason save is already out - the same shape as `runnerTagsBusy`. */
  const [dangerousBusy, setDangerousBusy] = useState(false);
  const [noCodeBusy, setNoCodeBusy] = useState(false);
  const [noCodeError, setNoCodeError] = useState<string | null>(null);
  /* The runner whose lift is out, or null - OmittedRunners' own busy flag. */
  const [omissionLifting, setOmissionLifting] = useState<string | null>(null);
  /* Every live runner's name, null while unread - OmittedRunners uses this to
     flag a row naming a runner that is no longer on the board. */
  const [runnerNames, setRunnerNames] = useState<string[] | null>(null);
  /* The live runners' names, offered in the omit form's picker. The same list
     as `runnerNames`: one live-runners read feeds both. */
  const [pickable, setPickable] = useState<string[]>([]);
  /* A hold or a resume is already out - StallPanel's own busy flag, the same
     single-flag shape the pre-approval control uses rather than ClaimPanel's per-field one. */
  const [stallBusy, setStallBusy] = useState(false);
  /* A message to the agent is on its way to the server. */
  const [messageSending, setMessageSending] = useState(false);
  /* Whether the primary-info card shows its form or its read mode - deliberately
     not persisted (HA-334 §6.7: a speed bump that only works on the first page
     load is not a speed bump), so every fresh load opens read-only. */
  const [editingPrimary, setEditingPrimary] = useState(false);

  const load = useCallback(async () => {
    /* The fifth read, sent with the other four and awaited apart from them.
       The four are what the page is made of and share one failure; the rollup
       is one card on it, so its failure is caught here and handed to that card
       instead of blanking an issue somebody came here to read. Handlers are
       attached at the call, so a rejection is never loose. */
    const rolling = getIssuePlan(key).then(
      (loaded) => ({ loaded, failure: null as string | null }),
      (err: unknown) => ({ loaded: null, failure: message(err) }),
    );

    /* And the sixth, on the same terms: what an agent would pick up under this
       issue. Sent unconditionally rather than after the issue arrives and says
       whether it has children - an issue with none has an empty subtree, which
       the server answers with the 204 that means "nothing to do", and waiting
       for the first read to decide would put this line on the page a moment
       after somebody started reading it. */
    const asking = getNextWorkUnder(key).then(
      (work) => ({ work, error: null as string | null }),
      (err: unknown) => ({ work: null, error: message(err) }),
    );

    /* And the seventh, on the same terms as the rollup: what every session run
       against this issue and everything beneath it has cost. Its own failure,
       handed to its own section - a meter that could not be read must not blank
       an issue somebody came here to read. */
    const metering = getWorkLog(key).then(
      (loaded) => ({ loaded, failure: null as string | null }),
      (err: unknown) => ({ loaded: null, failure: message(err) }),
    );

    /* And the eighth: everybody this issue could belong to, and who is signed
       in. Its own failure and no error line of its own - a directory that could
       not be read leaves the field disabled with the assignee still drawn on
       it, which is the honest state of "I know whose this is and cannot offer
       to change it". */
    const whom = getAssignees().then(
      (loaded) => loaded,
      () => null,
    );

    /* And the ninth: an ambition's capabilities, on the same tolerant terms as
       the rollup. Sent for every issue type - a non-ambition's 400 is caught
       here and left for `Capabilities` to never draw, since that section gates
       on `issue.type` before it looks at the error at all. */
    const capping = getIssueCapabilities(key).then(
      (loaded) => ({ loaded, failure: null as string | null }),
      (err: unknown) => ({ loaded: null, failure: message(err) }),
    );

    /* And the tenth: what this issue serves. On the same terms as `whom` - a
       200 for every type, so a failure is dropped rather than given its own
       line; `Serves` draws nothing until this arrives either way. */
    const serving = getIssueServes(key).then(
      (loaded) => loaded,
      () => null,
    );

    try {
      const [loaded, loadedBoard, loadedComments, loadedEvents, loadedDreams, loadedRemanded] = await Promise.all([
        getIssue(key),
        getBoard(),
        getComments(key),
        getEvents(key),
        getIssueDreams(key),
        getDreams(new URLSearchParams({ remandedFromKey: key, limit: '50' })),
      ]);
      setIssue(loaded);
      setBoard(loadedBoard);
      setComments(loadedComments);
      setEvents(loadedEvents);
      setDreams(loadedDreams);
      setRemandedDreams(loadedRemanded.dreams);
      setError(null);
    } catch (err) {
      setError(message(err));
    }

    setDirectory(await whom);

    const settled = await rolling;
    setRollup(settled.loaded);
    setRollupError(settled.failure);

    setNext(await asking);

    const metered = await metering;
    setWorkLog(metered.loaded);
    setWorkLogError(metered.failure);

    const capped = await capping;
    setCapabilities(capped.loaded);
    setCapabilitiesError(capped.failure);

    setServes(await serving);
  }, [key]);

  useEffect(() => {
    void load();
    const onFocus = () => void load();
    window.addEventListener('focus', onFocus);
    return () => window.removeEventListener('focus', onFocus);
  }, [load]);

  /* The one read OmittedRunners' picker and "not a runner" badge draw from.
     They are the same list because a browser person reads every runner
     (RunnersController.GetRunners scoping) - not true of a key, but this page
     is a browser's. Read once on mount, not keyed on `key`: it is not per issue. */
  useEffect(() => {
    getRunners()
      .then((rows) => {
        const names = rows.map((r) => r.name);
        setPickable(names);
        setRunnerNames(names);
      })
      .catch(() => {});
  }, []);

  /* While a claim is held: the issue, every fifteen seconds, so the heartbeat
     `claimHealth` judges is the one the board last saw rather than the one the
     page loaded with - left open, a page would otherwise say "not heard from
     lately" a half lease after it opened, whatever the runner was doing. While
     a message to the agent is also waiting, the comments too, every five
     seconds, so a message reads as read without a reload. This is the one
     thing on this page that polls, for the reason `useRunners` gives for
     polling at all: the state changes on its own and the person is watching
     for the change. It stops when the claim ends (`watchEvery` turns null),
     and while the tab is hidden - and a tab that comes back re-reads at once,
     since its timers were throttled to a stop.

     A failed read is dropped and the next tick tries again: the page already
     says what it knows, and an error line every few seconds would say it
     louder than a message that has not been read yet. */
  const every = watchEvery(comments, issue?.claim ?? null);
  const waiting = watching(comments, issue?.claim ?? null);
  useEffect(() => {
    if (every === null) return;

    const reread = async () => {
      try {
        if (waiting) {
          const [loadedComments, loadedIssue] = await Promise.all([getComments(key), getIssue(key)]);
          setComments(loadedComments);
          setIssue(loadedIssue);
        } else {
          setIssue(await getIssue(key));
        }
      } catch {
        /* The next tick asks again. */
      }
    };
    const refreshIf = () => {
      if (mayRefresh({ visible: document.visibilityState === 'visible', paused: false })) void reread();
    };

    const timer = setInterval(refreshIf, every);
    document.addEventListener('visibilitychange', refreshIf);

    return () => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', refreshIf);
    };
  }, [key, every, waiting]);

  /* Sends what was typed into the Claim panel to the session holding the claim.
     Answers whether it went, so the box is emptied only when it did. */
  const sendMessage = useCallback(
    async (body: string): Promise<boolean> => {
      setMessageSending(true);
      try {
        await addComment(key, { body, kind: 'message' });
        await load();
        return true;
      } catch (err) {
        setError(message(err));
        return false;
      } finally {
        setMessageSending(false);
      }
    },
    [key, load],
  );

  /* Answers whether the patch went through. Every existing caller says
     `void save({ … })` and is unaffected; the one that asks is the status bar,
     because an offer to close a subtree must not follow a move the server
     refused. */
  const save = useCallback(
    async (patch: Parameters<typeof patchIssue>[1]): Promise<boolean> => {
      try {
        await patchIssue(key, patch);
        await load();
        return true;
      } catch (err) {
        setError(message(err));
        return false;
      }
    },
    [key, load],
  );

  /* The two the ordinary patch route does not carry. Its own call because it
     is its own endpoint: setting an override is closed to an API key, and that
     refusal is a property of the route rather than of a rule anybody is asked
     to follow. Otherwise exactly `save` - it re-reads, and a refusal lands in
     `error` above in the server's own words. */
  const savePlaybook = useCallback(
    async (patch: Parameters<typeof patchIssuePlaybook>[1]) => {
      try {
        await patchIssuePlaybook(key, patch);
        await load();
      } catch (err) {
        setError(message(err));
      }
    },
    [key, load],
  );

  /* How many of this epic's stories may run at once. Its own call for
     `savePlaybook`'s reason - it is its own endpoint, and writing one is
     closed to an API key - and otherwise exactly `savePlaybook`. */
  const saveWipLimit = useCallback(
    async (limit: string) => {
      try {
        await setWipLimit(key, limit);
        await load();
      } catch (err) {
        setError(message(err));
      }
    },
    [key, load],
  );

  /* Who owns it. Its own call for the reason `savePlaybook` is - it is its own
     endpoint, and writing one is closed to an API key - and otherwise exactly
     `save`: it re-reads, so the card, the hint and the directory's idea of who
     is signed in all redraw together, and a refusal lands in `error` above in
     the server's own words. */
  const saveAssignee = useCallback(
    async (request: AssigneeRequest) => {
      try {
        await setAssignee(key, request);
        await load();
      } catch (err) {
        setError(message(err));
      }
    },
    [key, load],
  );

  /* Whether this one goes first. Its own call for the reason `saveAssignee` is -
     its own endpoint, closed to an API key - and otherwise exactly `save`: it
     re-reads, so the control, the trail below and the board behind this page
     all redraw from the server's answer rather than from the assumption that
     the press worked. A refusal lands in `error` above in the server's own
     words, and the control goes back to saying what the issue still holds. */
  const savePriority = useCallback(
    async (priority: 'normal' | 'expedited' | 'emergency' | 'low' | 'economy' | 'paused') => {
      setPriorityBusy(true);
      try {
        await setPriority(key, priority);
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setPriorityBusy(false);
      }
    },
    [key, load],
  );

  /* A story's own say over whether its tasks share its branch and one pull
     request. Its own call for the reason `savePriority` is, though unlike it
     the write is open to an API key - and otherwise exactly it: re-read, so the
     control and the trail redraw from the server's answer. */
  const saveOnePr = useCallback(
    async (setting: OnePrSetting) => {
      setOnePrBusy(true);
      try {
        await setOnePr(key, setting);
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setOnePrBusy(false);
      }
    },
    [key, load],
  );

  /* Saves the release note through the sibling lib call, which never throws -
     its refusal goes to `releaseNoteError`, not the page's own `error`, since
     `ReleaseNote` draws it inside its own section. */
  const saveReleaseNote = useCallback(
    async (note: string): Promise<boolean> => {
      setReleaseNoteError(null);
      const refusal = await writeReleaseNote(key, note);
      if (refusal !== null) {
        setReleaseNoteError(refusal);
        return false;
      }
      await load();
      return true;
    },
    [key, load],
  );

  /* Saves the no-code reason through the sibling lib call, which never throws -
     its refusal goes to `noCodeError`, beside the field, not the page's own
     `error`. */
  const saveNoCode = useCallback(
    async (reason: string): Promise<boolean> => {
      setNoCodeError(null);
      setNoCodeBusy(true);
      try {
        const refusal = await writeNoCode(key, reason);
        if (refusal !== null) {
          setNoCodeError(refusal);
          return false;
        }
        await load();
        return true;
      } finally {
        setNoCodeBusy(false);
      }
    },
    [key, load],
  );

  /* The approve press given in advance. Its own call for the reason
     `savePriority` is - its own endpoint, closed to an API key - and
     otherwise exactly it. */
  const savePreApproval = useCallback(
    async (level: 'none' | 'express' | 'preapproved') => {
      setPreApprovalBusy(true);
      try {
        await setPreApproval(key, level);
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setPreApprovalBusy(false);
      }
    },
    [key, load],
  );

  /* How far the issue leans on a local model. Its own call for the reason
     `savePreApproval` is - its own endpoint - and otherwise exactly it. */
  const saveLocalModel = useCallback(
    async (level: LocalModel) => {
      setLocalModelBusy(true);
      try {
        await setLocalModel(key, level);
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setLocalModelBusy(false);
      }
    },
    [key, load],
  );

  /* Who works the issue. Its own call for the reason `savePreApproval` is -
     its own endpoint - and otherwise exactly it. */
  const saveHumanWork = useCallback(
    async (value: HumanWork) => {
      setHumanWorkBusy(true);
      try {
        await setHumanWork(key, value);
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setHumanWorkBusy(false);
      }
    },
    [key, load],
  );

  /* The own-tags save, the same shape savePreApproval is. */
  const saveRunnerTags = useCallback(
    async (tags: string[]) => {
      setRunnerTagsBusy(true);
      try {
        await setIssueRunnerTags(key, { tags });
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setRunnerTagsBusy(false);
      }
    },
    [key, load],
  );

  /* The dangerous-reason save, the same shape saveRunnerTags is. */
  const saveDangerous = useCallback(
    async (reason: string) => {
      setDangerousBusy(true);
      try {
        await setIssueDangerous(key, { reason });
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setDangerousBusy(false);
      }
    },
    [key, load],
  );

  /* Lifting one of this issue's own omitted runners, the same shape saveDangerous is. */
  const liftOmission = useCallback(
    async (runner: string) => {
      setOmissionLifting(runner);
      try {
        await liftRunnerOmission(key, runner);
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setOmissionLifting(null);
      }
    },
    [key, load],
  );

  /* Posting a new omission. Resolves null once added, else the API's refusal
     sentence, so OmittedRunners draws it in place rather than in the page's
     `error`. */
  const addOmission = useCallback(
    async (runner: string, reason: string) => {
      const r = await submitOmission(omitRunner, key, runner, reason);
      if (r.ok) await load();
      return r.ok ? null : r.refusal;
    },
    [key, load],
  );

  /* Resume now or Hold for me - StallPanel's two buttons, both PutHold under
     the one busy flag, the same shape savePreApproval uses. */
  const saveHold = useCallback(
    async (held: boolean) => {
      setStallBusy(true);
      try {
        await setHold(key, held);
        await load();
      } catch (err) {
        setError(message(err));
      } finally {
        setStallBusy(false);
      }
    },
    [key, load],
  );

  /* Taking the ticket back off a runner. Its own call for the reason
     `saveAssignee` is - its own endpoint, closed to an API key - and otherwise
     exactly `save`: it re-reads, so the section, the card and the trail below
     all redraw together, and a refusal lands in `error` above in the server's
     own words.

     The dialog is shut either way, including on a refusal - the page's error
     line is behind the scrim, and an explanation nobody can read until they
     dismiss the thing covering it is not an explanation. */
  const clearTheClaim = useCallback(async () => {
    setClaimClearing(true);
    try {
      await clearClaim(key);
      await load();
    } catch (err) {
      setError(message(err));
    } finally {
      setClaimClearing(false);
      setClearingClaim(false);
    }
  }, [key, load]);

  /* An edge added or taken off. Its own call rather than a field on the patch
     for the reason `savePlaybook` is - it is its own endpoint - and otherwise
     exactly `save`: it re-reads, and a refusal lands in `error` above in the
     server's own words, which are the sentences naming which rule the edge
     broke. */
  const saveDependency = useCallback(
    async (write: () => Promise<unknown>) => {
      try {
        await write();
        await load();
      } catch (err) {
        setError(message(err));
      }
    },
    [load],
  );

  // Handed `load`, so a confirmed cascade re-reads the rollup too and the
  // progress meter counts the children that closed. Declared with the other
  // hooks, above the guards below, because the rules of hooks are an error here.
  const closing = useCloseSubtree(load);

  // The dialog a status-bar press on "to a dream" raises. Reloads on confirm,
  // so the page shows where the dream went; there is no chicklet to raise here.
  const remanding = useRemand(load);

  // The dialog a status-bar press into a full WIP section raises. No
  // `onLeave`: `moveStatus`'s own catch has already re-read the issue before asking.
  const wip = useWipOverride();

  // The second look at a contribution moving into a column that merges pull requests.
  const contributionMove = useMoveConfirmation();

  /* Declared above the guards, for hook order: the console moves this issue
     through the same two, and registers them below. `onOffer` is the console
     asking about what is under the issue its own way, and with it a refusal is
     the console's to show rather than this page's. `asked` is returned where a
     dialog took the move over. */
  const sendStatus = async (
    statusId: number,
    onOffer?: (offer: CloseOffer | null) => void,
  ): Promise<MoveResult> => {
    if (!issue || !board) return 'moved';
    // Computed before the patch, so it is the subtree the operator was looking
    // at when they pressed; asked after it, so a refused move asks nothing.
    const offer = closeOffer(board, key, issue.statusId, statusId);
    try {
      await patchIssue(key, { statusId });
      await load();
      (onOffer ?? closing.ask)(offer);
      return 'moved';
    } catch (err) {
      const refusal = wipRefusal(err);
      if (refusal) {
        await load();
        wip.ask(key, refusal, async () => {
          await patchIssue(key, overridden({ statusId }));
          await load();
        });
        return 'asked';
      }
      if (onOffer) throw err;
      setError(message(err));
      return 'moved';
    }
  };

  const moveStatus = async (statusId: number, onOffer?: (offer: CloseOffer | null) => void): Promise<MoveResult> => {
    if (!issue || !board) return 'moved';
    // `moveBlocked` has already disabled a column that would be refused, so a
    // press that reaches here is one the server will take; a contribution going
    // where its merge happens is asked about first, and Leave it sends nothing.
    const target = board.statuses.find((s) => s.id === statusId);
    const sentence = target ? confirmMove(target, issue) : null;
    if (sentence) {
      contributionMove.ask(key, sentence, async () => {
        await sendStatus(statusId);
      });
      return 'asked';
    }
    return sendStatus(statusId, onOffer);
  };

  useConsoleMoves({
    pageKey: key,
    peekedKey: null,
    projectsByKey,
    send: (_key, statusId, onOffer) => moveStatus(statusId, onOffer),
    reload: load,
  });

  if (error && !issue) return <p className="text-danger">{error}</p>;
  if (!issue || !board) return <p className="text-muted">Loading…</p>;

  const project = projectsByKey.get(issue.projectKey) ?? null;

  // The legal parents: same project, a type this issue may hang under, and
  // never itself - see lib/parents.ts. The server decides too - this only keeps
  // the picker from offering something it will refuse.
  const parents = parentCandidates(board.issues, issue.projectKey, issue.type, issue.key);

  // What may be filed under this issue, read off the same table the server
  // refuses by - see lib/childTypes.ts. Empty on a task, which is what decides
  // both the composer and, with childKeys, whether the card is drawn at all.
  const filings = childTypes(issue.type);

  // Sitting in a column where the work has stopped - shipped or shelved - so
  // the due chip stops warning. The board follows the same rule for the half of
  // it that it can draw, and for the same reason: a date is only late if
  // somebody is still waiting on the thing.
  const stopped = isSettled(board.statuses.find((s) => s.id === issue.statusId));

  // Whether an open question on this issue is a record rather than a request:
  // true once the ticket has shipped, in a terminal column. Narrower than
  // `stopped` on purpose - a deferred ticket is parked, not done, and a
  // question on it still holds the ticket up.
  const obviated = isObviated(board.statuses.find((s) => s.id === issue.statusId));

  async function remove() {
    if (!confirm(deleteQuestion(key))) return;
    try {
      await deleteIssue(key);
      void navigate('/');
    } catch (err) {
      setError(message(err));
    }
  }

  return (
    <div className="hatch-issue-page">
      <PageHeader
        title={<InlineTitle issue={issue} onSave={(title) => void save({ title })} />}
        description={<IssueSubheader issue={issue} project={project} stopped={stopped} />}
        actions={
          <Button variant="danger" onClick={() => void remove()}>
            Delete
          </Button>
        }
      />

      <IssueScopeNotice projectKey={issue.projectKey} />

      {error && <p className="text-danger">{error}</p>}

      {/* Above everything the page lets you change, because it is the one thing
          on it that something else is waiting for. An issue holding an
          unanswered question is not dispatched at all - see WorkController - so
          until this card is empty the ticket does not move. And deliberately
          not an IssueSection: collapsing this would let a reader hide the
          thing that's stopping the ticket. */}
      <Waiting issueKey={key} questions={openQuestions(comments).map((t) => fromComment(t.question))} obviated={obviated} onAnswered={() => void load()} onLeveled={() => void load()} onError={setError} />

      {/* Beside Waiting and for the same reason: something else is acting on
          this ticket right now, and that is worth knowing before pressing
          anything below. Draws nothing on the overwhelming majority of pages.
          And deliberately not an IssueSection: collapsing this would let a
          reader hide that somebody is working on it right now. */}
      <ClaimPanel
        issueKey={key}
        claim={issue.claim}
        messages={claimMessages(comments, issue.claim)}
        sending={messageSending}
        onSend={sendMessage}
        onClear={() => setClearingClaim(true)}
      />

      {/* Beside ClaimPanel, and for the same stated reason: a fact about the
          ticket that something else is or is not about to act on it. Draws
          nothing on the overwhelming majority of pages. */}
      <StallPanel
        issue={issue}
        heldBy={heldBy(events)}
        directory={directory}
        busy={stallBusy}
        onResume={() => void saveHold(false)}
        onHold={() => void saveHold(true)}
      />

      <ClearClaimDialog
        issueKey={key}
        claim={clearingClaim ? issue.claim : null}
        busy={claimClearing}
        onConfirm={() => void clearTheClaim()}
        onClose={() => setClearingClaim(false)}
      />

      {/* Not an IssueSection either, the same reason as Waiting and ClaimPanel
          above it: the column this ticket is in right now is not content to
          be folded away. */}
      <IssueStatusBar
        issue={issue}
        statuses={board.statuses}
        directory={directory}
        canApprove={project?.canApprove ?? false}
        priorityBusy={priorityBusy}
        preApprovalBusy={preApprovalBusy}
        localModelBusy={localModelBusy}
        humanWorkBusy={humanWorkBusy}
        onMove={(statusId) => void moveStatus(statusId)}
        onRemand={(statusId) => remanding.ask(remandOffer(board, key, statusId))}
        blocked={(status) => moveBlocked(status, project, issue)}
        onPriority={(priority) => void savePriority(priority)}
        onPreApproval={(level) => void savePreApproval(level)}
        onLocalModel={(level) => void saveLocalModel(level)}
        onHumanWork={(value) => void saveHumanWork(value)}
      />

      <Card>
        <div className="hatch-section-head">
          <h2 className="hatch-section-title">Details</h2>
          <div className="hatch-section-actions">
            <Button onClick={() => setEditingPrimary(!editingPrimary)}>{editingPrimary ? 'Done' : 'Edit'}</Button>
          </div>
        </div>

        {!editingPrimary && <IssueFacts issue={issue} project={project} stopped={stopped} />}

        {editingPrimary && (
        <div className="hatch-issue-controls">
          <Field label="Type" as="div">
            <TypeControl issueKey={issue.key} type={issue.type} onChange={(type) => void save({ type })} />
          </Field>

          {/* `as="div"` for the reason the fields below it are - `ProjectMark`'s
              own `role="img" aria-label=...` would otherwise be folded into a
              <label>'s accessible name. */}
          <Field label="Project" as="div">
            <span className="hatch-issue-project">
              <ProjectMark
                size="sm"
                letters={project?.key ?? issue.projectKey}
                color={project?.color ?? null}
                icon={project?.icon ?? null}
                logoUrl={project ? projectLogoUrl(project) : null}
                title={project?.name ?? issue.projectKey}
              />
              {project?.name ?? issue.projectKey}
            </span>
            {directory?.me?.kind === 'person' && hasMultipleProjects(projects) && (
              <button type="button" className="hatch-issue-move-project" onClick={() => move.open(issue)}>
                Move…
              </button>
            )}
          </Field>

          {/* `as="div"`: Field wraps its children in a <label> for implicit
              association, and text inside a label joins the control's
              accessible name - with the popup inside it, the whole candidate
              list would be read out as the name of this control. The picker
              carries its own aria-label instead, which is why `label` is
              passed twice. The hint is unaffected: Field already draws it
              outside the label, for the same reason.

              The clear is still the empty string the API reads as "no parent"
              - see IssuePatchRequest - now the `— none —` row rather than an
              empty <option>. `save` is handed over rather than wrapped in
              `void`: the picker awaits it to know when the press is over, and
              `save` catches its own rejection and puts the server's sentence
              in `error` above. */}
          {LEGAL_PARENT_TYPES[issue.type].length > 0 && (
            <Field label="Parent" as="div" hint={parentHint(issue.type)}>
              <IssuePicker
                label="Parent"
                value={issue.parentKey}
                candidates={parents}
                emptyMessage={parentEmptyMessage(issue.projectKey, issue.type)}
                homeProject={issue.projectKey}
                onChange={(parentKey) => save({ parentKey })}
              />
            </Field>
          )}

          {/* `as="div"` for the reason the Parent field is - see there. Set
              here, read in its own section below (FiledSection) - this is
              provenance only, so there is nothing for it to order or block. */}
          <Field
            label="Filed from"
            as="div"
            hint="Provenance only - it holds nothing, orders nothing and blocks nothing."
          >
            <IssuePicker
              label="Filed from"
              value={issue.filedFromKey ?? null}
              candidates={filedFromCandidates(board.issues, issue.key)}
              emptyMessage="Nothing else is on the board yet."
              onChange={(filedFromKey) => save({ filedFromKey })}
            />
          </Field>

          {/* `as="div"` for the reason the Parent field is - see there: a
              <label> wrapping a control with text in it swallows the
              accessible name, and the field carries its own aria-label. */}
          <Field label="Assignee" as="div" hint={assigneeHint(issue.assignee)}>
            <AssigneeField
              assignee={issue.assignee}
              directory={directory}
              onChange={(request) => void saveAssignee(request)}
            />
          </Field>

          <IssueRunnerTagsOwnField tags={issue.runnerTagsOwn} busy={runnerTagsBusy} onSave={(tags) => void saveRunnerTags(tags)} />
          <RunnerTagsInherited from={issue.runnerTagsFrom} />

          <DangerousOwnField reason={issue.dangerousOwn} busy={dangerousBusy} onSave={(reason) => void saveDangerous(reason)} />
          <DangerousInherited from={issue.dangerousFrom} reason={issue.dangerous} />
          <NoCodeField noCode={issue.noCode ?? null} busy={noCodeBusy} error={noCodeError} onSave={saveNoCode} />

          <OmittedRunners
            omissions={issue.omittedRunners}
            runnerNames={runnerNames}
            pickable={pickable}
            liftingRunner={omissionLifting}
            onLift={(runner) => void liftOmission(runner)}
            onAdd={addOmission}
          />

          {/* A story or a bug answers for its tasks, so it gets the control; a task under
              one that shares a pull request says so, since "why is there no PR
              on this" is the question that value answers. Anything else has no
              story or bug to share with and shows nothing. */}
          {(issue.type === 'story' || issue.type === 'bug') && (
            <Field
              label="One pull request"
              as="div"
              hint={`Whether this ${issue.type}'s tasks share its branch and open one pull request, merged once, instead of one each. Locked once any task under it has started: work begun on one branch cannot be moved onto the other.`}
            >
              <OnePrControl issue={issue} busy={onePrBusy} onChange={(setting) => void saveOnePr(setting)} />
            </Field>
          )}
          {issue.type !== 'story' && issue.type !== 'bug' && issue.onePr && (
            <Field label="One pull request" as="div" hint="This task shares its parent's branch; the parent opens the one pull request.">
              <span className="hatch-one-pr-said on">{onePrSaid(issue)}</span>
            </Field>
          )}

          <MomentField
            label="Ready"
            hint="Folded off the board until this day."
            value={issue.readyAt}
            onChange={(readyAt) => void save({ readyAt })}
          />

          <MomentField
            label="Due"
            hint="A past date is fine - nothing here argues with one."
            value={issue.dueAt}
            onChange={(dueAt) => void save({ dueAt })}
          />

          <PullRequestField
            url={issue.pullRequestUrl}
            onSave={(pullRequestUrl) => void save({ pullRequestUrl })}
          />

          {/* The playbook for a transition prices every ticket that makes it.
              These two say this one is different, and they beat every playbook
              that could speak for it - not one transition's worth. `— playbook —`
              is the empty string the route reads as "clear it", and is where
              every issue on the board starts.

              Drawn from the constants the Playbooks page draws from, so what an
              issue may be set to and what a playbook may be set to cannot drift
              apart. A pinned id somebody set through the API is
              offered back rather than swapped for an alias - that is Choice's
              own doing. */}
          {project?.repoSourced && <p className="text-muted">{repoSourcedOverrideHint(project)}</p>}

          <Field label="Model" hint="What an agent dispatched for this issue runs on. A pinned id picks which agent runs it.">
            <Choice
              value={issue.modelOverride ?? ''}
              options={PLAYBOOK_MODELS}
              placeholder="— playbook —"
              onChange={(model) => void savePlaybook({ model })}
            />
          </Field>

          <Field label="Effort" hint="How hard it thinks. Empty means the playbook decides.">
            <Choice
              value={issue.effortOverride ?? ''}
              options={PLAYBOOK_EFFORTS}
              placeholder="— playbook —"
              onChange={(effort) => void savePlaybook({ effort })}
            />
          </Field>

          {/* Epics and reflections only - every other type's page draws nothing
              here. Retyping one to something else leaves the value in place,
              unread. */}
          {RUNS_CHILDREN.includes(issue.type) && (
            <WipLimitField limit={issue.wipLimit} onSetLimit={(limit) => void saveWipLimit(limit)} />
          )}
        </div>
        )}
      </Card>

      {/* Drawn where something may be filed under this issue, and where
          something already is. The second arm is not redundant: a retype does
          not re-check what already hangs below (StageEditAsync), so an epic
          with stories can be turned into a task, and that card must keep its
          list.

          Inside it, the meter and the list are gated on childKeys, which
          arrived with the issue, rather than on the rollup: the card's shape is
          settled on the first paint instead of rearranging itself under the
          reader a moment later. A task's meter, and an epic nobody has put
          anything under, could read 0% or 100% and nothing else - which says
          less than the status band already above it.

          It sits above the dependency chain and the description: an issue's
          children are the work, so "what is this made of" is read before
          "what is it waiting on". */}
      <Capabilities issue={issue} board={board} capabilities={capabilities} error={capabilitiesError} />

      <EpicsInFlight
        issue={issue}
        rollup={rollup}
        error={rollupError}
        capabilities={capabilities}
        statuses={board.statuses}
      />

      {(filings.length > 0 || issue.childKeys.length > 0) && (
        <Progress
          rollup={rollup}
          error={rollupError}
          statuses={board.statuses}
          next={next}
          hasChildren={issue.childKeys.length > 0}
          projectId={issue.projectId}
          project={project}
          parentKey={issue.key}
          parentType={issue.type}
          onFiled={() => void load()}
        />
      )}

      <Serves issue={issue} serves={serves} />

      <OriginSection issue={issue} board={board} />

      <FiledSection issue={issue} board={board} />

      <Dependencies
        issue={issue}
        board={board}
        onAdd={(dependsOnKey) => saveDependency(() => addDependency(key, { dependsOnKey }))}
        onRemove={(dependsOnKey) => saveDependency(() => removeDependency(key, dependsOnKey))}
      />

      <DreamsDrawnOn dreams={dreams} issueKey={issue.key} />

      <DreamsRemandedTo
        dreams={remandedDreams}
        issue={issue}
        deferred={board.statuses.find((s) => s.id === issue.statusId)?.isDeferred ?? false}
      />

      <Description issue={issue} onSave={(description) => save({ description })} />

      <ReleaseNote issue={issue} onSave={saveReleaseNote} error={releaseNoteError} />

      <Comments
        issueKey={key}
        comments={comments}
        claim={issue.claim}
        onAdded={() => void load()}
        onError={setError}
      />

      {/* Between the thread and the trail, and visibly part of neither: a
          comment is somebody talking, an event is something happening, and this
          is the meter. */}
      <WorkLog log={workLog} error={workLogError} statuses={board.statuses} />

      <EventTrail events={events} />

      <CloseSubtreeDialog
        offer={closing.offer}
        busy={closing.busy}
        error={closing.error}
        failures={closing.failures}
        onConfirm={closing.confirm}
        onClose={closing.close}
      />

      <RemandDialog
        offer={remanding.offer}
        busy={remanding.busy}
        error={remanding.error}
        onChoose={remanding.choose}
        onConfirm={remanding.confirm}
        onClose={remanding.close}
      />

      <WipOverrideDialog
        asking={wip.asking}
        busy={wip.busy}
        error={wip.error}
        onConfirm={wip.confirm}
        onClose={wip.close}
      />

      <ContributionMoveDialog
        asking={contributionMove.asking}
        busy={contributionMove.busy}
        error={contributionMove.error}
        onConfirm={contributionMove.confirm}
        onClose={contributionMove.close}
      />

      <MoveProjectDialog move={move} projects={projects} />
    </div>
  );
}

/**
 * Provenance in both directions: the issue this one was filed from, and the
 * issues filed from it. Read-only - the picker that sets `filedFromKey` is
 * the Details edit view above, not here.
 *
 * Omitted entirely when there is neither a source nor anything filed, the
 * same way `Progress` is omitted when there is nothing to show.
 */
function FiledSection({ issue, board }: { issue: Issue; board: Board }) {
  const filedKeys = issue.filedKeys ?? [];

  if (!issue.filedFromKey && filedKeys.length === 0) return null;

  return (
    <>
      <IssueSection id="filed-from" title="Filed from">
        {issue.filedFromKey ? (
          <ul className="hatch-progress-list">
            <DependencyRow issueKey={issue.filedFromKey} board={board} />
          </ul>
        ) : (
          <p className="text-muted">&mdash;</p>
        )}
      </IssueSection>

      <IssueSection id="filed" title="Filed" count={filedKeys.length}>
        {filedKeys.length === 0 ? (
          <p className="text-muted">&mdash;</p>
        ) : (
          <ul className="hatch-progress-list">
            {filedKeys.map((key) => (
              <DependencyRow key={key} issueKey={key} board={board} />
            ))}
          </ul>
        )}
      </IssueSection>
    </>
  );
}

/**
 * What this issue waits on, and what waits on it.
 *
 * Two lists of the same edges read from the two ends. Only the first is
 * editable here, because an edge is owned by the issue that waits: taking one
 * off from the blocker's page would be one issue deciding another issue's
 * order.
 *
 * A dependency gates one thing - the move into the column where the code gets
 * written - and gates it until the issue it names is in a terminal column. So
 * a blocker sitting in review still blocks, which is why each row wears the
 * column it is in: that pill is the answer to "why is this still waiting".
 */
function Dependencies({
  issue,
  board,
  onAdd,
  onRemove,
}: {
  issue: Issue;
  board: Board;
  onAdd: (dependsOnKey: string) => Promise<unknown>;
  onRemove: (dependsOnKey: string) => Promise<unknown>;
}) {
  const candidates = dependencyCandidates(board.issues, issue.key, issue.dependsOnKeys);

  return (
    <>
      <IssueSection id="depends-on" title="Depends on" count={issue.dependsOnKeys.length}>
        <p className="hatch-section-hint text-muted">
          Done before this is implemented. Everything to the left of that still moves.
        </p>

        {issue.dependsOnKeys.length === 0 ? (
          <p className="text-muted">&mdash;</p>
        ) : (
          <ul className="hatch-progress-list">
            {issue.dependsOnKeys.map((key) => (
              <DependencyRow key={key} issueKey={key} board={board} onRemove={onRemove} />
            ))}
          </ul>
        )}

        {/* `as="div"` for the reason the Parent field is - see there. `allowNone`
            off and a placeholder in its place: taking a row here adds an edge
            rather than replacing the one value a field holds, so there is
            nothing for a clear row to clear, and removing is the button on the
            row itself. */}
        <div className="hatch-depends-add">
          <Field label="Add a dependency" as="div">
            <IssuePicker
              label="Add a dependency"
              value={null}
              allowNone={false}
              placeholder="Add a dependency…"
              candidates={candidates}
              emptyMessage="Nothing else on the board can be depended on."
              onChange={onAdd}
            />
          </Field>
        </div>
      </IssueSection>

      <IssueSection id="blocks" title="Blocks" count={issue.dependentKeys.length}>
        <p className="hatch-section-hint text-muted">
          Waiting on this one. Taken off from their own pages &mdash; an edge belongs to the issue that
          waits.
        </p>

        {issue.dependentKeys.length === 0 ? (
          <p className="text-muted">&mdash;</p>
        ) : (
          <ul className="hatch-progress-list">
            {issue.dependentKeys.map((key) => (
              <DependencyRow key={key} issueKey={key} board={board} />
            ))}
          </ul>
        )}
      </IssueSection>
    </>
  );
}

function DreamsDrawnOn({ dreams, issueKey }: { dreams: Dream[]; issueKey: string }) {
  if (dreams.length === 0) return null; // drawn only when there are rows - no empty 'Drew on' section
  return (
    <IssueSection id="drew-on" title="Drew on" count={dreams.length}>
      <ul className="hatch-progress-list">
        {dreams.map((dream) => (
          <li key={dream.id} className="hatch-progress-row">
            <Link to={`/dreams?issue=${issueKey}`}>{firstLine(dream.body)}</Link>
          </li>
        ))}
      </ul>
    </IssueSection>
  );
}

/** The dream this issue's tree was shelved to, when `GET /api/dreams` finds
    one for it under `remandedFromKey`. Drawn whether the issue is still
    deferred or has come back onto a lane - `deferred` says which, and the
    sentence changes with it rather than the section disappearing. Omitted
    entirely when no dream was remanded from this issue. */
function DreamsRemandedTo({ dreams, issue, deferred }: { dreams: Dream[]; issue: Issue; deferred: boolean }) {
  if (dreams.length === 0) return null;

  const now = new Date();

  return (
    <IssueSection id="remanded-to" title="Remanded to a dream" count={dreams.length}>
      <ul className="hatch-progress-list">
        {dreams.map((dream) => {
          const root = dream.remandedFrom[0];
          return (
            <li key={dream.id} className="hatch-progress-row">
              <Link to={`/dreams?remandedFrom=${issue.key}`}>{firstLine(dream.body)}</Link>
              <span className="text-muted" title={new Date(dream.createdAt).toLocaleString()}>
                remanded {agoPhrase(dream.createdAt, now)} by {dream.author}
              </span>
              {root !== issue.key && (
                <span className="text-muted">
                  remanded with <Link to={`/issues/${root}`}>{root}</Link>
                </span>
              )}
              <span className="text-muted">{deferred ? 'It is still in a deferred column.' : 'It is back on the board.'}</span>
            </li>
          );
        })}
      </ul>
    </IssueSection>
  );
}

/**
 * What an epic serves: each capability it was filed to further, with the
 * ambition it belongs to and whether that capability holds.
 *
 * Read-only - writing what an epic serves stays HA-2491's, on the CLI.
 * Omitted entirely off any type but an epic, and off an epic that serves
 * nothing - the same way FiledSection is omitted when there is nothing to show.
 */
export function Serves({ issue, serves }: { issue: Issue; serves: ServedCapability[] | null }) {
  if (issue.type !== 'epic' || !serves?.length) return null;

  return (
    <IssueSection id="serves" title="Serves" count={serves.length}>
      <ul className="hatch-progress-list">
        {serves.map((row) => (
          <li key={row.capabilityId} className="hatch-progress-row">
            <Link to={`/issues/${row.ambitionKey}`} className="hatch-plan-key">
              {row.ambitionKey}
            </Link>
            <span className="text-muted">#{row.position}</span>
            <span className="hatch-progress-title">{row.statement}</span>
            <span className={`hatch-capability-state hatch-capability-state-${row.state.replace(/_/g, '-')}`}>
              {capabilityStateLabel(row.state)}
            </span>
          </li>
        ))}
      </ul>
    </IssueSection>
  );
}

/**
 * The mirrored ancestors of this issue, root first, each with where it came
 * from - read off the parents' own records on the board, so a person reading a
 * task sees the epic it came from. Omitted entirely when no ancestor has a
 * record, as FiledSection is when there is nothing to show; an ancestor the
 * board no longer carries is not listed, since there is no card to read.
 */
function OriginSection({ issue, board }: { issue: Issue; board: Board }) {
  const chain = originChain(issue.parentKey, board.issues);
  if (chain.length === 0) return null;

  return (
    <IssueSection id="origin" title="Origin" count={chain.length}>
      <ul className="hatch-progress-list">
        {chain.map((card) => (
          <li key={card.key} className="hatch-progress-row">
            <Link to={`/issues/${card.key}`} className="hatch-plan-key">
              {card.key}
            </Link>
            <TypeBadge type={card.type} />
            <span className="hatch-progress-title">{card.title}</span>
            {card.contribution && <ContributionBadge contribution={card.contribution} />}
          </li>
        ))}
      </ul>
    </IssueSection>
  );
}

/**
 * One edge, drawn from the board rather than fetched: the key, what it is, its
 * title and the column it is in.
 *
 * An issue the board does not carry is drawn as its key alone rather than
 * dropped. A key with nothing behind it is a thing somebody has to be able to
 * see in order to take it off; a silently missing row is not.
 */
function DependencyRow({
  issueKey,
  board,
  onRemove,
}: {
  issueKey: string;
  board: Board;
  /** Absent on the Blocks list, which is read-only from this page. */
  onRemove?: (dependsOnKey: string) => Promise<unknown>;
}) {
  const [removing, setRemoving] = useState(false);
  const card: IssueCard | undefined = board.issues.find((i) => i.key === issueKey);
  const status = board.statuses.find((s) => s.id === card?.statusId);

  return (
    <li className="hatch-progress-row">
      <Link to={`/issues/${issueKey}`} className="hatch-plan-key">
        {issueKey}
      </Link>
      {card && <TypeBadge type={card.type} />}
      <span className="hatch-progress-title">{card?.title ?? ''}</span>
      {status && <StatusPill status={status} />}
      {onRemove && (
        <Button
          className="hatch-depends-remove"
          loading={removing}
          aria-label={`Stop waiting on ${issueKey}`}
          onClick={() => {
            setRemoving(true);
            void onRemove(issueKey).finally(() => setRemoving(false));
          }}
        >
          Remove
        </Button>
      )}
    </li>
  );
}

/**
 * An ambition's capabilities: what must be true, how anyone would tell, and
 * what already holds.
 *
 * Read-only - adding and editing a capability stays HA-2491's, on the CLI;
 * this section only draws the `hatch capability add` command to start one.
 * Omitted entirely off any type but an ambition.
 */
export function Capabilities({
  issue,
  board,
  capabilities,
  error,
}: {
  issue: Issue;
  board: Board;
  capabilities: Capability[] | null;
  error: string | null;
}) {
  if (issue.type !== 'ambition') return null;

  const now = new Date();

  return (
    <IssueSection id="capabilities" title="Capabilities" count={capabilities ? capabilities.length : undefined}>
      {error && <p className="text-danger">{error}</p>}
      {!capabilities && !error && <p className="text-muted">Loading…</p>}

      {capabilities && capabilities.length === 0 && (
        <>
          <p className="text-muted">This ambition has no capabilities yet.</p>
          <Command command={`hatch capability add ${issue.key} "<statement>" --how "<how to tell>"`} />
        </>
      )}

      {capabilities && capabilities.length > 0 && (
        <ul className="hatch-capability-list">
          {capabilities.map((capability) => (
            <CapabilityRow key={capability.id} capability={capability} board={board} now={now} />
          ))}
        </ul>
      )}
    </IssueSection>
  );
}

/**
 * The epics open under an ambition: which, what column each is in, which of
 * the ambition's capabilities each serves, and how many run at once.
 *
 * Drawn from the rollup Progress already fetched and the capabilities
 * Capabilities already fetched - nothing new is asked for. Open means a column
 * that is neither terminal nor deferred; a status the board does not carry
 * counts as closed. Omitted entirely off any type but an ambition.
 */
export function EpicsInFlight({
  issue,
  rollup,
  error,
  capabilities,
  statuses,
}: {
  issue: Issue;
  rollup: IssueRollup | null;
  error: string | null;
  capabilities: Capability[] | null;
  statuses: Status[];
}) {
  if (issue.type !== 'ambition') return null;

  const open = (rollup?.children ?? []).flatMap((child) => {
    const status = statuses.find((s) => s.id === child.issue.statusId);
    return status && !status.isTerminal && !status.isDeferred ? [{ card: child.issue, status }] : [];
  });
  const runs = issue.wipLimit ?? 1;

  return (
    <IssueSection id="epics-in-flight" title="Epics in flight" count={rollup ? open.length : undefined}>
      {error && <p className="text-danger">{error}</p>}
      {!rollup && !error && <p className="text-muted">Loading…</p>}

      {rollup && open.length === 0 && <p className="text-muted">No epic is open under this ambition.</p>}

      {rollup && open.length > 0 && (
        <>
          <p className="text-muted">
            {open.length} open, runs {runs} at once
          </p>
          <ul className="hatch-progress-list">
            {open.map(({ card, status }) => {
              const served = (capabilities ?? [])
                .filter((c) => c.servedBy.includes(card.key))
                .sort((a, b) => a.position - b.position);

              return (
                <li key={card.key} className="hatch-progress-row">
                  <Link to={`/issues/${card.key}`} className="hatch-plan-key">
                    {card.key}
                  </Link>
                  <span className="hatch-progress-title">{card.title}</span>
                  <span className="hatch-epics-in-flight-meta">
                    <StatusPill status={status} size="sm" />
                    {served.length === 0 ? (
                      <span className="text-muted">&mdash;</span>
                    ) : (
                      served.map((c) => (
                        <span key={c.id} className="hatch-capability-badge" title={c.statement}>
                          #{c.position}
                        </span>
                      ))
                    )}
                  </span>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </IssueSection>
  );
}

/**
 * One capability: its statement and how to tell, its state and, where the
 * state came from a verdict, that verdict's own trail - and the epics serving
 * it.
 */
function CapabilityRow({ capability, board, now }: { capability: Capability; board: Board; now: Date }) {
  const verdict = capability.newestVerdict;

  return (
    <li className="hatch-capability-row">
      <span className="text-muted">#{capability.position}</span>

      <div className="hatch-progress-title">
        <p>{capability.statement}</p>
        <p className="text-muted">{capability.howToTell}</p>
      </div>

      <div>
        <span className={`hatch-capability-state hatch-capability-state-${capability.state.replace(/_/g, '-')}`}>
          {capabilityStateLabel(capability.state)}
        </span>
        {verdict && (
          <p className="text-muted">
            {verdict.lookedAt}, {shortSha(verdict.sha)}, by {verdict.author}, <DatedAt at={verdict.at} now={now} />
          </p>
        )}
      </div>

      <div className="hatch-capability-served-by">
        {capability.servedBy.length === 0 ? (
          <span className="text-muted">&mdash;</span>
        ) : (
          capability.servedBy.map((key) => <ServedByLink key={key} issueKey={key} board={board} />)
        )}
      </div>
    </li>
  );
}

/** One epic serving a capability, drawn from the board rather than fetched -
    the key and its status pill where the board carries it, the bare key
    otherwise, the same fallback DependencyRow uses. */
function ServedByLink({ issueKey, board }: { issueKey: string; board: Board }) {
  const card = board.issues.find((i) => i.key === issueKey);
  const status = board.statuses.find((s) => s.id === card?.statusId);

  if (!card) return <span className="hatch-plan-key">{issueKey}</span>;

  return (
    <>
      <Link to={`/issues/${issueKey}`} className="hatch-plan-key">
        {issueKey}
      </Link>
      {status && <StatusPill status={status} size="sm" />}
    </>
  );
}

/**
 * How far along this issue is, and what it is made of.
 *
 * The card an epic or a story grows the moment something is filed under it: the
 * whole subtree's meter across the top, and a line per direct child beneath.
 *
 * It replaces the list of child keys this card used to be rather than sitting
 * beside it. Two lists of the same children on one page is one list going
 * stale, and the keys are still here - each row starts with one.
 */
function Progress({
  rollup,
  error,
  statuses,
  next,
  hasChildren,
  projectId,
  project,
  parentKey,
  parentType,
  onFiled,
}: {
  rollup: IssueRollup | null;
  error: string | null;
  statuses: Status[];
  next: NextAnswer | null;
  /** Whether anything is filed under this issue, off the issue rather than off
      the rollup, so the card does not change shape when the fifth read lands. */
  hasChildren: boolean;
  projectId: number;
  /** The issue's own project, for ChildComposer's mark - null while the list is
      still loading or failed to. */
  project: Project | null;
  parentKey: string;
  /** The issue's own type: what may be filed here is read from it, and a task
      draws no composer - it takes nothing. */
  parentType: IssueType;
  onFiled: () => void;
}) {
  return (
    <IssueSection id="progress" title="Progress" count={rollup ? rollup.children.length : undefined}>
      {hasChildren && (
        <>
          {/* Said here, where the thing that failed was going to be. The rest of
              the page is already readable without it, so this is not the page's
              error - and it is not nothing, either, which is what a card that
              quietly stayed empty would be. */}
          {error && <p className="text-danger">{error}</p>}
          {!rollup && !error && <p className="text-muted">Loading…</p>}

          {rollup && (
            <>
              <StatusMeter rollup={rollup.rollup} statuses={statuses} size="lg" counts />

              <Next answer={next} rollup={rollup} />

              {/* Rank order, as the server sent them - the order they sit in on
                  the board's column, so the two screens agree about what is
                  next. */}
              <ul className="hatch-progress-list">
                {rollup.children.map((child) => (
                  <ChildRow key={child.issue.key} child={child} statuses={statuses} />
                ))}
              </ul>
            </>
          )}
        </>
      )}

      {/* Last in the card, and not waiting on the rollup: it is drawn from the
          issue, which has already arrived. */}
      {childTypes(parentType).length > 0 && (
        <ChildComposer
          projectId={projectId}
          project={project}
          parentKey={parentKey}
          parentType={parentType}
          onFiled={onFiled}
        />
      )}
    </IssueSection>
  );
}

/** The scoped `work/next`, settled: the issue an agent would take under this
    one, or the sentence saying why the ask itself did not go through. Null
    where neither has arrived. */
interface NextAnswer {
  work: Work | null;
  error: string | null;
}

/**
 * What an agent would pick up under this issue, and the one command that sets
 * it going.
 *
 * The last sentence of the loop this tracker exists for: find the project worth
 * furthering, find the next actionable story under it, and move it forward. The
 * Plan page answers the first; this answers the second, on the page of the epic
 * somebody has already chosen - once, rather than on every card of a screen
 * holding twenty-eight of them.
 *
 * Which issue is next is not decided here and could not be: the rule is the
 * server's (WorkController), and a copy of it in a browser would disagree with
 * the CLI the first time a column was renamed. This draws the answer.
 */
function Next({ answer, rollup }: { answer: NextAnswer | null; rollup: IssueRollup }) {
  // Not back yet. Said with nothing rather than with a second "Loading…" under
  // a meter that has already painted - the card is readable, and this line
  // arriving a moment later is what it is.
  if (answer === null) return null;

  // The ask failed while the rollup did not. Muted rather than red: the meter
  // above is the card's subject and it is fine, and this is one line of it
  // that could not be filled in.
  if (answer.error !== null) {
    return <p className="hatch-next-none text-muted">Could not ask what is next here: {answer.error}</p>;
  }

  if (answer.work !== null) {
    const found = answer.work.issue;
    return (
      <p className="hatch-next">
        <span className="hatch-next-label">Next</span>
        <Link to={`/issues/${found.key}`} className="hatch-plan-key">
          {found.key}
        </Link>
        <span className="hatch-next-title">— {found.title}</span>
        <Command command={`hatch work ${found.key}`} />
      </p>
    );
  }

  // A 204: nothing under here is an agent's to move. That has several causes -
  // it is all shipped, it is all in review, it is all waiting on a date - and
  // only one of them is worth naming, because only one of them is somebody's
  // to fix from this page.
  const waiting = rollup.rollup.waiting;
  const holder = waitingChild(rollup);

  return (
    <p className="hatch-next-none text-muted">
      Nothing under this is an agent&rsquo;s to move.{' '}
      {waiting > 0 && (
        <>
          {waiting} question{waiting === 1 ? ' is' : 's are'} waiting on a person
          {holder && (
            <>
              , on{' '}
              <Link to={`/issues/${holder}`} className="hatch-plan-key">
                {holder}
              </Link>
            </>
          )}
          .
        </>
      )}
    </p>
  );
}

/** One direct child: what it is, and either how far along it is or where it sits. */
function ChildRow({ child, statuses }: { child: ChildRollup; statuses: Status[] }) {
  const status = statuses.find((s) => s.id === child.issue.statusId);
  const waiting = child.rollup.waiting;

  return (
    <li className="hatch-progress-row">
      <Link to={`/issues/${child.issue.key}`} className="hatch-plan-key">
        {child.issue.key}
      </Link>
      <TypeBadge type={child.issue.type} fill={tileFill(statuses, child.issue.statusId)} />
      <span className="hatch-progress-title">{child.issue.title}</span>

      {/* Somebody owes an answer at or below this child, which is why the bar
          above has stopped. Worn by the row rather than printed inside the
          meter's counts, so it is in the same place on a leaf - which has no
          meter to put it in and is exactly the row most likely to be the one
          holding everything up. */}
      {waiting > 0 && (
        <span
          className="hatch-meter-waiting"
          title={`${waiting} unanswered question${waiting === 1 ? '' : 's'} at or below ${child.issue.key}`}
        >
          waiting
        </span>
      )}

      {/* isLeaf decides, not the shape of the numbers: a story with one task
          and a task with none both roll up to a single leaf, and only one of
          them has a bar worth drawing. */}
      {child.isLeaf ? (
        status ? (
          <StatusPill status={status} />
        ) : (
          <span className="text-muted">unknown</span>
        )
      ) : (
        <StatusMeter rollup={child.rollup} statuses={statuses} size="sm" counts waiting={false} />
      )}
    </li>
  );
}

/** The title, editable where it is read. A separate edit screen for one string is a screen too many. */
function InlineTitle({ issue, onSave }: { issue: Issue; onSave: (title: string) => void }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(issue.title);
  const [known, setKnown] = useState(issue.title);

  // Re-syncs when the title changes underneath - a refetch on focus, or
  // somebody else's edit - so the editor does not hand back a stale string on
  // its next save. Adjusted during render rather than in an effect: an effect
  // would paint the old value first, and React documents this shape for
  // exactly this case.
  if (issue.title !== known) {
    setKnown(issue.title);
    setDraft(issue.title);
  }

  if (!editing) {
    return (
      <button type="button" className="hatch-inline-title" onClick={() => setEditing(true)}>
        {issue.title}
      </button>
    );
  }

  function commit() {
    setEditing(false);
    if (draft.trim() && draft !== issue.title) onSave(draft.trim());
  }

  return (
    <input
      className="hatch-inline-title-input"
      value={draft}
      autoFocus
      onChange={(e) => setDraft(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === 'Enter') commit();
        if (e.key === 'Escape') {
          setDraft(issue.title);
          setEditing(false);
        }
      }}
    />
  );
}

/**
 * The description: raw markdown in a textarea, with a preview toggle. Stored
 * and edited raw on purpose - what the database holds is what somebody wrote.
 */
function Description({ issue, onSave }: { issue: Issue; onSave: (description: string) => Promise<boolean> }) {
  const coarse = useCoarsePointer();

  return (
    <IssueSection id="description" title="Description">
      {/* `title={null}`: IssueSection already draws the heading in the
          summary, so DescriptionEditor's own `.hatch-section-head` row holds
          only the Edit/Preview/Save group - it is `justify-content:
          space-between`, so they still sit right. A <button> inside a
          <summary> would toggle the <details> on every press unless the
          handler stopped propagation, which is a trap rather than a feature;
          keeping the actions in the body avoids it rather than working around
          it. */}
      <DescriptionEditor
        title={null}
        value={issue.description}
        onSave={onSave}
        editorClassName="hatch-grows"
        clampTo={coarse ? 3 : null}
        projectId={issue.projectId}
      />
    </IssueSection>
  );
}

function Comments({
  issueKey,
  comments,
  claim,
  onAdded,
  onError,
}: {
  issueKey: string;
  comments: Comment[];
  /** Only to say where a message to the agent stands - a note shows nothing of it. */
  claim: Issue['claim'];
  onAdded: () => void;
  onError: (message: string) => void;
}) {
  const [body, setBody] = useState('');
  const [saving, setSaving] = useState(false);
  const now = new Date();
  const coarse = useCoarsePointer();

  async function submit() {
    setSaving(true);
    try {
      await addComment(issueKey, { body });
      setBody('');
      onAdded();
    } catch (err) {
      onError(message(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <IssueSection id="comments" title={<NewestFirstTitle>Comments</NewestFirstTitle>} count={comments.length}>
      <div className="hatch-comment-box">
        <MarkdownEditor
          value={body}
          onChange={setBody}
          rows={3}
          className="hatch-grows"
          deferred
          ariaLabel="Comment"
          placeholder="Markdown, like everything else."
        />
        <Button variant="primary" loading={saving} disabled={!body.trim()} onClick={() => void submit()}>
          Comment
        </Button>
      </div>

      {comments.length === 0 && <p className="text-muted">Nothing said yet.</p>}

      <ul className="hatch-comments">
        {newestFirst(comments).map((comment) => (
          <li key={comment.id} className={`hatch-comment${comment.kind ? ` hatch-comment-${comment.kind}` : ''}`}>
            <div className="hatch-comment-head">
              <strong>{comment.author}</strong>
              {/* The thread still shows everything, newest first -
                  the card above is for acting, this is for reading back what
                  was decided and when. */}
              {comment.kind === 'question' && <Badge>asked</Badge>}
              {comment.kind === 'answer' && <Badge>answered</Badge>}
              {comment.kind === 'message' && <Badge>to the agent</Badge>}
              <span className="text-muted">{new Date(comment.createdAt).toLocaleString()}</span>
              <MessageState status={messageState(comment, claim, issueKey, now)} />
            </div>
            <ProseClamp
              source={comment.body}
              limit={coarse ? 3 : null}
              className={comment.kind === 'question' ? 'hatch-question-body' : undefined}
            />
            {comment.options && <QuestionOptions options={comment.options} />}
          </li>
        ))}
      </ul>
    </IssueSection>
  );
}

/** The audit trail, collapsed. It is there to be looked up, not to be read. */
/**
 * How many of an epic's stories may run at once. Committed on blur, the way
 * StatusesPage's LimitField is - blur-commit, re-sync when the value changes
 * underneath - but bare rather than WipSliceSetting-shaped: an epic's limit
 * is not a slice of the board, so it is not that component and not its
 * helpers, only its pattern.
 */
function WipLimitField({ limit, onSetLimit }: { limit: number | null; onSetLimit: (limit: string) => void }) {
  const [draft, setDraft] = useState(wipLimitDraft(limit));
  const [known, setKnown] = useState(limit);

  // Re-syncs when the issue changes underneath - see StatusesPage's ColorCell.
  if (limit !== known) {
    setKnown(limit);
    setDraft(wipLimitDraft(limit));
  }

  return (
    <Field label="Stories at once" hint="Blank means no limit.">
      <input
        inputMode="numeric"
        value={draft}
                onChange={(e) => setDraft(e.target.value)}
        onBlur={() => {
          const next = wipLimitRequest(draft);
          if (next !== wipLimitDraft(limit)) onSetLimit(next);
        }}
      />
    </Field>
  );
}

/**
 * Where this issue is reviewed, editable right where it is read - blur-commit,
 * re-sync when it changes underneath, the way WipLimitField is. For the
 * changeset built out of band, with nowhere else in the web UI to link it
 * back from. Emptying it clears the field, the same as the API reads it -
 * see IssuePatchRequest. A bad address comes back as the server's own
 * sentence, in the page's error line above.
 */
function PullRequestField({ url, onSave }: { url: string | null; onSave: (url: string) => void }) {
  const [draft, setDraft] = useState(url ?? '');
  const [known, setKnown] = useState(url);

  // Re-syncs when the issue changes underneath - see WipLimitField.
  if (url !== known) {
    setKnown(url);
    setDraft(url ?? '');
  }

  return (
    <Field label="Pull request" hint="An absolute address. Emptied, it clears.">
      <input
        type="url"
        value={draft}
        placeholder="https://github.com/owner/repo/pull/12"
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => {
          const trimmed = draft.trim();
          if (trimmed !== (url ?? '')) onSave(trimmed);
        }}
      />
    </Field>
  );
}

/**
 * This issue's own runner tags, editable right where they are read -
 * blur-commit, re-sync when they change underneath, the way WipLimitField
 * is. `RunnerTagsField` already renders its own Field around the textarea,
 * so this wraps it in a bare div for the blur handler rather than a second
 * Field, which would stack two "Runner tags" labels in the DOM.
 */
function IssueRunnerTagsOwnField({ tags, busy, onSave }: { tags: string[]; busy: boolean; onSave: (tags: string[]) => void }) {
  const [draft, setDraft] = useState(runnerTagsDraft(tags));
  const [known, setKnown] = useState(tags);

  // Re-syncs when the issue changes underneath - see WipLimitField.
  if (!sameTags(tags, known)) {
    setKnown(tags);
    setDraft(runnerTagsDraft(tags));
  }

  return (
    <div
      onBlur={() => {
        const next = runnerTagsRequest(draft);
        if (!sameTags(next, tags)) onSave(next);
      }}
    >
      <RunnerTagsField
        label="Runner tags"
        hint="One tag per line. Only a runner advertising every one of these, and every tag inherited below, is handed this ticket. Blank adds nothing."
        value={draft}
        busy={busy}
        onChange={setDraft}
      />
    </div>
  );
}

/** One sentence per ancestor or project a tag is inherited from, in order -
    the project unlinked, an issue linked back to its own page. */
function RunnerTagsInherited({ from }: { from: RunnerTagSource[] }) {
  if (from.length === 0) return null;
  return (
    <>
      {from.map((source) => (
        <p key={`${source.kind}-${source.key}`} className="hatch-section-hint text-muted">
          Also {joinTags(source.tags)} from{' '}
          {source.kind === 'issue' ? <Link to={`/issues/${source.key}`}>{source.key}</Link> : runnerTagsFromLabel(source)}.
        </p>
      ))}
    </>
  );
}

/**
 * This issue's own dangerous reason, editable right where it is read -
 * blur-commit, re-sync when it changes underneath, on PullRequestField.
 */
function DangerousOwnField({ reason, busy, onSave }: { reason: string | null; busy: boolean; onSave: (reason: string) => void }) {
  const [draft, setDraft] = useState(dangerousDraft(reason));
  const [known, setKnown] = useState(reason);

  // Re-syncs when the issue changes underneath - see WipLimitField.
  if (reason !== known) {
    setKnown(reason);
    setDraft(dangerousDraft(reason));
  }

  return (
    <Field label="Dangerous" hint="One line: why running this could destabilise a machine. Emptied, it clears the flag.">
      <input
        type="text"
        value={draft}
        maxLength={200}
        disabled={busy}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => {
          const next = dangerousRequest(draft);
          if (next !== (reason ?? '')) onSave(next);
        }}
      />
    </Field>
  );
}

/** The sentence naming the ancestor a dangerous reason is inherited from -
    null where the reason is this issue's own, or there is none. */
function DangerousInherited({ from, reason }: { from: string | null; reason: string | null }) {
  if (!from) return null;
  return (
    <p className="hatch-section-hint text-muted">
      Inherited from <Link to={`/issues/${from}`}>{from}</Link>: {reason}
    </p>
  );
}

function EventTrail({ events }: { events: IssueEvent[] }) {
  return (
    <IssueSection id="history" title={<NewestFirstTitle>History</NewestFirstTitle>} count={events.length} defaultOpen={false}>
      <ul className="hatch-events">
        {events.map((event) => (
          <li key={event.id} className="hatch-event">
            <span className="text-muted">{new Date(event.at).toLocaleString()}</span>
            <Badge>{event.kind.replaceAll('_', ' ')}</Badge>
            <span>{event.actor}</span>
            <span className="text-muted">{describe(event)}</span>
          </li>
        ))}
      </ul>
    </IssueSection>
  );
}

