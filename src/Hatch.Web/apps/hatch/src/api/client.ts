import { consumptionFilterQuery, type ConsumptionFilterQuery } from '../lib/consumptionReport';
import { HttpError, refusalSentence } from '../lib/errors';
import { handledRefusal } from '../lib/signIn';
import type {
  ActivityPage,
  ActivityReport,
  AmbitionChange,
  AmbitionPass,
  ChangeDecisionRequest,
  ChangeOutcome,
  IssueBudget,
  CreateRunnerRequest,
  AssigneeDirectory,
  AssigneeRequest,
  Attachment,
  Attention,
  BackupStatus,
  Board,
  BoardHealth,
  Capability,
  ChangelogReport,
  Comment,
  CommentCreateRequest,
  ConsumptionReport,
  CumulativeFlow,
  DangerousRequest,
  NoCodeRequest,
  Dream,
  DreamCreateRequest,
  DreamPatchRequest,
  DreamsPage,
  DreamShapeRequest,
  DreamWeaveRequest,
  HatchSettings,
  HatchSettingsWriteRequest,
  HumanWork,
  ImportRequest,
  ImportResult,
  Issue,
  LocalModel,
  IssueBulkEditRequest,
  IssueBulkResult,
  IssueCard,
  IssueCreateRequest,
  IssueDependencyRequest,
  IssueEvent,
  IssueMoveRequest,
  DreamRemandRequest,
  DreamRemandResult,
  IssuePatchRequest,
  IssuePlaybookRequest,
  IssueRollup,
  IssueRunnerTagsWriteRequest,
  IssueSearch,
  Me,
  AuthMe,
  ParsedEpic,
  PastedPlan,
  AmbitionRow,
  Ballot,
  Plan,
  Person,
  PersonSession,
  ChatChannel,
  ChatManifest,
  ChatRoom,
  ChatRoomCreate,
  ChatRoomPatch,
  ChatTestResult,
  ChatDelivery,
  ChatWorkspace,
  PersonCreateRequest,
  PersonScope,
  PersonScopeWriteRequest,
  PersonWriteRequest,
  Playbook,
  LocalModelCreateRequest,
  LocalModelRow,
  PlaybookCreateRequest,
  PlaybookFilesDto,
  ChangelogFilesDto,
  DependencyFilesDto,
  PlaybookPatchRequest,
  PlaybookProposal,
  OmitRunnerRequest,
  ServedCapability,
  PlaybookProposalApproveResult,
  PlaybookProposalRejectResult,
  Project,
  ProjectCreateRequest,
  ProjectLandingWriteRequest,
  ProjectMember,
  ProjectPatchRequest,
  ProjectBindsTrackerResponse,
  ProjectBindsTrackerWriteRequest,
  ProjectStoriesOnEpicsWriteRequest,
  ProjectCheckApprovesMergeWriteRequest,
  ProjectNoCodeClosesWriteRequest,
  ProjectRunnersWorkContributionsWriteRequest,
  ProjectFilesContributionsWriteRequest,
  ProjectAllowsUnsignedCommitsWriteRequest,
  ProjectOnePrPolicyWriteRequest,
  ProjectPriorityWriteRequest,
  ProjectRepoSourcedWriteRequest,
  ProjectRepository,
  ProjectRepositoryWriteRequest,
  ProjectRunnerTagsWriteRequest,
  Question,
  QuestionLevel,
  QuestionOverturn,
  QuestionPromote,
  QueueEntry,
  Revision,
  Runner,
  RunnerDownloads,
  RunnerFailures,
  RunnerMinted,
  RunnerOrderRequest,
  RunnerPatchRequest,
  RunnerSessionPatchRequest,
  SearchResult,
  SessionSort,
  Status,
  StatusCreateRequest,
  StatusPatchRequest,
  StatusRunnerTagsWriteRequest,
  Timeline,
  GooseFinding,
  GooseFindingBugRequest,
  GooseRun,
  GooseRunDetail,
  TrunkBuildBugRequest,
  Utilization,
  WipSection,
  WipSectionRequest,
  Work,
  WorkLog,
  WorkLogByPlaybook,
  WorkLogHistory,
  WorkLogSessions,
} from '../types';

async function fetchJson<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    // Only a string body is ours to label. FormData sets its own content type
    // with the multipart boundary in it, and stating one here would produce a
    // header the server cannot parse the body against.
    headers: {
      Accept: 'application/json',
      ...(typeof init?.body === 'string' ? { 'Content-Type': 'application/json' } : {}),
    },
    ...init,
  });
  if (await handledRefusal(res)) {
    // Navigating away; this promise is abandoned with the document.
    return await new Promise<T>(() => {});
  }
  if (!res.ok) {
    const text = await res.text();
    const fallback = `${init?.method ?? 'GET'} ${path} failed: ${res.status} ${res.statusText}`;
    let body: unknown;
    try {
      body = JSON.parse(text);
    } catch {
      body = undefined;
    }
    throw new HttpError(refusalSentence(text, fallback), res.status, body);
  }
  // 204s (every DELETE here) have no body, and res.json() throws on empty input.
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

const asJson = (body: unknown): RequestInit => ({ body: JSON.stringify(body) });

/** The display key in a URL. Keys are ASCII by construction, but a malformed
    one must not be able to reach outside its path segment. */
const seg = (key: string) => encodeURIComponent(key);

// ---- Board ----

export const getBoard = () => fetchJson<Board>('/api/board');

// ---- Projects ----

export const getProjects = () => fetchJson<Project[]>('/api/projects');
export const createProject = (request: ProjectCreateRequest) =>
  fetchJson<Project>('/api/projects', { method: 'POST', ...asJson(request) });
export const patchProject = (id: number, request: ProjectPatchRequest) =>
  fetchJson<Project>(`/api/projects/${id}`, { method: 'PATCH', ...asJson(request) });
export const deleteProject = (id: number) =>
  fetchJson<void>(`/api/projects/${id}`, { method: 'DELETE' });
export const putProjectRepositories = (id: number, request: ProjectRepositoryWriteRequest[]) =>
  fetchJson<ProjectRepository[]>(`/api/projects/${id}/repositories`, { method: 'PUT', ...asJson(request) });
export const putProjectLanding = (id: number, request: ProjectLandingWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/landing`, { method: 'PUT', ...asJson(request) });
export const putProjectPlaybooksSource = (id: number, request: ProjectRepoSourcedWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/playbooks-source`, { method: 'PUT', ...asJson(request) });
export const putProjectReflection = (id: number, cadenceDays: number | null) =>
  fetchJson<Project>(`/api/projects/${id}/reflection`, { method: 'PUT', ...asJson({ cadenceDays }) });
/** Files the reflection the dialog's *Reflect now* button asks for, or
    answers the one already filed - see ProjectsController.PostReflect.
    Owner-only, same gate as putProjectLanding. */
export const fileProjectReflection = (id: number) =>
  fetchJson<Issue>(`/api/projects/${id}/reflect`, { method: 'POST' });
export const putProjectRunnerTags = (id: number, request: ProjectRunnerTagsWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/runner-tags`, { method: 'PUT', ...asJson(request) });
/** Sets whether a project binds the tracker's own repository, taking the flag
    from whichever project had it - see ProjectsController.PutBindsTracker. */
export const putProjectBindsTracker = (id: number, request: ProjectBindsTrackerWriteRequest) =>
  fetchJson<ProjectBindsTrackerResponse>(`/api/projects/${id}/binds-tracker`, { method: 'PUT', ...asJson(request) });
/** Sets whether a project's board shows stories on their epics. */
export const putProjectStoriesOnEpics = (id: number, request: ProjectStoriesOnEpicsWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/stories-on-epics`, { method: 'PUT', ...asJson(request) });
/** Sets whether a green check on a pull request approves its merge. */
export const putProjectCheckApprovesMerge = (id: number, request: ProjectCheckApprovesMergeWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/check-approves-merge`, { method: 'PUT', ...asJson(request) });
/** Sets whether a no-code declaration closes the ticket. */
export const putProjectNoCodeCloses = (id: number, request: ProjectNoCodeClosesWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/no-code-closes`, { method: 'PUT', ...asJson(request) });
/** Sets whether runners work a contribution's branch. */
export const putProjectRunnersWorkContributions = (id: number, request: ProjectRunnersWorkContributionsWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/runners-work-contributions`, { method: 'PUT', ...asJson(request) });
/** Sets whether outside pull requests are filed as contributions. */
export const putProjectFilesContributions = (id: number, request: ProjectFilesContributionsWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/files-contributions`, { method: 'PUT', ...asJson(request) });
/** Sets whether a project allows unsigned commits. */
export const putProjectAllowsUnsignedCommits = (id: number, request: ProjectAllowsUnsignedCommitsWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/allows-unsigned-commits`, { method: 'PUT', ...asJson(request) });
/** Sets whether a ticket's work must land in one pull request. */
export const putProjectOnePrPolicy = (id: number, request: ProjectOnePrPolicyWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/one-pr-policy`, { method: 'PUT', ...asJson(request) });
/** Sets how much a project matters to the loop. */
export const putProjectPriority = (id: number, request: ProjectPriorityWriteRequest) =>
  fetchJson<Project>(`/api/projects/${id}/priority`, { method: 'PUT', ...asJson(request) });
export const putProjectMember =(id: number, personId: string, role: string) =>
  fetchJson<ProjectMember[]>(`/api/projects/${id}/members/${seg(personId)}`, { method: 'PUT', ...asJson({ role }) });
export const deleteProjectMember = (id: number, personId: string) =>
  fetchJson<ProjectMember[]>(`/api/projects/${id}/members/${seg(personId)}`, { method: 'DELETE' });
export const claimProject = (id: number) =>
  fetchJson<ProjectMember[]>(`/api/projects/${id}/claim`, { method: 'POST' });
export const putProjectLogo = (id: number, blob: Blob) =>
  fetchJson<Project>(`/api/projects/${id}/logo`, { method: 'PUT', body: blob });
export const deleteProjectLogo = (id: number) =>
  fetchJson<Project>(`/api/projects/${id}/logo`, { method: 'DELETE' });

// ---- Attachments ----

/** Stores one file. Multipart, so the browser sets the boundary and this does
    not name its own content type, as with `previewImport`. `projectId`, given,
    is sent on the query string, so the attachment can be linked to the project
    the editor is in - left off `null` or `undefined`, as `getPlan`'s does. */
export const uploadAttachment = (blob: Blob, fileName: string, projectId?: number | null) => {
  const form = new FormData();
  form.append('file', blob, fileName);
  const qs = projectId !== null && projectId !== undefined ? `?projectId=${projectId}` : '';
  return fetchJson<Attachment>(`/api/attachments${qs}`, { method: 'POST', body: form });
};

/** The attachments among `ids` that still exist, in the order asked. Ids are
    guids, so no encoding. The route caps at 100 ids and drops any unknown or
    duplicate id from the answer. */
export const getAttachments = (ids: string[]) =>
  fetchJson<Attachment[]>(`/api/attachments?ids=${ids.join(',')}`);

export const deleteAttachment = (id: string) =>
  fetchJson<void>(`/api/attachments/${id}`, { method: 'DELETE' });

// ---- Statuses ----

export const getStatuses = () => fetchJson<Status[]>('/api/statuses');
export const createStatus = (request: StatusCreateRequest) =>
  fetchJson<Status>('/api/statuses', { method: 'POST', ...asJson(request) });
export const patchStatus = (id: number, request: StatusPatchRequest) =>
  fetchJson<Status>(`/api/statuses/${id}`, { method: 'PATCH', ...asJson(request) });
export const deleteStatus = (id: number) =>
  fetchJson<void>(`/api/statuses/${id}`, { method: 'DELETE' });

/** Which columns are work in progress, and how much of one slice of the board
    may sit across them at once. Its own route because a key must not write it
    - see WipController. */
export const getWip = () => fetchJson<WipSection>('/api/wip');
export const putWip = (request: WipSectionRequest) =>
  fetchJson<WipSection>('/api/wip', { method: 'PUT', ...asJson(request) });

/** Which columns an express issue is carried past with no session. Its own
    route rather than a field on the patch, and for the same reason the
    express issue flag has one: writing it is closed to an API key - see
    StatusesController.PutExpressSkips. */
export const setExpressSkips = (id: number, expressSkips: boolean) =>
  fetchJson<Status>(`/api/statuses/${id}/express-skips`, {
    method: 'PUT',
    ...asJson({ expressSkips }),
  });

/** Whether a child standing in this column is carried on to the next one with
    no session while its parent stands in the implementation column. Its own
    route for the same reason ExpressSkips has one: writing it is closed to an
    API key - see StatusesController.PutParentPulls. */
export const setParentPulls = (id: number, parentPulls: boolean) =>
  fetchJson<Status>(`/api/statuses/${id}/parent-pulls`, {
    method: 'PUT',
    ...asJson({ parentPulls }),
  });

/** Which column a program's own issues are born in, rather than the
    leftmost one. Its own route for the same reason ParentPulls has one:
    writing it is closed to an API key - see StatusesController.PutAgentFiles. */
export const setAgentFiles = (id: number, agentFiles: boolean) =>
  fetchJson<Status>(`/api/statuses/${id}/agent-files`, {
    method: 'PUT',
    ...asJson({ agentFiles }),
  });

/** Which column is where code gets written. Its own route for the same
    reason AgentFiles has one: writing it is closed to an API key - see
    StatusesController.PutImplementation. */
export const setImplementation = (id: number, isImplementation: boolean) =>
  fetchJson<Status>(`/api/statuses/${id}/implementation`, {
    method: 'PUT',
    ...asJson({ isImplementation }),
  });

/** Who may move work into this column: an owner or approver of the issue's
    project, never a key. Its own route for the reason the other four flag
    routes have one - see StatusesController.PutProtected. */
export const setProtected = (id: number, on: boolean) =>
  fetchJson<Status>(`/api/statuses/${id}/protected`, { method: 'PUT', ...asJson({ on }) });

/** Whether the runner merges an issue's pull request once it stands here,
    current and green, with no session. Requires Protected - see
    StatusesController.PutMergesPullRequest. */
export const setMergesPullRequest = (id: number, on: boolean) =>
  fetchJson<Status>(`/api/statuses/${id}/merges-pull-request`, { method: 'PUT', ...asJson({ on }) });

/** Replaces the runner tags a column requires; `[]` clears them. A person, not
    a key, writes it - see StatusesController.PutRunnerTags. */
export const setStatusRunnerTags = (id: number, request: StatusRunnerTagsWriteRequest) =>
  fetchJson<Status>(`/api/statuses/${id}/runner-tags`, { method: 'PUT', ...asJson(request) });

// ---- Issues ----

// ---- Playbooks ----
//
// Reading is all an API key may do here; the three writes are
// refused for one, which is why they exist only on this page and never in a
// script. See PlaybooksController.

export const getPlaybooks = () => fetchJson<Playbook[]>('/api/playbooks');
export const getPlaybookProposals = () => fetchJson<PlaybookProposal[]>('/api/playbook-proposals');
export const approveAllPlaybookProposals = (proposalIds: number[]) =>
  fetchJson<PlaybookProposalApproveResult>('/api/playbook-proposals/approve-all', { method: 'POST', ...asJson({ proposalIds }) });
export const rejectAllPlaybookProposals = (proposalIds: number[]) =>
  fetchJson<PlaybookProposalRejectResult>('/api/playbook-proposals/reject-all', { method: 'POST', ...asJson({ proposalIds }) });
export const createPlaybook = (request: PlaybookCreateRequest) =>
  fetchJson<Playbook>('/api/playbooks', { method: 'POST', body: JSON.stringify(request) });
export const patchPlaybook = (id: number, request: PlaybookPatchRequest) =>
  fetchJson<Playbook>(`/api/playbooks/${id}`, { method: 'PATCH', body: JSON.stringify(request) });
export const deletePlaybook = (id: number) =>
  fetchJson<void>(`/api/playbooks/${id}`, { method: 'DELETE' });

// ---- Local models ----
//
// Reading is open to a key, since a runner's own beat has to know the table;
// the two writes are a person's alone, like the playbook writes above. See
// LocalModelsController.

export const getLocalModels = () => fetchJson<LocalModelRow[]>('/api/local-models');
export const createLocalModel = (request: LocalModelCreateRequest) =>
  fetchJson<LocalModelRow>('/api/local-models', { method: 'POST', ...asJson(request) });
export const deleteLocalModel = (id: number) =>
  fetchJson<void>(`/api/local-models/${id}`, { method: 'DELETE' });

export const getPlaybookFiles = () => fetchJson<PlaybookFilesDto>('/api/playbook-files');

export const getDependencyFiles = () => fetchJson<DependencyFilesDto>('/api/dependency-files');
export const getChangelogFiles = () => fetchJson<ChangelogFilesDto>('/api/changelog-files');

// ---- Runners ----
//
// Reading is open to a key, like the claim - a dispatcher that
// could not say it was alive would leave a page that could only ever be empty.
// The write is refused to one, for the reason the playbook writes are: an agent
// that could raise its own --max-spend could raise its own budget. See
// RunnersController.
//
// There is no heartbeat here. A browser is not a runner, and the only caller
// that could ever post one is the loop itself.

export const getRunners = () => fetchJson<Runner[]>('/api/runners?live=true');
export const getRunnerFailures = (name: string) =>
  fetchJson<RunnerFailures>(`/api/runners/${seg(name)}/failures`);
export const patchRunner = (name: string, request: RunnerPatchRequest) =>
  fetchJson<Runner>(`/api/runners/${seg(name)}`, { method: 'PATCH', ...asJson(request) });
export const putRunnerRefusedModels = (name: string, models: string[]) =>
  fetchJson<Runner>(`/api/runners/${seg(name)}/refused-models`, { method: 'PUT', ...asJson({ models }) });
export const orderRunner = (name: string, request: RunnerOrderRequest) =>
  fetchJson<Runner>(`/api/runners/${seg(name)}/order`, { method: 'POST', ...asJson(request) });
export const withdrawOrder = (name: string) =>
  fetchJson<void>(`/api/runners/${seg(name)}/order`, { method: 'DELETE' });

// The runners page's own calls. Admin only, except the reads and the per-session
// writes a person owns - the controller decides, and a 403 is its answer.

/** Every runner, revoked and gone ones too: no `live` flag, unlike `getRunners`. */
export const getAllRunners = () => fetchJson<Runner[]>('/api/runners');
/** A name nobody has taken, answered as a bare string. */
export const suggestRunnerName = () => fetchJson<string>('/api/runners/suggest-name');
/** The response carries the secret, once. */
export const createRunner = (request: CreateRunnerRequest) =>
  fetchJson<RunnerMinted>('/api/runners', { method: 'POST', ...asJson(request) });
/** Replaces the runner's secret; the new one is in the response, once. */
export const rotateRunner = (name: string) =>
  fetchJson<RunnerMinted>(`/api/runners/${seg(name)}/rotate`, { method: 'POST' });
export const revokeRunner = (name: string) =>
  fetchJson<void>(`/api/runners/${seg(name)}/revoke`, { method: 'POST' });
/** Changes or clears whose tickets a runner's `--mine` reaches. */
export const setRunnerOwner = (name: string, personId: string | null) =>
  fetchJson<void>(`/api/runners/${seg(name)}/owner`, { method: 'PUT', ...asJson({ personId }) });
export const patchRunnerSession = (name: string, id: number, request: RunnerSessionPatchRequest) =>
  fetchJson<Runner>(`/api/runners/${seg(name)}/sessions/${id}`, { method: 'PATCH', ...asJson(request) });
export const orderRunnerSession = (name: string, id: number, request: RunnerOrderRequest) =>
  fetchJson<Runner>(`/api/runners/${seg(name)}/sessions/${id}/order`, { method: 'POST', ...asJson(request) });
export const withdrawSessionOrder = (name: string, id: number) =>
  fetchJson<void>(`/api/runners/${seg(name)}/sessions/${id}/order`, { method: 'DELETE' });
/** Lifts a board pause, so the session's next heartbeat and next claim run as normal. */
export const liftBoardPause = (name: string, id: number) =>
  fetchJson<void>(`/api/runners/${seg(name)}/sessions/${id}/pause`, { method: 'DELETE' });
export const pauseAllSessions = (name: string) =>
  fetchJson<void>(`/api/runners/${seg(name)}/pause-all`, { method: 'POST' });
export const stopAllSessions = (name: string) =>
  fetchJson<void>(`/api/runners/${seg(name)}/stop-all`, { method: 'POST' });

export const getIssue = (key: string) => fetchJson<Issue>(`/api/issues/${seg(key)}`);

export const getIssueQuestions = (key: string, open = true) =>
  fetchJson<Question[]>(`/api/issues/${seg(key)}/questions${open ? '' : '?open=false'}`);

/** The issues a filter finds, as cards. An absent field is left off the query
    string entirely - an empty `parentKey` means "no parent" to the server, so
    sending one for a field nobody filled in would silently ask a different
    question. */
export const searchIssues = (filter: IssueSearch) => {
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(filter)) {
    if (value === null || value === undefined) continue;
    if (Array.isArray(value)) for (const v of value) params.append(name, String(v));
    else params.set(name, String(value));
  }
  const query = params.toString();
  return fetchJson<IssueCard[]>(`/api/issues${query ? `?${query}` : ''}`);
};

/** The search page's data: `params` is already built, so the page and the
    test share one builder (lib/search.ts's `searchRequest`). */
export const getSearch = (params: URLSearchParams) =>
  fetchJson<SearchResult>(`/api/search?${params.toString()}`);

export const bulkEditIssues = (request: IssueBulkEditRequest) =>
  fetchJson<IssueBulkResult>('/api/issues/bulk', { method: 'POST', ...asJson(request) });
export const createIssue = (request: IssueCreateRequest) =>
  fetchJson<Issue>('/api/issues', { method: 'POST', ...asJson(request) });
export const patchIssue = (key: string, request: IssuePatchRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}`, { method: 'PATCH', ...asJson(request) });
/** The two things an issue overrides its playbooks with. Its own route because
    setting one is closed to an API key - see IssuePlaybookController. */
/** Everybody an issue could belong to, and who the caller is. One read, because
    the picker needs the first and **Assign to me** needs the second. */
export const getAssignees = () => fetchJson<AssigneeDirectory>('/api/assignees');

/** Give an issue to somebody, or - with both fields null - to nobody. Its own
    route rather than a field on the patch: writing one is closed to an API key,
    and that refusal is a property of the route. */
export const setAssignee = (key: string, request: AssigneeRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/assignee`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });

/** *This one first, or further.* Its own route rather than a field on the
    patch, for the same reason the assignee has one: writing it is closed to
    an API key, because priority decides what the loop reaches for first and,
    at emergency, what it preempts - see IssueExpediteController.

    The level is sent by name rather than as a step, so two browsers looking
    at the same card cannot move it in conflicting directions and leave the
    answer depending on which request landed second. */
export const setPriority = (key: string, priority: 'normal' | 'expedited' | 'emergency' | 'low' | 'economy' | 'paused') =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/priority`, {
    method: 'PUT',
    ...asJson({ priority }),
  });

/** A story's own say over whether its tasks share its branch and one pull
    request: `on`, `off`, or `inherit` to follow the project's default. Open to
    an API key, unlike priority and express - it decides how a story is
    branched, not what the loop reaches for - see IssueOnePrController. */
export const setOnePr = (key: string, value: 'on' | 'off' | 'inherit') =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/one-pr`, {
    method: 'PUT',
    ...asJson({ value }),
  });

/** Its own route rather than a field on the patch, because the patch carries
    no field for it - see IssueReleaseNoteController. An empty string clears
    the note. */
export const setReleaseNote = (key: string, note: string) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/release-note`, {
    method: 'PUT',
    ...asJson({ note }),
  });

/** The approve press given in advance. Its own route rather than a field on
    the patch, and for the same reason priority has one: writing it is closed
    to an API key, because pre-approval decides which gates the loop may pass
    unattended - see IssuePreApprovalController.

    The level is sent by name rather than as a step, for the same reason
    `setPriority` sends one. */
export const setPreApproval = (key: string, level: 'none' | 'express' | 'preapproved') =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/pre-approval`, {
    method: 'PUT',
    ...asJson({ level }),
  });

/** How far an issue leans on a local model. Its own route rather than a field
    on the patch, and for the same reason pre-approval has one: the level is
    walked live down the tree and is sent by name, and writing it is closed to
    an API key - see IssueLocalModelController. */
export const setLocalModel = (key: string, level: LocalModel) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/local-model`, {
    method: 'PUT',
    ...asJson({ level }),
  });

/** How far a question is allowed to go on its recommendation - `hold`, `build`
    or `merge`. Its own route, and a person's alone: the level is the
    operator's say over how much runs ahead of an answer - see
    QuestionLevelController. A refusal comes back as a plain sentence. */
export const setQuestionLevel = (key: string, id: number, level: QuestionLevel) =>
  fetchJson<Question>(`/api/issues/${seg(key)}/questions/${id}/level`, {
    method: 'PUT',
    ...asJson({ level }),
  });

/** Overturn a look-back question: the person's own answer in place of the
    option the work went ahead on, and one task filed to bring the work in
    line. A refusal comes back as a plain sentence - see
    QuestionOverturnController. */
export const overturnQuestion = (key: string, id: number, answer: string) =>
  fetchJson<QuestionOverturn>(`/api/issues/${seg(key)}/questions/${id}/overturn`, {
    method: 'POST',
    ...asJson({ answer }),
  });

/** Promote a question's answer, or the sentence given instead, into a
    principle of the ambition above its issue. A refusal comes back as a
    plain sentence - see QuestionPromoteController. */
export const promoteQuestion = (key: string, id: number, principle: string) =>
  fetchJson<QuestionPromote>(`/api/issues/${seg(key)}/questions/${id}/promote`, {
    method: 'POST',
    ...asJson({ principle }),
  });

/** Who works an issue - `human`, `agent` or `inherit`. Its own route, closed
    to an API key the same way `setLocalModel`'s is - see
    IssueHumanWorkController. */
export const setHumanWork = (key: string, value: HumanWork) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/human-work`, {
    method: 'PUT',
    ...asJson({ value }),
  });

export const setIssueRunnerTags = (key: string, request: IssueRunnerTagsWriteRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/runner-tags`, { method: 'PUT', ...asJson(request) });

export const setIssueDangerous = (key: string, request: DangerousRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/dangerous`, { method: 'PUT', ...asJson(request) });

export const setIssueNoCode = (key: string, request: NoCodeRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/no-code`, { method: 'PUT', ...asJson(request) });

export const omitRunner = (key: string, runner: string, request: OmitRunnerRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/omitted-runners/${seg(runner)}`, { method: 'PUT', ...asJson(request) });

export const liftRunnerOmission = (key: string, runner: string) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/omitted-runners/${seg(runner)}`, { method: 'DELETE' });

/** Mark a ticket stalled, with why - and, through the same panel, read back
    off. On `setPriority`'s shape: the state is sent, not toggled, and the
    route is its own rather than a field on the patch. Today only the runner
    and the board write this; nothing in the browser calls it yet. */
export const setStall = (key: string, why: string) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/stall`, {
    method: 'PUT',
    ...asJson({ why }),
  });

/** Hold a ticket for a person, or let it go - StallPanel's two buttons. On
    `setPriority`'s shape, matching HoldRequest(bool Held). */
export const setHold = (key: string, held: boolean) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/hold`, {
    method: 'PUT',
    ...asJson({ held }),
  });

/** Take a ticket back off a runner - the operator's clobber, with no token in
    it, which is why the server refuses it from an API key: a key may release
    only the lease it holds. Answers nothing (204), so the page re-reads rather
    than repainting from a response.

    There is deliberately no take and no heartbeat beside it. A person does not
    hold a lease; the only claim verb a browser gets is this one. */
export const clearClaim = (key: string) =>
  fetchJson<void>(`/api/issues/${seg(key)}/claim`, { method: 'DELETE' });

export const patchIssuePlaybook = (key: string, request: IssuePlaybookRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/playbook`, { method: 'PATCH', ...asJson(request) });
/** How many of an epic's stories may run at once. Its own route for
    IssuePlaybookController's reason: writing it is closed to an API key,
    because a key that could raise its own epic's ceiling could pull more of
    its own stories into progress at once - see IssueWipLimitController. */
export const setWipLimit = (key: string, limit: string) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/wip`, { method: 'PATCH', ...asJson({ limit }) });
/** What an issue waits on. Both verbs answer with the whole issue, so the page
    repaints from one response instead of composing the new state itself, and
    there is no GET: the two lists ride the issue. */
export const addDependency = (key: string, request: IssueDependencyRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/dependencies`, { method: 'POST', ...asJson(request) });
export const removeDependency = (key: string, dependsOnKey: string) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/dependencies/${seg(dependsOnKey)}`, { method: 'DELETE' });
export const deleteIssue = (key: string) =>
  fetchJson<void>(`/api/issues/${seg(key)}`, { method: 'DELETE' });
export const moveIssue = (key: string, request: IssueMoveRequest) =>
  fetchJson<Issue>(`/api/issues/${seg(key)}/move`, { method: 'POST', ...asJson(request) });
/** Shelve an issue and the work under it into a deferred column, and post a dream from it. */
export const remandIssue = (key: string, request: DreamRemandRequest) =>
  fetchJson<DreamRemandResult>(`/api/issues/${seg(key)}/remand`, { method: 'POST', ...asJson(request) });

// ---- Progress ----

/** What one subtree adds up to, and what each of its direct children adds up
    to. Server-side arithmetic on purpose: a meter re-derived here would
    disagree with the CLI the first time a column was added. See Rollup.cs. */
export const getIssuePlan = (key: string) => fetchJson<IssueRollup>(`/api/plan/${seg(key)}`);

/** Every epic in the tracker and what it adds up to, plus what hangs under no
    epic at all. Its own read rather than a corner of the board: the board is
    refetched after every drag, and a rollup bolted onto it would be recomputed
    on every drop for a screen nobody is looking at. */
export const getPlan = (projectId?: number | number[]) => {
  const params = new URLSearchParams();
  for (const id of ([] as number[]).concat(projectId ?? [])) params.append('projectId', String(id));
  const qs = params.toString();
  return fetchJson<Plan>(`/api/plan${qs ? `?${qs}` : ''}`);
};

/** Every question waiting on a person, as a tree of the issues that enclose
    them. `projectId`, given, narrows to those projects - repeated per id, and
    left off when none, as `getPlan`'s is. */
export const getBallot = (projectId?: number | number[]) => {
  const params = new URLSearchParams();
  for (const id of ([] as number[]).concat(projectId ?? [])) params.append('projectId', String(id));
  const qs = params.toString();
  return fetchJson<Ballot>(`/api/ballot${qs ? `?${qs}` : ''}`);
};

/** Every ambition across projects, each with its capabilities counted four
    ways and the epics open under it. `projectId`, given, narrows to those
    projects - repeated per id, and left off when none, as `getBallot`'s is. */
export const getAmbitions = (projectId?: number | number[]) => {
  const params = new URLSearchParams();
  for (const id of ([] as number[]).concat(projectId ?? [])) params.append('projectId', String(id));
  const qs = params.toString();
  return fetchJson<AmbitionRow[]>(`/api/ambitions${qs ? `?${qs}` : ''}`);
};

// ---- Work ----

/**
 * What an agent would pick up under this issue, and how it would be dispatched
 * - or null when nothing beneath it is an agent's to move.
 *
 * The rule for "next" is the server's and is not re-derived here: right to
 * left, top of the column down, folding past a ready date, an open question or
 * a terminal column. `ancestorKey` narrows the candidates to one subtree and
 * changes nothing else. See WorkController.
 *
 * A 204 arrives as an empty body, which `fetchJson` gives back as undefined;
 * it is normalised to null so a caller has one absent value rather than two.
 * It is not a failure - "nothing to do" is an answer.
 */
export const getNextWorkUnder = (ancestorKey: string) =>
  fetchJson<Work | undefined>(
    `/api/work/next?ancestorKey=${encodeURIComponent(ancestorKey)}&offsetMinutes=${-new Date().getTimezoneOffset()}`,
  ).then((work) => work ?? null);

/**
 * Every issue a pass would look at, in the order it looks, each with the
 * sentence saying why it cannot be advanced - or nothing, where it can. See
 * WorkController.GetQueue.
 *
 * No `ancestorKey`, `remote`, `standing` or `clones`: a browser holds no
 * checkouts and must not claim to, and nothing here narrows the scan to a
 * subtree yet. `asRunner`, given, answers off that runner's own declared row
 * instead of this undeclared read - see WorkController.GetQueue's own doc
 * comment for the four it still refuses alongside it.
 *
 * `takers`, given, adds to each clear row the live runners that could take its
 * step (`takers`) and, where fewer than two can, why each of the rest cannot
 * (`untaken`). It costs the server one scan per live runner, so only the queue
 * modal asks; the flow-viz board poll does not.
 */
export const getQueue = (asRunner: string | null = null, takers = false) => {
  const params = new URLSearchParams({ offsetMinutes: String(-new Date().getTimezoneOffset()) });
  if (asRunner !== null) params.set('asRunner', asRunner);
  if (takers) params.set('takers', 'true');
  return fetchJson<QueueEntry[]>(`/api/work/queue?${params.toString()}`);
};

// ---- Comments and events ----

export const getComments = (key: string) => fetchJson<Comment[]>(`/api/issues/${seg(key)}/comments`);
export const addComment = (key: string, request: CommentCreateRequest) =>
  fetchJson<Comment>(`/api/issues/${seg(key)}/comments`, { method: 'POST', ...asJson(request) });
export const getEvents = (key: string) => fetchJson<IssueEvent[]>(`/api/issues/${seg(key)}/events`);

// ---- Dreams ----

/** A page of dreams: `params` is already built, so the page and the test
    share one builder (lib/dreams.ts's `dreamsRequest`). */
export const getDreams = (params: URLSearchParams) =>
  fetchJson<DreamsPage>(`/api/dreams?${params.toString()}`);
export const addDream = (request: DreamCreateRequest) =>
  fetchJson<Dream>('/api/dreams', { method: 'POST', ...asJson(request) });
export const patchDream = (id: number, request: DreamPatchRequest) =>
  fetchJson<Dream>(`/api/dreams/${id}`, { method: 'PATCH', ...asJson(request) });
export const deleteDream = (id: number) =>
  fetchJson<void>(`/api/dreams/${id}`, { method: 'DELETE' });
export const getIssueDreams = (key: string) => fetchJson<Dream[]>(`/api/issues/${seg(key)}/dreams`);
/** An ambition's capabilities, each with its state, its newest verdict and the
    epics serving it. 400 off an ambition - see IssueCapabilitiesController. */
export const getIssueCapabilities = (key: string) => fetchJson<Capability[]>(`/api/issues/${seg(key)}/capabilities`);
/** What an issue serves, with each capability's ambition. Empty for any type
    that serves nothing - see IssueServesController. */
export const getIssueServes = (key: string) => fetchJson<ServedCapability[]>(`/api/issues/${seg(key)}/serves`);
/** An ambition's passes, newest first, each with the verdicts it recorded and
    the comments said while it ran. 400 off an ambition - see
    IssuePassesController. */
export const getIssuePasses = (key: string) => fetchJson<AmbitionPass[]>(`/api/issues/${seg(key)}/passes`);
/** What an ambition may cost and what it has cost. 400 off an ambition - see
    IssueBudgetController. */
export const getIssueBudget = (key: string) => fetchJson<IssueBudget>(`/api/issues/${seg(key)}/budget`);
/** An ambition's changes, newest first, each with what it did and the
    outcomes recorded so far. 400 off an ambition - see
    IssueChangesController. */
export const getIssueChanges = (key: string) => fetchJson<AmbitionChange[]>(`/api/issues/${seg(key)}/changes`);
/** A person's decision - shelve or keep running - on one recorded outcome.
    The route refuses a key. */
export const decideChangeOutcome = (key: string, outcomeId: number, request: ChangeDecisionRequest) =>
  fetchJson<ChangeOutcome>(`/api/issues/${seg(key)}/changes/outcomes/${outcomeId}/decision`, {
    method: 'POST',
    ...asJson(request),
  });
/** Sets, changes or clears an ambition's budget: a blank amount clears it. A
    person's call - the route refuses a key. */
export const setIssueBudget = (key: string, amount: string) =>
  fetchJson<IssueBudget>(`/api/issues/${seg(key)}/budget`, { method: 'PATCH', ...asJson({ amount }) });
export const putIssueDreams = (key: string, dreamIds: number[]) =>
  fetchJson<Dream[]>(`/api/issues/${seg(key)}/dreams`, { method: 'PUT', ...asJson({ dreamIds }) });
/** Files the epic a weave press asks for - see DreamWeaveController.PostWeave. */
export const weaveDreams = (request: DreamWeaveRequest) =>
  fetchJson<Issue>('/api/dreams/weave', { method: 'POST', ...asJson(request) });

/** Files the draft ambition a shaping press asks for - see
    DreamShapeController.PostShape. A person's press: the route refuses a key. */
export const shapeAmbition = (request: DreamShapeRequest) =>
  fetchJson<Issue>('/api/dreams/shape', { method: 'POST', ...asJson(request) });

/** The trail across every issue, newest first, a page at a time. `limit` is
    left at the server's own default when absent. */
export const getActivity = ({
  limit,
  offset,
  projectId,
}: {
  limit?: number;
  offset?: number;
  projectId?: number[] | null;
}) => {
  const params = new URLSearchParams();
  if (limit !== undefined) params.set('limit', String(limit));
  if (offset !== undefined) params.set('offset', String(offset));
  for (const id of projectId ?? []) params.append('projectId', String(id));
  const query = params.toString();
  return fetchJson<ActivityPage>(`/api/activity${query ? `?${query}` : ''}`);
};

// ---- The importer ----

/** What these files would become, without writing anything. Multipart because
    it is several files at once; the browser sets the boundary, so this is the
    one request here that does not name its own content type. */
export const previewImport = (files: File[]) => {
  const form = new FormData();
  for (const file of files) form.append('files', file, file.name);
  return fetchJson<ParsedEpic[]>('/api/import/preview', { method: 'POST', body: form });
};

/** The same look for a plan that was pasted rather than uploaded. Plain JSON:
    there is no file, so there is no multipart envelope to build. Comes back as
    one epic, which the page wraps in the array the upload path returns so that
    both roads meet at the same preview and the same import. */
export const previewText = (request: PastedPlan) =>
  fetchJson<ParsedEpic>('/api/import/preview-text', { method: 'POST', ...asJson(request) });

export const runImport = (request: ImportRequest) =>
  fetchJson<ImportResult>('/api/import', { method: 'POST', ...asJson(request) });

// ---- The battery ----

/**
 * My own Claude headroom, off whatever my own runners last reported on their
 * heartbeat.
 *
 * A 204 arrives as an empty body, which `fetchJson` gives back as undefined,
 * and it is normalised to null the way `getNextWorkUnder` normalises its own.
 * It is the answer this endpoint gives on a fresh install, and to anybody who
 * has never run a runner - and it is not a failure: it means there is no
 * battery here, and the component draws nothing at all.
 */
export const getUtilization = () =>
  fetchJson<Utilization | undefined>('/api/utilization').then((reading) => reading ?? null);

// ---- What is waiting on a person ----

/**
 * Whether the loop is waiting on a person, and on what.
 *
 * One request for both halves rather than a board read and a questions read.
 * Two polls can be a poll interval apart, and the disagreement shows up as a
 * lit control whose panel is empty - the one failure a widget like this does
 * not recover from, because after it happens twice nobody reads it again.
 */
export const getAttention = () => fetchJson<Attention>('/api/attention');

/** Files the bug a failing trunk row's *File a bug* button asks for, or answers
    the one already filed - see TrunkBuildBugController. Person-only: a key is
    refused `403`, because the bug it files is expedited. */
export const fileTrunkBuildBug = (id: number, request?: TrunkBuildBugRequest) =>
  fetchJson<Issue>(`/api/trunk-builds/${id}/bug`, { method: 'POST', ...(request ? asJson(request) : {}) });

/** The goose's runs, newest first, without the per-check detail. A caller
    whose scope is narrower is answered an empty list - a run holds what was
    seen across every project. */
export const getGooseRuns = () => fetchJson<GooseRun[]>('/api/goose/runs');

/** One run, with what each check looked at and concluded. */
export const getGooseRun = (id: number) => fetchJson<GooseRunDetail>(`/api/goose/runs/${id}`);

/** Every finding the caller's projects have: open ones first, then closed ones, newest first. */
export const getGooseFindings = () => fetchJson<GooseFinding[]>('/api/goose/findings');

/** Files the bug a goose finding's *File a bug* button asks for, or answers the
    one already filed - see GooseFindingBugController. Person-only, as
    `fileTrunkBuildBug` is of a key: the bug it files is expedited. */
export const fileGooseFindingBug = (id: number, request?: GooseFindingBugRequest) =>
  fetchJson<Issue>(`/api/goose/findings/${id}/bug`, { method: 'POST', ...(request ? asJson(request) : {}) });

// ---- Who is sitting here ----

/**
 * Who Hatch thinks is at this machine, or null.
 *
 * Null is the 204 and is not a failure: it means this install has a wall, so
 * the question does not arise and the bar draws nothing at all - the same
 * shape `getUtilization` takes and for the same reason.
 */
export const getMe = () => fetchJson<Me | undefined>('/api/me').then((me) => me ?? null);

/** Ends the grant and clears the cookie; the caller navigates to sign in. */
export const signOut = () => fetchJson<void>('/api/auth/sign-out', { method: 'POST' });

// ---- Settings ----
//
// Person only, both verbs: unlike every other route in this file, an API key
// is refused here. One of these two settings is a credential and the
// other is the name every event a browser writes is signed with. See
// Hatch.Api.Modules.Hatch.SettingsController.

export const getHatchSettings = () => fetchJson<HatchSettings>('/api/settings');

/** Answers with the settings as they now read, so a save needs no reload. */
export const putHatchSettings = (request: HatchSettingsWriteRequest) =>
  fetchJson<HatchSettings>('/api/settings', { method: 'PUT', ...asJson(request) });

// ---- Backups ----

export const getBackup = () => fetchJson<BackupStatus>('/api/backup');
export const runBackup = () => fetchJson<BackupStatus>('/api/backup/run', { method: 'POST' });

// ---- The runner ----
//
// A person at a browser setting a machine up, or a runner's own key, unlike
// Settings above it: the key lane is what lets a runner check its own build
// against the sha256 this answer carries. The download itself is a plain
// link on the page - it is a file, and a fetch would only mean holding 12MB
// in memory to hand it straight back to the browser. See
// Hatch.Api.Modules.Hatch.RunnerController.

/** Which platforms this image can hand out, and the revision all of them were built from. */
export const getRunner = () => fetchJson<RunnerDownloads>('/api/runner');

// ---- The work log ----

/**
 * What every session run against this issue cost, and what the whole subtree
 * beneath it cost.
 *
 * Read-only, and that is the guarantee rather than a convention: there is no
 * `postWorkLog` here, no patch and no delete, because a work log entry is
 * written by the dispatcher with an API key and by nothing else. The server
 * refuses a browser outright - see IssueWorkLogController.NotAKey - and the absence
 * of a function that could is the client half of the same statement. Do not add
 * one for symmetry.
 */
export const getWorkLog = (key: string) => fetchJson<WorkLog>(`/api/issues/${seg(key)}/work-log`);

/** What a range of the log holds, ranked - and what that whole range cost.

    The leaderboard asks this twice over one range: once pinned to the top five
    by tokens for the ranking, and once at the URL's own sort for the table. Two
    reads rather than one re-sorted in the browser, because a browser re-ranking
    a capped hundred rows would be ranking a sample and calling it a
    leaderboard.

    Anything null or undefined is left out rather than sent empty - the rule
    `searchIssues` states, and it matters here for the same reason: an empty
    `ancestorKey` is not the same question as no `ancestorKey`. */
export const getWorkLogSessions = (query: WorkLogSessionQuery) => {
  const { projectId, ...rest } = query;
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(rest)) {
    if (value !== null && value !== undefined) params.set(name, String(value));
  }
  for (const id of projectId ?? []) params.append('projectId', String(id));
  return fetchJson<WorkLogSessions>(`/api/work-log/sessions?${params.toString()}`);
};

/** The same rows, folded along a time axis - one bucket per bar.

    **No bucket size is sent.** The server chooses it from the length of the
    range and names it back, and the graph labels its axis from that rather than
    from what it asked for. `offsetMinutes` is the same value the page's range
    presets were computed on, so the daily grid the page aligned to and the
    daily grid the server buckets on are one grid. */
export const getWorkLogHistory = (query: WorkLogHistoryQuery) => {
  const { projectId, ...rest } = query;
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(rest)) {
    if (value !== null && value !== undefined) params.set(name, String(value));
  }
  for (const id of projectId ?? []) params.append('projectId', String(id));
  return fetchJson<WorkLogHistory>(`/api/work-log/history?${params.toString()}`);
};

export interface WorkLogHistoryQuery {
  from: string;
  to: string;
  ancestorKey?: string | null;
  /** Any of these projects; omitted or empty means every project. */
  projectId?: number[] | null;
  /** Minutes east of UTC. It puts a daily bucket on the reader's midnight; an
      overnight run split across UTC midnight is two half-nights nobody
      worked. */
  offsetMinutes: number;
  /** One of the claim outcomes, or `none` for sessions with no outcome; left
      out of the URL rather than sent empty when unset. */
  outcome?: string | null;
}

/** When the work happened: the top-level epics active in a range and the work
    beneath each, as stretches along one axis.

    The server defaults `to` to now and `from` to ninety days before it.
    `projectId` and `createdBy` narrow the top-level rows only - what stands
    beneath a row that stays is not filtered again.

    Anything null or undefined is left out rather than sent empty, as
    `getWorkLogHistory` does, and the `?` is left off when nothing is left. */
export const getTimeline = (query: TimelineQuery) => {
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value === null || value === undefined) continue;
    if (Array.isArray(value)) for (const v of value) params.append(name, String(v));
    else params.set(name, String(value));
  }
  const qs = params.toString();
  return fetchJson<Timeline>('/api/reports/timeline' + (qs ? '?' + qs : ''));
};

/** The timeline route's query string; it has no DTO. An unset field is left out
    of the URL, not sent empty. `projectId` repeats once per id when given a list. */
export interface TimelineQuery {
  from?: string | null;
  to?: string | null;
  projectId?: number | number[] | null;
  createdBy?: string | null;
}

/** How many issues sat in each board column over time, read off the same
    status history as `getTimeline`. The route takes no `createdBy`: a column's
    count is a fact about the board, not about any one creator. `project` scopes
    it to a set of projects, repeated once per key.

    Anything null or undefined is left out rather than sent empty, as
    `getTimeline` does. */
export const getCumulativeFlow = (query: CumulativeFlowQuery) => {
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value === null || value === undefined) continue;
    if (Array.isArray(value)) for (const v of value) params.append(name, v);
    else params.set(name, String(value));
  }
  const qs = params.toString();
  return fetchJson<CumulativeFlow>('/api/reports/cumulative-flow' + (qs ? '?' + qs : ''));
};

/** The cumulative-flow route's query string; it has no DTO. An unset field is
    left out of the URL, not sent empty. */
export interface CumulativeFlowQuery {
  from?: string | null;
  to?: string | null;
  /** Narrows the read to these projects' issues; every project when null,
      undefined or empty. */
  project?: string[] | null;
}

/** The board's health over a window, from `GET /api/board/health`. `window` and
    `project` are sent only when set, `project` repeated once per key, so the
    server's own window and every project stand when they are left out. */
export const getBoardHealth = (query: BoardHealthQuery) => {
  const params = new URLSearchParams();
  if (query.window) params.set('window', query.window);
  for (const key of query.project ?? []) params.append('project', key);
  const qs = params.toString();
  return fetchJson<BoardHealth>('/api/board/health' + (qs ? '?' + qs : ''));
};

/** The board health route's query string. */
export interface BoardHealthQuery {
  window?: string | null;
  project?: string[] | null;
}

/** One actor's events over a range. `actor` is always sent, exactly as given
    (`URLSearchParams` does the encoding); `from` and `to` only when set, so the
    server's own fourteen days stand; `projectId` is repeated once per id.
    `offsetMinutes` is always sent, so daily buckets align to the reader's
    midnight and not UTC's; `window` only when set, so the server's own default
    stands; `includeKeys` only when true. */
export const getActivityReport = (query: ActivityReportQuery) => {
  const params = new URLSearchParams();
  params.set('actor', query.actor);
  if (query.from) params.set('from', query.from);
  if (query.to) params.set('to', query.to);
  for (const id of query.projectId ?? []) params.append('projectId', String(id));
  params.set('offsetMinutes', String(query.offsetMinutes));
  if (query.window != null) params.set('window', String(query.window));
  params.set('gap', String(query.gap));
  params.set('lead', String(query.lead));
  if (query.includeKeys === true) params.set('includeKeys', 'true');
  return fetchJson<ActivityReport>('/api/reports/activity?' + params.toString());
};

export interface ActivityReportQuery {
  actor: string;
  from?: string | null;
  to?: string | null;
  projectId?: number[] | null;
  /** Minutes east of UTC, the reader's. */
  offsetMinutes: number;
  /** The rolling mean's width, in buckets; 1..90. */
  window?: number | null;
  /** Minutes between two consecutive events that still counts as one working
      session - always resolved by the caller, as `offsetMinutes` is elsewhere. */
  gap: number;
  /** Minutes added to every session's estimated length - always resolved by
      the caller. */
  lead: number;
  /** Also count what a hand typed under the actor's own keys; sent only when true. */
  includeKeys?: boolean;
}

/** Closed epics bucketed over a window. Each parameter is set only when it
    says something: a null, undefined or empty `from`/`to`/`bucket` is left off
    so the server's own default stands, `projectId` is repeated once per id,
    and `loose` is sent only when true. `level` and `offsetMinutes` are always
    sent - the page's default level is not the API's bare default. */
export const getChangelogReport = (query: ChangelogReportQuery) => {
  const params = new URLSearchParams();
  if (query.from) params.set('from', query.from);
  if (query.to) params.set('to', query.to);
  for (const id of query.projectId ?? []) params.append('projectId', String(id));
  if (query.bucket) params.set('bucket', query.bucket);
  params.set('level', query.level);
  params.set('offsetMinutes', String(query.offsetMinutes));
  if (query.loose === true) params.set('loose', 'true');
  const qs = params.toString();
  return fetchJson<ChangelogReport>('/api/reports/changelog' + (qs ? '?' + qs : ''));
};

/** The changelog report route's query string. `offsetMinutes` is minutes east
    of UTC, the reader's. */
export interface ChangelogReportQuery {
  from?: string | null;
  to?: string | null;
  projectId?: number[] | null;
  bucket?: string | null;
  level: string;
  loose?: boolean | null;
  offsetMinutes: number;
}

/** What each issue of a type consumed over a window. `projectId` and `statusId`
    are repeated once per id; `type` and `sort` are left to the server's default
    when unset, and `limit` and `offsetMinutes` are always sent. */
export const getConsumptionReport = (query: ConsumptionReportQuery) => {
  const params = consumptionFilterQuery(query);
  params.set('limit', String(query.limit));
  const qs = params.toString();
  return fetchJson<ConsumptionReport>('/api/reports/consumption' + (qs ? '?' + qs : ''));
};

/** The consumption report route's query string: `ConsumptionFilterQuery` -
    shared with the CSV download (`lib/consumptionReport.ts`) - plus the row
    cap the CSV has none of. */
export interface ConsumptionReportQuery extends ConsumptionFilterQuery {
  limit: number;
}

export interface WorkLogSessionQuery {
  from: string;
  to: string;
  /** One issue and everything beneath it, that issue included. */
  ancestorKey?: string | null;
  /** Any of these projects; omitted or empty means every project. */
  projectId?: number[] | null;
  sort?: SessionSort;
  /** 1 to 500; the server refuses anything outside it. */
  limit?: number;
  /** One of the claim outcomes, or `none` for sessions with no outcome; left
      out of the URL rather than sent empty when unset. */
  outcome?: string | null;
}

/** One row per playbook/transition/type/model/effort/outcome/reason
    combination in a range, heaviest first - and what each cost.

    Anything null or undefined is left out rather than sent empty, as
    `getWorkLogSessions` does. */
export const getWorkLogByPlaybook = (query: WorkLogByPlaybookQuery) => {
  const { projectId, ...rest } = query;
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(rest)) {
    if (value !== null && value !== undefined) params.set(name, String(value));
  }
  for (const id of projectId ?? []) params.append('projectId', String(id));
  return fetchJson<WorkLogByPlaybook>(`/api/work-log/by-playbook?${params.toString()}`);
};

export interface WorkLogByPlaybookQuery {
  from: string;
  to: string;
  /** One issue and everything beneath it, that issue included. */
  ancestorKey?: string | null;
  /** Any of these projects; omitted or empty means every project. */
  projectId?: number[] | null;
  /** One of the claim outcomes, or `none` for sessions with no outcome; left
      out of the URL rather than sent empty when unset. */
  outcome?: string | null;
}

/** A person's own home screen; a key is refused. Answers the fresh `/api/me`. */
export const putMyHome = (home: 'board' | 'ambitions') =>
  fetchJson<Me>('/api/me/home', { method: 'PUT', ...asJson({ home }) });

// ---- People ----
//
// Admin-only on the server, every one of them; a User's `/users` is not in the
// nav and these answer 403 if it is reached anyway.

export const getPeople = () => fetchJson<Person[]>('/api/people');
export const createPerson = (request: PersonCreateRequest) =>
  fetchJson<Person>('/api/people', { method: 'POST', ...asJson(request) });
export const putPerson = (id: string, request: PersonWriteRequest) =>
  fetchJson<Person>(`/api/people/${seg(id)}`, { method: 'PUT', ...asJson(request) });
export const deletePerson = (id: string) => fetchJson<void>(`/api/people/${seg(id)}`, { method: 'DELETE' });
export const getScopes = () => fetchJson<PersonScope[]>('/api/scopes');
export const putScope = (personId: string, request: PersonScopeWriteRequest) =>
  fetchJson<PersonScope>(`/api/scopes/${seg(personId)}`, { method: 'PUT', ...asJson(request) });
export const getPersonSessions = (id: string) =>
  fetchJson<PersonSession[]>(`/api/people/${seg(id)}/sessions`);
export const revokeGrant = (id: string) => fetchJson<void>(`/api/auth/grants/${seg(id)}`, { method: 'DELETE' });

// ---- Chat ----
//
// Admin-only on the server, and person-only: a key is refused. A bot token goes
// up in a body and is never sent back.

export const getChatWorkspaces = () => fetchJson<ChatWorkspace[]>('/api/chat/workspaces');
export const createChatWorkspace = (token: string) =>
  fetchJson<ChatWorkspace>('/api/chat/workspaces', { method: 'POST', ...asJson({ token }) });
export const replaceChatToken = (id: number, token: string) =>
  fetchJson<ChatWorkspace>(`/api/chat/workspaces/${seg(String(id))}/token`, { method: 'PUT', ...asJson({ token }) });
export const deleteChatWorkspace = (id: number) =>
  fetchJson<void>(`/api/chat/workspaces/${seg(String(id))}`, { method: 'DELETE' });
export const getChatManifest = () => fetchJson<ChatManifest>('/api/chat/manifest');
export const getChatDelivery = () => fetchJson<ChatDelivery>('/api/chat/delivery');
export const setChatProjects = (id: number, projectIds: number[]) =>
  fetchJson<number[]>(`/api/chat/workspaces/${seg(String(id))}/projects`, { method: 'PUT', ...asJson({ projectIds }) });
export const getChatChannels = (id: number) =>
  fetchJson<ChatChannel[]>(`/api/chat/workspaces/${seg(String(id))}/channels`);
export const getChatRooms = (id: number) => fetchJson<ChatRoom[]>(`/api/chat/workspaces/${seg(String(id))}/rooms`);
export const createChatRoom = (id: number, body: ChatRoomCreate) =>
  fetchJson<ChatRoom>(`/api/chat/workspaces/${seg(String(id))}/rooms`, { method: 'POST', ...asJson(body) });
export const updateChatRoom = (roomId: number, patch: ChatRoomPatch) =>
  fetchJson<ChatRoom>(`/api/chat/rooms/${seg(String(roomId))}`, { method: 'PATCH', ...asJson(patch) });
export const deleteChatRoom = (roomId: number) =>
  fetchJson<void>(`/api/chat/rooms/${seg(String(roomId))}`, { method: 'DELETE' });
export const testChatRoom = (roomId: number) =>
  fetchJson<ChatTestResult>(`/api/chat/rooms/${seg(String(roomId))}/test`, { method: 'POST' });

/**
 * The grant this browser holds - how the Users page knows which session row is
 * "this browser". Null when there is no grant (the wall is off), which is a
 * 401 off this one route rather than the refusal every other fetch here
 * means it as: a wall-off deployment never holds a grant at all, so routing
 * that through `fetchJson` would read "no grant" as "signed out" and bounce
 * the browser to the sign-in shell for a caller the wall never asked to sign
 * in to begin with.
 */
export async function getAuthMe(): Promise<AuthMe | null> {
  const res = await fetch('/api/auth/me', { headers: { Accept: 'application/json' } });
  if (res.status === 401) return null;
  if (!res.ok) {
    const text = await res.text();
    throw new HttpError(refusalSentence(text, `GET /api/auth/me failed: ${res.status} ${res.statusText}`), res.status, undefined);
  }
  const text = await res.text();
  return (text ? JSON.parse(text) : null) as AuthMe | null;
}

// ---- Revision ----

/** The one call in this file that reaches outside `/api/*`, deliberately:
    `/api/hatch-revision` sits on AuthGate's allow-list so it answers even to a
    caller the wall has not let in yet, which is exactly the caller that most
    needs to discover it is running a stale build. */
export const getHatchRevision = () => fetchJson<Revision>('/api/hatch-revision');
