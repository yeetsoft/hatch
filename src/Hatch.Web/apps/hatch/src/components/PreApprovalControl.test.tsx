import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PreApprovalControl } from './PreApprovalControl';
import type { AssigneeDirectory } from '../types';

const directory = (kind: 'person' | 'key' = 'person'): AssigneeDirectory => ({
  me: { kind, id: 'ada', name: 'Ada' },
  assignees: [],
});

describe('PreApprovalControl', () => {
  it('draws the pill for an issue holding its own express level, closed', () => {
    const html = renderToStaticMarkup(
      <PreApprovalControl
        issueKey="HA-14"
        preApproval="express"
        preApprovalOwn="express"
        preApprovalFrom={null}
        directory={directory()}
        canApprove={false}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('Express');
    expect(html).toContain('aria-label="Pre-approval: Express. Set HA-14&#x27;s pre-approval"');
    expect(html).toContain('title="Pre-approval: Express. Set HA-14&#x27;s pre-approval"');
    expect(html).toContain('aria-expanded="false"');
    expect(html).not.toContain('inherited from');
  });

  it('draws the pill for an issue holding its own preapproved level, closed', () => {
    const html = renderToStaticMarkup(
      <PreApprovalControl
        issueKey="HA-14"
        preApproval="preapproved"
        preApprovalOwn="preapproved"
        preApprovalFrom={null}
        directory={directory()}
        canApprove
        onChange={() => {}}
      />,
    );

    expect(html).toContain('Pre-approved');
    expect(html).toContain('✓');
    expect(html).toContain('aria-label="Pre-approval: Pre-approved. Set HA-14&#x27;s pre-approval"');
  });

  it('draws the pill for an issue holding its own none level, closed', () => {
    const html = renderToStaticMarkup(
      <PreApprovalControl
        issueKey="HA-14"
        preApproval="none"
        preApprovalOwn="none"
        preApprovalFrom={null}
        directory={directory()}
        canApprove={false}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('None');
    expect(html).toContain('aria-label="Pre-approval: None. Set HA-14&#x27;s pre-approval"');
  });

  it('names the ancestor on a level this issue inherits', () => {
    const html = renderToStaticMarkup(
      <PreApprovalControl
        issueKey="HA-14"
        preApproval="express"
        preApprovalOwn="none"
        preApprovalFrom="HA-12"
        directory={directory()}
        canApprove={false}
        onChange={() => {}}
      />,
    );

    expect(html).toContain(
      'aria-label="Pre-approval: Express, inherited from HA-12. Set HA-14&#x27;s pre-approval"',
    );
  });

  it('is busy and disabled while a press is out', () => {
    const html = renderToStaticMarkup(
      <PreApprovalControl
        issueKey="HA-14"
        preApproval="none"
        preApprovalOwn="none"
        preApprovalFrom={null}
        directory={directory()}
        canApprove={false}
        busy
        onChange={() => {}}
      />,
    );

    expect(html).toContain('aria-busy="true"');
    expect(html).toContain('disabled=""');
  });

  it('draws the word and the ancestor key for a key, with no button', () => {
    const html = renderToStaticMarkup(
      <PreApprovalControl
        issueKey="HA-14"
        preApproval="preapproved"
        preApprovalOwn="none"
        preApprovalFrom="HA-12"
        directory={directory('key')}
        canApprove={false}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('Pre-approved');
    expect(html).toContain('inherited from HA-12');
    expect(html).not.toContain('<button');
  });

  it('draws the word alone for a key holding its own level', () => {
    const html = renderToStaticMarkup(
      <PreApprovalControl
        issueKey="HA-14"
        preApproval="none"
        preApprovalOwn="none"
        preApprovalFrom={null}
        directory={directory('key')}
        canApprove={false}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('None');
    expect(html).not.toContain('inherited from');
    expect(html).not.toContain('<button');
  });

  it('draws the preapproved glyph and word on the pill', () => {
    const html = renderToStaticMarkup(
      <PreApprovalControl
        issueKey="HA-14"
        preApproval="preapproved"
        preApprovalOwn="preapproved"
        preApprovalFrom={null}
        directory={directory()}
        canApprove
        onChange={() => {}}
      />,
    );

    expect(html).toContain('✓');
    expect(html).toContain('Pre-approved');
  });
});
