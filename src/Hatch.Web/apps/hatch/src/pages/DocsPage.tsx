import { useEffect, useMemo, useRef, type MouseEvent } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import DOMPurify from 'dompurify';
import { PageHeader, useTheme } from '@hatch/ui';
import {
  CHANGELOG_LINK,
  DOC_PAGES,
  docLinkTarget,
  docNeighbours,
  docPage,
  docSections,
  hashId,
  isPlainPrimaryClick,
  pageForAnchor,
  renderDocs,
  type DocPage,
  type OutlineEntry,
} from '../lib/docs';
import { usePhone } from '../lib/viewport';
import { clientLogger } from '../lib/clientLogger';
import { DocsSetupPrompt } from '../components/SetupPromptBox';
import { DocsSidebar } from '../components/DocsSidebar';

type MermaidModule = typeof import('../lib/mermaid');

// Kept once it has arrived, the same shape as MarkdownEditor.tsx's Monaco
// loader - asked for once, kept for the next page or pass, cleared on
// rejection so a failed fetch is retryable (Chrome never retries a failed
// dynamic import in place).
let loadedMermaid: MermaidModule | null = null;
let loadingMermaid: Promise<MermaidModule> | null = null;

function loadMermaid(): Promise<MermaidModule> {
  if (loadedMermaid) return Promise.resolve(loadedMermaid);
  loadingMermaid ??= import('../lib/mermaid').then((m) => (loadedMermaid = m));
  loadingMermaid.catch(() => (loadingMermaid = null));
  return loadingMermaid;
}

/** The one line a reader sees above a diagram's source when it could not be
    drawn - mermaid refused it, or the chunk never arrived. Created once per
    container and reused, so a second failed pass does not stack a second
    line. */
function showMermaidError(container: Element, source: HTMLElement, message: string): void {
  source.hidden = false;
  container.querySelector('svg')?.remove();
  let error = container.querySelector<HTMLParagraphElement>('.hatch-docs-mermaid-error');
  if (!error) {
    error = document.createElement('p');
    error.className = 'hatch-docs-mermaid-error';
    container.insertBefore(error, source);
  }
  error.textContent = message;
}

/** Writes a drawn diagram's sanitised SVG markup into the container, beside
    the (now hidden) source `<pre>`, replacing a previous draw's SVG rather
    than leaving two behind. */
function showMermaidSvg(container: Element, source: HTMLElement, svgMarkup: string): void {
  const wrapper = document.createElement('div');
  wrapper.innerHTML = svgMarkup;
  const svg = wrapper.firstElementChild;
  if (!svg) return;
  const existing = container.querySelector('svg');
  if (existing) existing.replaceWith(svg);
  else container.appendChild(svg);
  container.querySelector('.hatch-docs-mermaid-error')?.remove();
  source.hidden = true;
}

interface RenderedDoc {
  html: string;
  outline: OutlineEntry[];
  mermaidCount: number;
}

// Filled lazily, the first time each slug is visited - DOC_PAGES's pages are
// cheap to render, but there is no reason to pay for all of them before a
// reader has asked for any.
const rendered = new Map<string, RenderedDoc>();

function renderedDoc(page: DocPage): RenderedDoc {
  let doc = rendered.get(page.slug);
  if (!doc) {
    const { html, outline, mermaidCount } = renderDocs(page.body);
    // DOMPurify's default SANITIZE_DOM silently drops an id whose value names a
    // property of document or of a form (title, links, images, forms, body,
    // location...), so a heading titled "Links" would lose its anchor. The content
    // here is shipped in the repo, not typed by a user, so the clobbering guard
    // protects nothing and costs anchors.
    doc = { html: DOMPurify.sanitize(html, { SANITIZE_DOM: false }), outline, mermaidCount };
    rendered.set(page.slug, doc);
  }
  return doc;
}

function DocsLanding() {
  const { hash } = useLocation();
  const navigate = useNavigate();

  // An old /docs#<id> link, from before the page was split: carries the reader
  // on to wherever that id lives now. replace: true so Back does not return to
  // a bare landing that bounces forward again.
  useEffect(() => {
    const to = pageForAnchor(DOC_PAGES, hashId(hash));
    if (to) navigate(to, { replace: true });
  }, [hash, navigate]);

  return (
    <div className="hatch-page">
      <PageHeader title="Docs" />
      {/* First under the title, because bringing somebody on is the thing a reader
          of the landing is most likely to want to pass along. Not repeated on every
          page below - that would be an aside shown sixteen times over. */}
      <DocsSetupPrompt />
      <div className="hatch-docs">
        <DocsSidebar />
        <div className="hatch-docs-landing">
          {docSections(DOC_PAGES).map(({ section, pages }) => (
            <section key={section}>
              <h2>{section}</h2>
              <ul className="hatch-docs-landing-list">
                {section === 'Reference' && (
                  <li>
                    <Link to={CHANGELOG_LINK.to}>{CHANGELOG_LINK.title}</Link>
                  </li>
                )}
                {pages.map((page) => (
                  <li key={page.slug}>
                    <Link to={`/docs/${page.slug}`}>{page.title}</Link>
                    <p className="hatch-docs-landing-summary">{page.summary}</p>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      </div>
    </div>
  );
}

function DocsNotFound({ path }: { path: string }) {
  return (
    <div className="hatch-page">
      <div className="hatch-docs">
        <DocsSidebar />
        <p className="hatch-docs-missing">
          There&rsquo;s no docs page at <code>/docs/{path}</code>. <Link to="/docs">Back to Docs</Link>
        </p>
      </div>
    </div>
  );
}

function DocsArticle({ page }: { page: DocPage }) {
  const isPhone = usePhone();
  const { pathname, hash } = useLocation();
  const navigate = useNavigate();
  const pageRef = useRef<HTMLDivElement>(null);
  const doc = useMemo(() => renderedDoc(page), [page]);
  const { previous, next } = docNeighbours(DOC_PAGES, page.slug);
  const { resolved } = useTheme();

  // On a fresh load, on a hash change, and on moving from one docs page to another
  // - Back included. The route element is the same for every docs page, so
  // .hatch-page stays mounted and keeps its scrollTop across the move; without this
  // a page opened from another would open part-way down. scrollIntoView moves
  // whichever ancestor scrolls: .hatch-page on the desk, the document on a phone.
  useEffect(() => {
    const id = hashId(hash);
    const target = id ? document.getElementById(id) : pageRef.current?.querySelector('.hatch-page-header');
    target?.scrollIntoView();
  }, [pathname, hash]);

  // Draws (and, on a theme switch, redraws) every mermaid placeholder in the
  // article. A page with none asks for nothing - doc.mermaidCount gates the
  // import itself, not just the drawing, so mermaid's chunk is never fetched
  // by a route that has no diagram. The source <pre> is never removed, only
  // hidden, so a theme-only pass can read it again.
  useEffect(() => {
    if (doc.mermaidCount === 0) return;
    const containers = Array.from(pageRef.current?.querySelectorAll<HTMLElement>('.hatch-docs-mermaid') ?? []);
    if (containers.length === 0) return;
    let live = true;

    const fail = (message: string) => {
      for (const container of containers) {
        const source = container.querySelector<HTMLElement>('.hatch-docs-mermaid-source');
        if (source) showMermaidError(container, source, message);
      }
    };

    loadMermaid().then(
      (mermaid) => {
        if (!live) return;
        mermaid.applyMermaidTheme();
        for (const container of containers) {
          const source = container.querySelector<HTMLElement>('.hatch-docs-mermaid-source');
          if (!source) continue;
          mermaid.renderMermaidDiagram(source.textContent ?? '').then(
            (svg) => {
              if (live) showMermaidSvg(container, source, DOMPurify.sanitize(svg));
            },
            (e: unknown) => {
              clientLogger.error('A mermaid diagram failed to draw', { message: String(e) });
              if (live) showMermaidError(container, source, 'This diagram could not be drawn.');
            },
          );
        }
      },
      (e: unknown) => {
        clientLogger.error('The mermaid chunk failed to load', { message: String(e) });
        if (live) fail('This diagram could not be drawn.');
      },
    );

    return () => {
      live = false;
    };
  }, [doc, resolved]);

  // One handler for the whole article. A link into the app goes through the router
  // (no page load, either address); fragments and external links stay the browser's.
  const onBodyClick = (e: MouseEvent<HTMLElement>) => {
    const a = (e.target as Element).closest?.('a');
    if (!a || !e.currentTarget.contains(a) || !isPlainPrimaryClick(e)) return;
    if ((a.target && a.target !== '_self') || a.hasAttribute('download')) return;
    const t = docLinkTarget(a.getAttribute('href'));
    if (t.kind !== 'app') return;
    e.preventDefault();
    navigate(t.to);
  };

  const list = (
    <ol className="hatch-docs-outline-list">
      {doc.outline.map((entry) => (
        <li key={entry.id} className={`hatch-docs-outline-item hatch-docs-outline-item--d${entry.depth}`}>
          <a href={'#' + entry.id}>{entry.text}</a>
        </li>
      ))}
    </ol>
  );

  return (
    <div className="hatch-page" ref={pageRef}>
      <PageHeader title={page.title} />
      {/* A direct child of .hatch-page, which caps its children at --measure. */}
      <div className="hatch-docs">
        <DocsSidebar />
        {/* Plain anchors, not <Link>: a router link would turn a fragment into a
            route change. */}
        {isPhone ? (
          <details className="hatch-docs-outline">
            <summary>On this page</summary>
            <nav aria-label="On this page">{list}</nav>
          </details>
        ) : (
          <nav className="hatch-docs-outline" aria-label="On this page">
            <p className="hatch-docs-outline-title">On this page</p>
            {list}
          </nav>
        )}
        <article className="hatch-docs-body" onClick={onBodyClick} dangerouslySetInnerHTML={{ __html: doc.html }} />
        <nav className="hatch-docs-pager" aria-label="Previous and next">
          {previous && (
            <Link className="hatch-docs-pager-link hatch-docs-pager-previous" to={`/docs/${previous.slug}`}>
              <span className="hatch-docs-pager-label">Previous</span>
              <span className="hatch-docs-pager-title">{previous.title}</span>
            </Link>
          )}
          {next && (
            <Link className="hatch-docs-pager-link hatch-docs-pager-next" to={`/docs/${next.slug}`}>
              <span className="hatch-docs-pager-label">Next</span>
              <span className="hatch-docs-pager-title">{next.title}</span>
            </Link>
          )}
        </nav>
      </div>
    </div>
  );
}

/** The docs frame: the landing at `/docs`, one page per slug in DOC_PAGES, and a
    not-found state for anything else. */
export function DocsPage() {
  const { '*': rest = '' } = useParams();
  if (rest === '') return <DocsLanding />;
  const page = docPage(DOC_PAGES, rest);
  if (!page) return <DocsNotFound path={rest} />;
  return <DocsArticle page={page} />;
}
