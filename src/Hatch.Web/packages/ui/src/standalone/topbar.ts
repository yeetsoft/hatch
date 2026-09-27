/* The bar, for a page Hatch did not build.
   ---------------------------------------------------------------------------
   Two pages in the house are not Vite/React apps and cannot render <TopBar>:

   - **Swagger UI**, which is Swashbuckle's page. Hatch owns the document only
     through the <head> content it is allowed to inject.
   - **apps/logo**, a static Claude Design export whose runtime loads its own
     React from a CDN at runtime.

   Both were one-way trips: once on them you had no way back but the browser's
   own Back button. This is what puts the bar on them.

   **What is shared and what is restated.** The stylesheets are the real ones -
   TopBar.css and ThemeSwitch.css, imported below, the same
   files the React components import. So is the theme mechanism (themeStore).
   What is restated is the markup: about twenty elements, built here with DOM
   calls instead of JSX. That is the whole duplication, and it is the cheap
   half - the design lives in the CSS and the class names, and those have one
   source. Bundling React to avoid restating twenty elements would put ~150KB
   on both pages, and on the logo page it would be React's *second* copy.

   The rule that keeps the two in step: this file may not invent a class name.
   Every one below appears in TopBar.tsx or ThemeSwitch.tsx.

   base.css is deliberately NOT imported. It paints the body - face, ground,
   heading scale - and these are host pages with their own designs. The bar
   dresses itself and touches nothing outside its own <header>. The single line
   of base.css the bar's stylesheets genuinely assume - border-box - is restated
   for the bar's own subtree in the topbar.css beside this file. */

import {
  applyChoice,
  readChoice,
  writeChoice,
  type ThemeChoice,
} from '../theme/themeStore';

/* The canonical file is hatch-logo.png at the repository root; see TopBar.tsx. */
import logo from '../assets/hatch-logo.png';

import '../tokens.css';
import './topbar.css';
import '../components/TopBar.css';
import '../components/ThemeSwitch.css';

/**
 * How the host page answers the theme.
 *
 * - `switch` — the page is themed, so the bar carries the Auto/Light/Dark
 *   control and drives `<html data-theme>` from it, exactly as <ThemeSwitch>
 *   does inside an app. Swagger UI takes this: it is dressed from the same
 *   tokens (see apps/chrome/public/swagger.css), so the control is telling the
 *   truth about the whole page.
 * - `light` / `dark` — the page has one appearance and no answer for the other.
 *   The bar pins the root to that theme so it matches its host, and renders no
 *   control. The logo export takes `light`: it is a fixed cream design, and a
 *   toggle there would recolor a 48px bar and leave the page behind it.
 *
 * A pinned host is why this is a config value rather than a fixed invariant.
 * <TopBar> in React keeps the switch unconditionally, and should: every app
 * that renders it can theme. This is the case that component does not cover.
 */
export type StandaloneThemeMode = 'switch' | 'light' | 'dark';

export interface StandaloneTopBarConfig {
  /** The app's own name, and the only text the bar states. */
  appName: string;
  /** Where the logo and name link to: the host's own home. Same default as
      <TopBar>. */
  homeHref?: string;
  theme?: StandaloneThemeMode;
}

declare global {
  interface Window {
    /** Set by the host page before this script runs. See mountFromConfig. */
    hatchTopBar?: StandaloneTopBarConfig;
  }
}

const THEME_CHOICES: { value: ThemeChoice; label: string }[] = [
  { value: 'auto', label: 'Auto' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
];

/* Two bars on one page would otherwise share a radio group and steal each
   other's checked state - the same hazard useId answers in <ThemeSwitch>. */
let groupSeq = 0;

/** The brand link in TopBar.tsx. An <a>, so middle-click and "copy link address" work. */
function brand(href: string, name: string): HTMLAnchorElement {
  const link = document.createElement('a');
  link.className = 'hatch-topbar__brand';
  link.href = href;

  /* Decoration: the name beside it is the link's accessible name. */
  const img = document.createElement('img');
  img.src = logo;
  img.alt = '';
  img.width = 28;
  img.height = 28;

  /* A <span>, not an <h1>: the bar is a wordmark saying where you are, and the
     page's own heading keeps the rank. */
  const text = document.createElement('span');
  text.className = 'hatch-topbar__name';
  text.textContent = name;

  link.append(img, text);
  return link;
}

/**
 * ThemeSwitch.tsx, in the `accent` tone the bar always uses.
 *
 * Real radio inputs, for the reason the component gives: the browser then
 * supplies the whole keyboard contract - one tab stop for the group, arrows to
 * move within it - and the grouping is announced off the fieldset/legend.
 */
function themeSwitch(): HTMLFieldSetElement {
  const name = `hatch-theme-${(groupSeq += 1)}`;
  const current = readChoice();

  const fieldset = document.createElement('fieldset');
  fieldset.className = 'hatch-theme-switch hatch-theme-switch--accent';

  const legend = document.createElement('legend');
  legend.className = 'hatch-theme-switch__legend';
  legend.textContent = 'Theme';
  fieldset.appendChild(legend);

  for (const option of THEME_CHOICES) {
    const label = document.createElement('label');
    label.className = 'hatch-theme-switch__option';

    const input = document.createElement('input');
    input.type = 'radio';
    input.name = name;
    input.value = option.value;
    input.checked = option.value === current;
    input.addEventListener('change', () => {
      if (!input.checked) return;
      writeChoice(option.value);
      applyChoice(option.value);
    });

    const text = document.createElement('span');
    text.textContent = option.label;

    label.append(input, text);
    fieldset.appendChild(label);
  }

  return fieldset;
}

/** Builds the bar. TopBar.tsx's structure, element for element. */
export function createTopBar(config: StandaloneTopBarConfig): HTMLElement {
  const { appName, homeHref = '/', theme = 'switch' } = config;

  const header = document.createElement('header');
  header.className = 'hatch-topbar';

  const inner = document.createElement('div');
  inner.className = 'hatch-topbar__inner';

  const start = document.createElement('div');
  start.className = 'hatch-topbar__side';
  start.appendChild(brand(homeHref, appName));

  const end = document.createElement('div');
  end.className = 'hatch-topbar__side hatch-topbar__side--end';
  if (theme === 'switch') {
    end.appendChild(themeSwitch());
  }

  inner.append(start, end);
  header.appendChild(inner);
  return header;
}

/** The theme the host page is to be shown in, settled before anything paints. */
function applyHostTheme(config: StandaloneTopBarConfig): void {
  const theme = config.theme ?? 'switch';
  applyChoice(theme === 'switch' ? readChoice() : theme);
}

/** Puts the bar at the top of the document. */
export function mountTopBar(config: StandaloneTopBarConfig): void {
  applyHostTheme(config);
  document.body.prepend(createTopBar(config));
}

/**
 * Auto-mount from `window.hatchTopBar`.
 *
 * Configuration rides on a global rather than on this script's own `data-`
 * attributes because one of the two hosts cannot set them: Swashbuckle's
 * `InjectJavascript` writes the `src` and nothing else, so the config arrives
 * as a separate `<script>` through its `HeadContent`. Those two tags may be
 * emitted in either order, which is why the config is read at DOM-ready rather
 * than at evaluation - by then every script in <head> has run, whichever order
 * they were written in. A page that sets nothing gets no bar, not an error.
 *
 * The theme is the exception, and is applied at evaluation when the config is
 * already there: this script runs from <head>, so writing `data-theme` that
 * early means the first paint is the right one instead of a light frame in
 * front of a dark page. When the config lands after us, DOM-ready applies it -
 * still before the body has anything of ours in it.
 */
function autoMount(): void {
  const config = window.hatchTopBar;
  if (config) mountTopBar(config);
}

if (typeof window !== 'undefined') {
  if (window.hatchTopBar) applyHostTheme(window.hatchTopBar);

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', autoMount, { once: true });
  } else {
    autoMount();
  }
}
