import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ISSUE_TYPES } from '../types';
import { OnePrControl, OnePrSteps, type OnePrSetting } from './OnePrControl';
import { onePrSaid, onePrToFile } from '../lib/onePr';

const story = (over: Partial<Parameters<typeof OnePrControl>[0]['issue']> = {}) => ({
  key: 'HA-5',
  type: 'story' as const,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
  onePrEnforced: false,
  ...over,
});

describe('onePrSaid', () => {
  it("says where a story's value came from", () => {
    expect(onePrSaid(story())).toBe("Off · the project's default");
    expect(onePrSaid(story({ onePr: true }))).toBe("On · the project's default");
    expect(onePrSaid(story({ onePr: true, onePrOwn: true }))).toBe('On · set on this story');
    expect(onePrSaid(story({ onePr: false, onePrOwn: false }))).toBe('Off · set on this story');
  });

  it('explains a bug as it does a story', () => {
    expect(onePrSaid(story({ type: 'bug' }))).toBe("Off · the project's default");
    expect(onePrSaid(story({ type: 'bug', onePr: true, onePrOwn: true }))).toBe('On · set on this bug');
  });

  it('names the story a task inherited it from', () => {
    expect(onePrSaid({ type: 'task', onePr: true, onePrOwn: null, onePrFrom: 'HA-5' })).toBe('On · inherited from HA-5');
  });

  it('says so where nothing sits above', () => {
    expect(onePrSaid({ type: 'task', onePr: false, onePrOwn: null, onePrFrom: null })).toBe('Off · not under a story or bug');
  });
});

describe('OnePrControl', () => {
  it('marks project default when the story carries no override of its own', () => {
    const html = renderToStaticMarkup(<OnePrControl issue={story({ onePr: true })} onChange={() => {}} />);

    expect(html).toContain('aria-label="Set HA-5&#x27;s one pull request setting"');
    expect(html).toMatch(/aria-pressed="true"[^>]*>Project default</);
    expect(html).toMatch(/aria-pressed="false"[^>]*>On</);
    expect(html).toMatch(/aria-pressed="false"[^>]*>Off</);
    expect(html).toContain("On · the project&#x27;s default");
  });

  it('marks on and off for an override, and disables only the marked step', () => {
    const on = renderToStaticMarkup(<OnePrControl issue={story({ onePr: true, onePrOwn: true })} onChange={() => {}} />);
    expect(on).toMatch(/aria-pressed="true"[^>]*>On</);
    expect(on).toMatch(/class="hatch-one-pr-step here"[^>]*disabled=""[^>]*>On</);
    expect(on).not.toMatch(/disabled=""[^>]*>Off</);

    const off = renderToStaticMarkup(<OnePrControl issue={story({ onePr: false, onePrOwn: false })} onChange={() => {}} />);
    expect(off).toMatch(/aria-pressed="true"[^>]*>Off</);
    expect(off).toContain('Off · set on this story');
  });

  it('stops answering while a press is out', () => {
    const html = renderToStaticMarkup(<OnePrControl issue={story()} busy onChange={() => {}} />);

    expect(html).toContain('aria-busy="true"');
    expect(html.match(/disabled=""/g)).toHaveLength(3);
  });

  it('is offered whoever is looking - the write is open to an API key', () => {
    const html = renderToStaticMarkup(<OnePrControl issue={story()} onChange={() => {}} />);

    expect(html).toContain('<button');
  });
});

describe('an enforced project', () => {
  const REASON = 'The project enforces its one-PR default on every story';

  it('locks every step with the reason and says the default is enforced', () => {
    for (const type of ['story', 'bug'] as const) {
      for (const onePr of [true, false]) {
        const html = renderToStaticMarkup(
          <OnePrControl issue={story({ type, onePr, onePrEnforced: true })} onChange={() => {}} />,
        );

        expect(html.match(/disabled=""/g)).toHaveLength(3);
        expect(html).toContain(REASON);
        expect(html).toContain(`${onePr ? 'On' : 'Off'} · the project&#x27;s default, enforced`);
        expect(html).toMatch(/aria-pressed="true"[^>]*>Project default</);
      }
    }
  });

  it('says an override from before enforcement is locked', () => {
    for (const type of ['story', 'bug'] as const) {
      for (const own of [true, false]) {
        const issue = story({ type, onePr: own, onePrOwn: own, onePrEnforced: true });
        const html = renderToStaticMarkup(<OnePrControl issue={issue} onChange={() => {}} />);

        const said = `${own ? 'On' : 'Off'} · set on this ${type} before the project enforced its default; locked`;
        expect(onePrSaid(issue)).toBe(said);
        expect(html).toContain(said);
        expect(html.match(/disabled=""/g)).toHaveLength(3);
        expect(html).toContain(REASON);
      }
    }
  });

  it('leaves a task as it was', () => {
    expect(onePrSaid({ type: 'task', onePr: true, onePrOwn: null, onePrFrom: 'HA-5', onePrEnforced: true })).toBe(
      'On · inherited from HA-5',
    );
    expect(onePrSaid({ type: 'task', onePr: false, onePrOwn: null, onePrFrom: null, onePrEnforced: true })).toBe(
      'Off · not under a story or bug',
    );
  });

  it('draws an optional project with live buttons', () => {
    const html = renderToStaticMarkup(<OnePrControl issue={story({ onePrEnforced: false })} onChange={() => {}} />);

    expect(html.match(/disabled=""/g)).toHaveLength(1);
    expect(html).not.toContain('enforces');
  });
});

describe('OnePrSteps', () => {
  it.each([
    ['inherit', 'Project default'],
    ['on', 'On'],
    ['off', 'Off'],
  ] as [OnePrSetting, string][])('marks exactly %s', (value, word) => {
    const html = renderToStaticMarkup(<OnePrSteps value={value} label="Set it" onChange={() => {}} />);

    expect(html.match(/aria-pressed="true"/g)).toHaveLength(1);
    expect(html).toMatch(new RegExp(`aria-pressed="true"[^>]*>${word}<`));
  });

  it('carries the given label', () => {
    const html = renderToStaticMarkup(<OnePrSteps value="inherit" label="Set the thing" onChange={() => {}} />);

    expect(html).toContain('aria-label="Set the thing"');
  });
});

describe('onePrToFile', () => {
  it('files the setting for a story or a bug', () => {
    for (const type of ['story', 'bug'] as const) {
      for (const setting of ['inherit', 'on', 'off'] as OnePrSetting[]) {
        expect(onePrToFile(type, setting)).toBe(setting);
      }
    }
  });

  it('files nothing for any other type, whatever the setting', () => {
    for (const type of ISSUE_TYPES.filter((t) => t !== 'story' && t !== 'bug')) {
      for (const setting of ['inherit', 'on', 'off'] as OnePrSetting[]) {
        expect(onePrToFile(type, setting)).toBeUndefined();
      }
    }
  });

  it('files nothing for a story or a bug under a project that enforces its own default', () => {
    expect(onePrToFile('story', 'on', true)).toBeUndefined();
    expect(onePrToFile('bug', 'off', true)).toBeUndefined();
  });

  it('still files the setting for a story or a bug left optional', () => {
    expect(onePrToFile('story', 'on')).toBe('on');
  });

  it('files nothing for a task whether or not the project enforces its default', () => {
    expect(onePrToFile('task', 'on')).toBeUndefined();
    expect(onePrToFile('task', 'on', true)).toBeUndefined();
  });
});
