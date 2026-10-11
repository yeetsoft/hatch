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
      <TrunkBuildBugDialog open={true} buildId={9} onClose={() => undefined} onFiled={() => undefined} />
    </IssueConfirmationsContext.Provider>,
  );
}

describe('TrunkBuildBugDialog', () => {
  it('opens with express on and priority expedited', () => {
    const html = render();

    expect(html).toContain('hatch-express-said on');
    expect(html).toContain('Expedited');
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
