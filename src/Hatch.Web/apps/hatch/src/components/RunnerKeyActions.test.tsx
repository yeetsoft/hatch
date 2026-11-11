import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { RevokeRunnerBody, RotateRunnerBody, RunnerKeyActions } from './RunnerKeyActions';
import type { Runner } from '../types';

// The clicks themselves (opening a modal, saving the owner) are checked in
// the browser driver per the brief - the app has no jsdom or
// testing-library, and a closed Modal renders null. This file proves the
// static shape: the owner select, the two buttons, and each modal's body in
// isolation.

const runner = (over: Partial<Runner> = {}): Runner =>
  ({
    name: 'Quill',
    ownerPersonId: null,
    ...over,
  }) as Runner;

describe('RunnerKeyActions', () => {
  it('draws the owner select, Rotate key and Revoke', () => {
    const html = renderToStaticMarkup(
      <RunnerKeyActions runner={runner()} onSecret={() => {}} reload={async () => {}} />,
    );
    expect(html).toContain('aria-label="Owner of Quill"');
    expect(html).toContain('>Rotate key</button>');
    expect(html).toContain('>Revoke</button>');
  });

  it('marks nobody selected when the runner has no owner', () => {
    const html = renderToStaticMarkup(
      <RunnerKeyActions runner={runner({ ownerPersonId: null })} onSecret={() => {}} reload={async () => {}} />,
    );
    expect(html).toMatch(/<option value="" selected[^>]*>nobody<\/option>/);
  });

  it('keeps both modals shut until pressed', () => {
    const html = renderToStaticMarkup(
      <RunnerKeyActions runner={runner()} onSecret={() => {}} reload={async () => {}} />,
    );
    expect(html).not.toContain('A new secret is made now');
    expect(html).not.toContain('Anything using this key is refused');
  });
});

describe('RotateRunnerBody', () => {
  it('says the old secret stops working at once, with no refusal by default', () => {
    const html = renderToStaticMarkup(<RotateRunnerBody refusal={null} />);
    expect(html).toContain('A new secret is made now and the current one stops working at once.');
    expect(html).not.toContain('text-danger');
  });

  it('draws a refusal inside the body', () => {
    const html = renderToStaticMarkup(<RotateRunnerBody refusal="could not rotate" />);
    expect(html).toMatch(/text-danger[^>]*>could not rotate/);
  });
});

describe('RevokeRunnerBody', () => {
  it('points at rotate for a leaked secret, with no refusal by default', () => {
    const html = renderToStaticMarkup(<RevokeRunnerBody refusal={null} />);
    expect(html).toContain('To replace a leaked secret and keep the runner working, rotate instead.');
    expect(html).not.toContain('text-danger');
  });

  it('draws a refusal inside the body', () => {
    const html = renderToStaticMarkup(<RevokeRunnerBody refusal="could not revoke" />);
    expect(html).toMatch(/text-danger[^>]*>could not revoke/);
  });
});
