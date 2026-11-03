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
});
