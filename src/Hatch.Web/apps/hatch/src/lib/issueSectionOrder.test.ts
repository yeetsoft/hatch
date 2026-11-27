/* HA-328 §1: the child-issues card reads above the dependency chain, and the
   dependency chain above the description - an issue's children are the work,
   so "what is this made of" comes before "what is it waiting on" and before
   the prose. Read as text, the way indexHtml.test.ts reads index.html and
   touchCss.test.ts reads App.css - there is no DOM in this runner to render
   IssuePage into. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const page = readFileSync(new URL('../pages/IssuePage.tsx', import.meta.url), 'utf8');

describe('IssuePage section order', () => {
  it('draws child issues above dependencies above the description', () => {
    expect(page.indexOf('<Progress')).toBeLessThan(page.indexOf('<Dependencies'));
    // lastIndexOf, not indexOf: an ambition's own <Description, gated
    // `issue.type === 'ambition'`, sits above Capabilities and comes first in
    // the file - this is every other type's, gated `issue.type !== 'ambition'`.
    expect(page.indexOf('<Dependencies')).toBeLessThan(page.lastIndexOf('<Description'));
  });

  it('for an ambition, draws the header above its own Description above Capabilities', () => {
    expect(page.indexOf('<AmbitionHeader')).toBeLessThan(page.indexOf('<Description'));
    expect(page.indexOf('<Description')).toBeLessThan(page.indexOf('<Capabilities'));
  });
});
