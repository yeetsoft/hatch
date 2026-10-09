import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { IssueSection } from './IssueSection';

/* No DOM here, same reasoning as IssuePeek.test.tsx: a static render pins
   what reaches the markup, not what a click does - that is the operator's
   browser. */
afterEach(() => vi.unstubAllGlobals());

function stubStorage() {
  vi.stubGlobal('window', {
    localStorage: {
      getItem: () => null,
      setItem: () => undefined,
    },
  });
}

describe('IssueSection', () => {
  it('puts the heading inside the summary', () => {
    stubStorage();

    const html = renderToStaticMarkup(
      <IssueSection id="history" title="History" defaultOpen={false}>
        <p>body</p>
      </IssueSection>,
    );

    expect(html).toMatch(/<summary[^>]*>History<\/summary>/);
  });

  it('reflects defaultOpen on the details element', () => {
    stubStorage();

    const openHtml = renderToStaticMarkup(
      <IssueSection id="comments" title="Comments" defaultOpen>
        <p>body</p>
      </IssueSection>,
    );
    const closedHtml = renderToStaticMarkup(
      <IssueSection id="history" title="History" defaultOpen={false}>
        <p>body</p>
      </IssueSection>,
    );

    expect(openHtml).toContain('<details class="hatch-issue-section" open=""');
    expect(closedHtml).toContain('<details class="hatch-issue-section">');
  });

  it('draws a count in parens when one is given, and nothing when it is not', () => {
    stubStorage();

    const withCount = renderToStaticMarkup(
      <IssueSection id="comments" title="Comments" count={3}>
        <p>body</p>
      </IssueSection>,
    );
    const withoutCount = renderToStaticMarkup(
      <IssueSection id="description" title="Description">
        <p>body</p>
      </IssueSection>,
    );

    expect(withCount).toContain('Comments (3)');
    expect(withoutCount).toContain('Description</summary>');
  });
});
