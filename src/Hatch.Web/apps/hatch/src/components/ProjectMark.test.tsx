/* <ProjectMark> and PROJECT_ICONS live in @hatch/ui (packages/ui/src), not in
   this app - the test stays here for the same reason tokens.test.ts and
   color.test.ts do: packages/ui has no test runner and make test-web only
   loops over the apps. */
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PROJECT_ICONS, ProjectMark } from '@hatch/ui';

describe('ProjectMark', () => {
  it('draws the letters when there is neither a logo nor an icon', () => {
    const html = renderToStaticMarkup(<ProjectMark letters="HA" title="Hatch" />);
    expect(html).toContain('>HA<');
    expect(html).not.toContain('<img');
    expect(html).not.toContain('<svg');
  });

  it('draws the icon over the letters when both are given', () => {
    const html = renderToStaticMarkup(<ProjectMark letters="HA" icon="rocket" title="Hatch" />);
    expect(html).toContain('<svg');
    expect(html).not.toContain('>HA<');
  });

  it('draws the logo over the icon and the letters when all three are given', () => {
    const html = renderToStaticMarkup(
      <ProjectMark letters="HA" icon="rocket" logoUrl="https://example.invalid/logo.png" title="Hatch" />,
    );
    expect(html).toContain('<img');
    expect(html).not.toContain('<svg');
    expect(html).not.toContain('>HA<');
  });

  /* The set can grow, or an install can be mid-upgrade to a newer one, so a
     slug this build does not recognise must not leave the mark blank. */
  it('falls back to the letters on a slug the stock set does not recognise', () => {
    const html = renderToStaticMarkup(<ProjectMark letters="HA" icon="a-slug-nobody-shipped" title="Hatch" />);
    expect(html).toContain('>HA<');
    expect(html).not.toContain('<svg');
  });
});

describe('PROJECT_ICONS', () => {
  /* The server's own rule (EfHatchProject.IconPattern), checked here so a
     slug that would be rejected the moment a project tried to save it is
     caught before it ships. */
  const SLUG_PATTERN = /^[a-z0-9-]{1,40}$/;

  it('is well-formed: every slug matches the pattern the server validates against', () => {
    for (const { slug } of PROJECT_ICONS) {
      expect(slug, `"${slug}" does not match ${SLUG_PATTERN}`).toMatch(SLUG_PATTERN);
    }
  });

  it('has no duplicate slugs', () => {
    const slugs = PROJECT_ICONS.map((icon) => icon.slug);
    expect(new Set(slugs).size).toBe(slugs.length);
  });

  it('gives every icon a title', () => {
    for (const { title } of PROJECT_ICONS) {
      expect(title.length).toBeGreaterThan(0);
    }
  });

  it('is roughly the thirty-two the brief asks for', () => {
    expect(PROJECT_ICONS.length).toBeGreaterThanOrEqual(30);
  });
});
