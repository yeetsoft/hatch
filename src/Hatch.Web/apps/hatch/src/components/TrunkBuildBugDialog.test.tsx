import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { TrunkBuildBugDialog } from './TrunkBuildBugDialog';
import { IssueConfirmationsContext, type IssueConfirmations } from '../lib/useIssueConfirmations';

// TrunkBuildBugDialog confirms a filing through this context - a renderless
// stub, never exercised by the opening-render assertions below.
const CONFIRMATIONS: IssueConfirmations = {
  confirm: () => undefined,
  moved: () => undefined,
  cascaded: () => undefined,
  undo: () => undefined,
  undoNewest: () => false,
  onUndone: () => () => undefined,
};

function render() {
  return renderToStaticMarkup(
    <IssueConfirmationsContext.Provider value={CONFIRMATIONS}>
      <TrunkBuildBugDialog open={true} buildId={9} projectKey="HA" onClose={() => undefined} onFiled={() => undefined} />
    </IssueConfirmationsContext.Provider>,
  );
}

describe('TrunkBuildBugDialog', () => {
  it('opens with Priority and Pre-approval on one row, on Expedited and Express', () => {
    const html = render();

    expect(html).toContain('hatch-field-pair');
    expect(html).toContain('Priority');
    expect(html).toContain('Pre-approval');
    expect(html).toContain('Expedited');
    expect(html).toContain('Express');
  });

  it('offers Cancel and File it as two independent buttons', () => {
    const html = render();

    const cancel = /<button[^>]*>Cancel<\/button>/.exec(html)![0];
    const fileIt = /<button[^>]*>File it<\/button>/.exec(html)![0];

    expect(cancel).not.toContain('File it');
    expect(fileIt).not.toContain('Cancel');
  });

  it('starts with File it disabled, before the column fetch resolves', () => {
    const html = render();

    const fileIt = /<button[^>]*>File it<\/button>/.exec(html)![0];
    expect(fileIt).toContain('disabled=""');
  });
});
