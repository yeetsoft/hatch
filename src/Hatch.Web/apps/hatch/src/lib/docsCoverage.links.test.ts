import { describe, expect, it } from 'vitest';
import { DOC_PAGES, docPage, hashId, renderDocs } from './docs';

/** The ids of every heading on every page, rendered once: a link to a page is
    checked against the target's ids, so each page is read for them a single time. */
const idsBySlug = new Map<string, Set<string>>(
  DOC_PAGES.map((page) => [page.slug, new Set(renderDocs(page.body).ids)]),
);

/** What each page links to. Page-to-page links reach the resolver; a link to a
    heading on the same page does not, so those are read back out of the HTML,
    where marked writes a link's `href` first and the writer's own `#` heading
    anchors (`class` first) are left alone. */
const links = DOC_PAGES.map((page) => {
  const resolved: { slug: string; fragment: string }[] = [];
  const { html, outline } = renderDocs(page.body, (slug, fragment) => {
    resolved.push({ slug, fragment });
    return '';
  });
  const samePage = [...html.matchAll(/<a href="#([^"]*)"/g)].map((m) => hashId(m[1]));
  return { page, resolved, samePage, outline };
});

describe('links between docs pages', () => {
  it.each(links.map((l) => [l.page.slug, l] as const))('%s links only to docs pages', (_, { page, resolved }) => {
    for (const { slug } of resolved) {
      expect(docPage(DOC_PAGES, slug), `${page.slug}.md links to ${slug}, which is not a docs page`).toBeDefined();
    }
  });

  it.each(links.map((l) => [l.page.slug, l] as const))('%s links only to headings that exist', (_, { page, resolved }) => {
    for (const { slug, fragment } of resolved) {
      const ids = idsBySlug.get(slug);
      if (!fragment || !ids) continue;
      expect(
        ids.has(hashId(fragment)),
        `${page.slug}.md links to ${slug}${fragment}, which is no heading on ${slug}.md`,
      ).toBe(true);
    }
  });

  it.each(links.map((l) => [l.page.slug, l] as const))('%s same-page links land', (_, { page, samePage }) => {
    const ids = idsBySlug.get(page.slug)!;
    for (const id of samePage) {
      expect(ids.has(id), `${page.slug}.md links to #${id}, which is no heading on this page`).toBe(true);
    }
  });

  it.each(links.map((l) => [l.page.slug, l] as const))('%s has no Coming heading', (_, { page, outline }) => {
    expect(
      outline.some((o) => o.text === 'Coming'),
      `${page.slug}.md ends in a Coming list: link the pages it names and delete the section`,
    ).toBe(false);
  });
});
