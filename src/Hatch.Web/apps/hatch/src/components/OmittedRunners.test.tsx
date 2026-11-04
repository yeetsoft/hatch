import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { OmittedRunners } from './OmittedRunners';
import type { RunnerOmission } from '../types';

const own: RunnerOmission = { runner: 'alpha', reason: 'flaky', by: 'the first person', at: '2026-01-02T03:04:05Z', fromKey: null };
const inherited: RunnerOmission = { runner: 'beta', reason: null, by: 'the second person', at: '2026-01-02T03:04:05Z', fromKey: 'AER-9' };

const draw = (over: Partial<Parameters<typeof OmittedRunners>[0]> = {}) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <OmittedRunners
        omissions={[own, inherited]}
        runnerNames={null}
        pickable={[]}
        liftingRunner={null}
        onLift={() => {}}
        onAdd={async () => null}
        {...over}
      />
    </MemoryRouter>,
  );

describe('OmittedRunners', () => {
  it('draws an own row and an inherited row, the inherited one without a Lift button', () => {
    const html = draw();
    expect(html).toContain('alpha');
    expect(html).toContain('beta');
    expect(html).toContain('omitted by the first person');
    expect(html).toContain('omitted by the second person');
    expect(html).toContain('href="/issues/AER-9"');
    expect(html).toContain('Lifted on');
    expect(html.match(/>Lift</g)).toHaveLength(1);
  });

  it('says a runner that omitted itself recused', () => {
    expect(draw({ omissions: [{ ...own, by: 'alpha' }] })).toContain('recused itself');
  });

  it('disables the button of the row whose lift is out', () => {
    const html = draw({ liftingRunner: 'alpha' });
    expect(html).toMatch(/<button[^>]*disabled[^>]*>Lifting\.\.\.</);
    expect(draw()).not.toMatch(/<button[^>]*disabled[^>]*>Lift</);
  });

  it('marks a name the board does not know only once the names are read', () => {
    const row = { ...own, runner: 'b' };
    expect(draw({ omissions: [row], runnerNames: ['a'] })).toContain('not a runner on this board');
    expect(draw({ omissions: [row], runnerNames: null })).not.toContain('not a runner on this board');
  });

  it('with nothing omitted says so, and still draws the add controls, labelled "Omit a runner"', () => {
    const html = draw({ omissions: [], pickable: ['one', 'two'] });
    expect(html).toContain('No runner is omitted.');
    expect(html).toContain('maxLength="200"');
    expect(html).toContain('0/200');
    expect(html).toContain('<option value="one">');
    expect(html).toContain('<option value="two">');
    expect(html).toMatch(/<button[^>]*disabled[^>]*>Omit</);
    expect(html).toContain('Omit a runner');
  });
});
