import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Button, Card, EmptyState, PageHeader, ProjectMark } from '@hatch/ui';
import { addDream, deleteDream, getDreams, patchDream, weaveDreams } from '../api/client';
import { Choice } from '../components/Choice';
import { DescriptionEditor } from '../components/DescriptionEditor';
import { DreamsOpeningLines } from '../components/DreamsOpeningLines';
import { MarkdownEditor } from '../components/MarkdownEditor';
import { ProjectChooser } from '../components/ProjectChooser';
import { agoPhrase } from '../lib/claim';
import {
  EMPTY_DREAMS,
  WEAVE_MODES,
  countLine,
  deleteDreamQuestion,
  dreamsRequest,
  hasMore,
  isUrl,
  pickedProjectId,
  projectCaption,
  readDreams,
  remandedMore,
  removeDream,
  replaceDream,
  scrubbedDreamProject,
  showsDream,
  weaveProjectDefault,
  weaveRequest,
  weaveSeed,
  writeDreams,
} from '../lib/dreams';
import { message } from '../lib/errors';
import { facetClass } from '../lib/facet';
import { projectLogoUrl } from '../lib/projectLogo';
import { createLedger } from '../lib/refresh';
import { useProjects } from '../lib/useProjects';
import { useScope } from '../lib/useScope';
import type { Dream, Project, WeaveMode } from '../types';
import { ScopeGate } from '../components/ScopeGate';

/** How long the box waits after the last keystroke before it writes `q`. */
const DEBOUNCE_MS = 300;

/**
 * The dreams, newest first - or best match first with `q`; the order is the
 * server's and nothing here reorders it. One project picker above the
 * composer, a search box and a Loose-only switch over a list of dreams. The
 * picked project leans new dreams, homes the weave and narrows the list to its
 * dreams and the ones that lean toward none. Every field is the URL's and
 * nowhere else, so Back undoes a change and pasting the link reproduces it.
 *
 * In a tab scoped to projects the list is those projects' dreams and the
 * loose ones, and every project the page offers is one of them.
 */
export function DreamsPage() {
  return (
    <ScopeGate>
      <DreamsContent />
    </ScopeGate>
  );
}

function DreamsContent() {
  const { ids, projects, isScoped, loaded } = useScope();

  if (!loaded) return <p className="text-muted">Loading…</p>;

  return <DreamsBody ids={ids} projects={projects} isScoped={isScoped} />;
}

function DreamsBody({ ids, projects, isScoped }: { ids: number[]; projects: Project[]; isScoped: boolean }) {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const read = readDreams(params);
  const filters = { ...read, projectId: scrubbedDreamProject(read.projectId, projects, isScoped) };

  // A project in the URL that the scope does not offer reads as none chosen.
  useEffect(() => {
    if (read.projectId !== filters.projectId) setParams(writeDreams(filters), { replace: true });
  }, [read.projectId, filters.projectId]);

  // Only for the row marks: its own fetch, so empty on the first render.
  const { byKey } = useProjects();

  const [draft, setDraft] = useState(filters.q);
  const [lastQ, setLastQ] = useState(filters.q);
  // The URL's q changed from outside (Back, or a link reached from elsewhere)
  // - adjusted during render, the way InlineTitle re-syncs its own draft.
  if (filters.q !== lastQ) {
    setLastQ(filters.q);
    setDraft(filters.q);
  }

  useEffect(() => {
    if (draft === filters.q) return;
    const timer = setTimeout(() => {
      setParams(writeDreams({ ...filters, q: draft }), { replace: true });
    }, DEBOUNCE_MS);
    return () => clearTimeout(timer);
    // filters.q, not filters: a re-render with the same q (loading, projects
    // arriving) must not restart the timer, or a quick re-render could starve it.
  }, [draft, filters.q, setParams]);

  const [dreams, setDreams] = useState<Dream[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ledger] = useState(createLedger);

  const key = dreamsRequest(filters, 0, ids).toString();

  useEffect(() => {
    const ticket = ledger.begin();
    setDreams([]);
    setTotal(0);
    setLoading(true);
    setError(null);

    getDreams(dreamsRequest(filters, 0, ids)).then(
      (result) => {
        if (!ledger.isCurrent(ticket)) return;
        setDreams(result.dreams);
        setTotal(result.total);
        setLoading(false);
      },
      (err: unknown) => {
        if (!ledger.isCurrent(ticket)) return;
        setError(message(err));
        setLoading(false);
      },
    );

    return () => ledger.invalidate();
    // key, not filters: it is the same string for an unchanged query, so a
    // re-render that leaves the query alone does not reload it.
  }, [key]);

  function showMore() {
    const ticket = ledger.begin();
    setLoadingMore(true);
    setError(null);

    getDreams(dreamsRequest(filters, dreams.length, ids)).then(
      (result) => {
        if (!ledger.isCurrent(ticket)) return;
        setDreams((prev) => [...prev, ...result.dreams]);
        setTotal(result.total);
        setLoadingMore(false);
      },
      (err: unknown) => {
        if (!ledger.isCurrent(ticket)) return;
        setError(message(err));
        setLoadingMore(false);
      },
    );
  }

  const [body, setBody] = useState('');
  const [source, setSource] = useState('');
  const [posting, setPosting] = useState(false);
  const [postError, setPostError] = useState<string | null>(null);

  async function post() {
    // The editor's own Ctrl+Enter can fire on an empty box, or twice.
    if (!body.trim() || posting) return;
    setPosting(true);
    setPostError(null);

    try {
      const dream = await addDream({ body, source: source.trim() || null, projectId: pickedProjectId(filters.projectId, projects) });
      setBody('');
      setSource('');

      if (showsDream(filters, dream)) {
        setDreams((prev) => [dream, ...prev]);
        setTotal((t) => t + 1);
      } else {
        // The filters would hide it: clear them but the picked project, and
        // the key effect reloads with the new dream first. Prepending a dream
        // the server would not list under them would also push every Show more
        // offset one too far.
        setParams(writeDreams({ ...EMPTY_DREAMS, projectId: filters.projectId }));
      }
    } catch (err) {
      // The draft stays as typed, so a refusal costs nothing to retry.
      setPostError(message(err));
    } finally {
      setPosting(false);
    }
  }

  const [weaveMode, setWeaveMode] = useState<WeaveMode>(WEAVE_MODES[0]);
  const [weaveSeedText, setWeaveSeedText] = useState<string | null>(null);
  const [weaveOnScreen, setWeaveOnScreen] = useState(false);
  const [pressing, setPressing] = useState(false);
  const [weaveError, setWeaveError] = useState<string | null>(null);

  const weaveProject = weaveProjectDefault(filters.projectId, projects);

  const weave = weaveRequest({
    mode: weaveMode,
    seed: weaveSeedText,
    q: draft,
    projectId: weaveProject,
    useOnScreen: weaveOnScreen,
    dreams,
  });

  async function press() {
    if (weave === null || pressing) return;
    setPressing(true);
    setWeaveError(null);

    try {
      const epic = await weaveDreams(weave);
      navigate(`/issues/${epic.key}`);
    } catch (err) {
      setWeaveError(message(err));
      setPressing(false);
    }
  }

  const onChanged = (updated: Dream) => setDreams((prev) => replaceDream(prev, updated));

  // Show more's offset is dreams.length, so the row and the total drop together.
  const onRemoved = (id: number) => {
    setDreams((prev) => removeDream(prev, id));
    setTotal((t) => t - 1);
  };

  // Once per render of the list, so every row's "ago" reads from the same instant.
  const now = new Date();

  return (
    <div className="hatch-page">
      <div className="hatch-dream-intro">
        <PageHeader title="Dreams" />
        <DreamsOpeningLines />
      </div>

      {projects.length > 0 && (
        <div className="hatch-dream-project">
          <ProjectChooser
            projects={projects}
            value={pickedProjectId(filters.projectId, projects)}
            onChange={(id) => setParams(writeDreams({ ...filters, projectId: String(id) }))}
            legend="Project"
            keepLabel="No project"
            onKeep={() => setParams(writeDreams({ ...filters, projectId: '' }))}
          />
          <p className="hatch-dream-project__caption">{projectCaption(filters.projectId, projects)}</p>
        </div>
      )}

      <Card className="hatch-dream-composer">
        <div className="hatch-dream-composer__box">
          <MarkdownEditor
            value={body}
            onChange={setBody}
            rows={3}
            deferred
            ariaLabel="Dream"
            placeholder="A line worth keeping…"
            onCtrlEnter={() => void post()}
          />
          {postError && <p className="text-danger">{postError}</p>}
        </div>

        <input
          type="text"
          className="hatch-dream-composer__source"
          aria-label="Source"
          placeholder="Where it came from (optional)"
          value={source}
          onChange={(e) => setSource(e.target.value)}
        />

        <div className="hatch-dream-composer__actions">
          <Button variant="primary" loading={posting} disabled={!body.trim()} onClick={() => void post()}>
            Post
          </Button>
        </div>
      </Card>

      <details className="hatch-dream-weave">
        <summary>Weave an epic</summary>

        <Choice label="Mode" value={weaveMode} options={WEAVE_MODES} onChange={(value) => setWeaveMode(value as WeaveMode)} />

        {weaveMode === 'seed' && (
          <input
            type="text"
            aria-label="Seed"
            value={weaveSeed(weaveSeedText, draft)}
            onChange={(e) => setWeaveSeedText(e.target.value)}
          />
        )}

        <label>
          <input type="checkbox" checked={weaveOnScreen} onChange={(e) => setWeaveOnScreen(e.target.checked)} />
          Use the dreams on screen
        </label>

        <div className="hatch-dream-weave__actions">
          <Button variant="primary" loading={pressing} disabled={weave === null} onClick={() => void press()}>
            Weave
          </Button>
          {weaveProject === null && (
            <p className="text-muted">
              {projects.length === 0 ? 'There is no project to weave into yet.' : 'Pick a project above to weave.'}
            </p>
          )}
          {weaveError && <p className="text-danger">{weaveError}</p>}
        </div>
      </details>

      <div className="hatch-board-filters hatch-search-filters">
        {/* type="search" on purpose; base.css says why. */}
        <input
          type="search"
          className={`hatch-board-search hatch-search-box${draft.trim() !== '' ? ' hatch-board-search--lit' : ''}`}
          aria-label="Search dreams"
          placeholder="Search dreams…"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
        />

        <button
          type="button"
          className={facetClass(filters.loose)}
          aria-pressed={filters.loose}
          title="Dreams no issue has drawn on"
          onClick={() => setParams(writeDreams({ ...filters, loose: !filters.loose }))}
        >
          Loose only
        </button>

        {filters.issueKey !== '' && (
          <button
            type="button"
            className={facetClass(true)}
            title="Show dreams no matter which issue drew on them"
            onClick={() => setParams(writeDreams({ ...filters, issueKey: '' }))}
          >
            Drew on {filters.issueKey} ×
          </button>
        )}

        {filters.remandedFrom !== '' && (
          <button
            type="button"
            className={facetClass(true)}
            title="Show dreams no matter which issue they were remanded from"
            onClick={() => setParams(writeDreams({ ...filters, remandedFrom: '' }))}
          >
            Remanded from {filters.remandedFrom} ×
          </button>
        )}
      </div>

      {loading && <p className="text-muted">Loading…</p>}

      {!loading && error && dreams.length === 0 && <p className="text-danger">{error}</p>}

      {!loading && !error && dreams.length === 0 && (
        <EmptyState message={writeDreams(filters).size === 0 ? 'No dreams yet.' : 'Nothing matches.'} />
      )}

      {!loading && dreams.length > 0 && (
        <>
          {error && <p className="text-danger">{error}</p>}

          <p className="text-muted" aria-live="polite">
            {countLine(dreams.length, total)}
          </p>

          <ul className="hatch-dream-list">
            {dreams.map((dream) => (
              <DreamRow
                key={dream.id}
                dream={dream}
                now={now}
                project={dream.projectKey ? byKey.get(dream.projectKey) : undefined}
                onChanged={onChanged}
                onRemoved={onRemoved}
              />
            ))}
          </ul>

          {hasMore(dreams.length, total) && (
            <div className="hatch-form-actions">
              <Button onClick={showMore} loading={loadingMore}>
                Show more
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

/** One dream: who said it and when, where it came from, the project it leans
    toward, its words, and the issues that drew on it. The words are editable in
    place and the row can be deleted; the draft and the refusal are the row's own,
    so one row's edit never touches another's. */
function DreamRow({
  dream,
  now,
  project,
  onChanged,
  onRemoved,
}: {
  dream: Dream;
  now: Date;
  project: Project | undefined;
  onChanged: (updated: Dream) => void;
  onRemoved: (id: number) => void;
}) {
  const source = dream.source;
  const [error, setError] = useState<string | null>(null);
  // Remounts the editor after each save. The server trims the body it stores,
  // so a draft saved with trailing whitespace would otherwise differ from the
  // value that comes back and read as "changed while this was being edited".
  const [saves, setSaves] = useState(0);

  async function remove() {
    // Deleting a dream also drops it from the "Drew on" of every issue woven
    // into; the issues it was remanded from are untouched either way.
    if (!confirm(deleteDreamQuestion(dream))) return;
    setError(null);
    try {
      await deleteDream(dream.id);
      onRemoved(dream.id);
    } catch (err) {
      setError(message(err));
    }
  }

  return (
    <li className="hatch-dream">
      <div className="hatch-dream__head text-muted">
        <span>{dream.author}</span>
        <span title={new Date(dream.createdAt).toLocaleString()}>{agoPhrase(dream.createdAt, now)}</span>
        {source &&
          (isUrl(source) ? (
            <a href={source} target="_blank" rel="noreferrer">
              {source}
            </a>
          ) : (
            <span>{source}</span>
          ))}
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
        <span className="hatch-dream__delete">
          <Button variant="danger" onClick={() => void remove()}>
            Delete
          </Button>
        </span>
      </div>

      <div className="hatch-dream__body">
        <DescriptionEditor
          key={saves}
          title={null}
          value={dream.body}
          rows={3}
          noun="dream"
          error={error}
          onSave={async (next) => {
            setError(null);
            try {
              onChanged(await patchDream(dream.id, { body: next }));
              setSaves((n) => n + 1);
              return true;
            } catch (err) {
              setError(message(err));
              return false;
            }
          }}
        />
      </div>

      {(dream.wovenInto.length > 0 || dream.remandedFrom.length > 0) && (
        <div className="hatch-dream__woven">
          {dream.wovenInto.map((k) => (
            <Link key={k} className="hatch-chip" to={`/issues/${k}`}>
              {k}
            </Link>
          ))}

          {dream.remandedFrom.length > 0 && (
            <span className="hatch-chip">
              remanded from <Link to={`/issues/${dream.remandedFrom[0]}`}>{dream.remandedFrom[0]}</Link>
              {remandedMore(dream.remandedFrom) && ` ${remandedMore(dream.remandedFrom)}`}
            </span>
          )}
        </div>
      )}
    </li>
  );
}
