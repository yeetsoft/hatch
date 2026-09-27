import {
  createContext,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
  useSyncExternalStore,
} from 'react';
import type {
  ComponentPropsWithoutRef,
  ElementType,
  KeyboardEvent as ReactKeyboardEvent,
  MouseEvent as ReactMouseEvent,
  PointerEvent as ReactPointerEvent,
  FocusEvent as ReactFocusEvent,
  ReactNode,
  Ref,
} from 'react';
import { createMenuBar } from './menuState';
import type { MenuBar as MenuBarController } from './menuState';
import './Menu.css';

export type MenuTone = 'surface' | 'accent';
export type MenuAlign = 'start' | 'end';

/** What a custom `trigger` receives: spread it on the one element that opens
    the panel. It carries the ref, the handlers and the ARIA the state machine
    relies on, so an icon button is a button plus `{...props}`. */
export interface MenuTriggerProps {
  ref: Ref<HTMLButtonElement>;
  type: 'button';
  className: string;
  'aria-label'?: string;
  'aria-expanded': boolean;
  'aria-controls': string;
  'aria-current'?: 'true';
  onClick: (event: ReactMouseEvent<HTMLButtonElement>) => void;
  onKeyDown: (event: ReactKeyboardEvent<HTMLButtonElement>) => void;
}

export interface MenuProps {
  /** The trigger's text, or its accessible name when `trigger` draws it. */
  label: string;
  /** Which edge of the trigger the panel lines up with. */
  align?: MenuAlign;
  /** The ground the trigger sits on. The panel is always a card. */
  tone?: MenuTone;
  /** The trigger lit: this menu holds the current page. */
  active?: boolean;
  /** Open on first render, uncontrolled. For the gallery's "open" specimen. */
  defaultOpen?: boolean;
  /** Draw the trigger yourself, for an icon. */
  trigger?: (props: MenuTriggerProps) => ReactNode;
  children: ReactNode;
}

const BarContext = createContext<MenuBarController | null>(null);
const ItemContext = createContext<(() => void) | null>(null);

function newController(): MenuBarController {
  return createMenuBar({
    setTimeout: (fn, ms) => window.setTimeout(fn, ms),
    clearTimeout: (handle) => window.clearTimeout(handle as number),
  });
}

/** Read at event time, not render time: a laptop that gains a mouse changes
    its answer, and the server has no answer at all. */
function canHover() {
  return typeof window.matchMedia === 'function' && window.matchMedia('(hover: hover)').matches;
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])';

export interface MenuBarProps {
  as?: ElementType;
  className?: string;
  children: ReactNode;
}

/** Puts the menus inside it on one controller, so only one is open at a time
    and moving across triggers switches without a frame of neither or both.
    Nested inside another `Menu.Bar`, it joins that one instead of opening its
    own - two bars on one controller is what lets a bar's own nav coordinate
    with a trigger the bar renders itself (the gear), rather than the two
    shadowing each other. */
function MenuBar({ as: Tag = 'div', className, children }: MenuBarProps) {
  const existing = useContext(BarContext);
  const [own] = useState(newController);
  const tag = <Tag className={['hatch-menu-bar', className].filter(Boolean).join(' ')}>{children}</Tag>;
  return existing ? tag : <BarContext.Provider value={own}>{tag}</BarContext.Provider>;
}

/**
 * A trigger that opens a panel of links, or of anything: on hover, on click or
 * tap, and from the keyboard.
 *
 * The APG disclosure-navigation pattern, deliberately not `role="menu"`: the
 * rows are links and a segmented control, not commands, and `role="menu"`
 * would promise arrow-key roving that a link list does not have. The trigger
 * is a button with `aria-expanded` and `aria-controls`; Tab moves through the
 * panel as through any other content, and leaving it closes it.
 *
 * All timing and one-open-per-bar live in `menuState.ts`; this file is the DOM
 * around it (focus, Escape, outside press).
 */
function MenuRoot({
  label,
  align = 'start',
  tone = 'surface',
  active = false,
  defaultOpen = false,
  trigger,
  children,
}: MenuProps) {
  const bar = useContext(BarContext);
  const [own] = useState(() => (bar ? null : newController()));
  const controller = bar ?? own!;
  const id = useId();
  const panelId = `${id}-panel`;
  const open = useSyncExternalStore(
    controller.subscribe,
    () => controller.isOpen(id),
    () => false,
  );
  const wrapperRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const focusFirst = useRef(false);

  useEffect(() => {
    if (defaultOpen) controller.open(id);
    return () => controller.close(id);
    // Once: defaultOpen is only the first state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Focus follows the commit that opened the panel, not the keypress: until
  // then the panel is still visibility:hidden and cannot take focus.
  useEffect(() => {
    if (open && focusFirst.current) {
      panelRef.current?.querySelector<HTMLElement>(FOCUSABLE)?.focus();
    }
    focusFirst.current = false;
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!wrapperRef.current?.contains(event.target as Node)) controller.close(id);
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open, controller, id]);

  const openFromKeyboard = () => {
    focusFirst.current = true;
    controller.open(id);
    // Already open (by hover): no commit follows, so focus now.
    if (open) {
      focusFirst.current = false;
      panelRef.current?.querySelector<HTMLElement>(FOCUSABLE)?.focus();
    }
  };

  const triggerProps: MenuTriggerProps = {
    ref: triggerRef,
    type: 'button',
    className: [
      'hatch-menu__trigger',
      `hatch-menu__trigger--${tone}`,
      active ? 'hatch-menu__trigger--active' : '',
    ]
      .filter(Boolean)
      .join(' '),
    'aria-expanded': open,
    'aria-controls': panelId,
    'aria-current': active ? 'true' : undefined,
    onClick(event) {
      // detail is 0 for a keyboard-initiated click (Enter, Space): that opens
      // and goes in; a pointer click just toggles.
      if (event.detail === 0 && !open) openFromKeyboard();
      else controller.toggle(id);
    },
    onKeyDown(event) {
      if (event.key === 'ArrowDown') {
        event.preventDefault();
        openFromKeyboard();
      }
    },
  };
  if (trigger) triggerProps['aria-label'] = label;

  const onPointerEnter = (event: ReactPointerEvent) => {
    if (event.pointerType === 'touch') return;
    controller.pointerEnter(id, canHover());
  };
  const onPointerLeave = (event: ReactPointerEvent) => {
    if (event.pointerType === 'touch') return;
    controller.pointerLeave(id);
  };
  const onKeyDown = (event: ReactKeyboardEvent) => {
    if (event.key === 'Escape' && open) {
      event.stopPropagation();
      controller.close(id);
      triggerRef.current?.focus();
    }
  };
  const onBlur = (event: ReactFocusEvent) => {
    const next = event.relatedTarget as Node | null;
    if (next && !wrapperRef.current?.contains(next)) controller.close(id);
  };

  return (
    <div
      ref={wrapperRef}
      className="hatch-menu"
      onPointerEnter={onPointerEnter}
      onPointerLeave={onPointerLeave}
      onKeyDown={onKeyDown}
      onBlur={onBlur}
    >
      {trigger ? (
        trigger(triggerProps)
      ) : (
        <button {...triggerProps}>
          {label}
          <svg className="hatch-menu__caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
            <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </button>
      )}
      {/* The outer box carries the gap between trigger and panel as padding, so
          a pointer crossing it is still over the panel and does not leave. */}
      <div
        ref={panelRef}
        id={panelId}
        className={`hatch-menu__panel hatch-menu__panel--${align}${open ? ' hatch-menu__panel--open' : ''}`}
      >
        <ItemContext.Provider value={() => controller.close(id)}>
          <div className="hatch-menu__surface">{children}</div>
        </ItemContext.Provider>
      </div>
    </div>
  );
}

export type MenuItemProps<T extends ElementType = 'a'> = { as?: T } & Omit<
  ComponentPropsWithoutRef<T>,
  'as'
>;

/** A row. `as` is what it renders - a plain `<a>` by default, a router's
    `NavLink` where the app has one - so this package never imports a router.
    Activating it closes the menu: a row clicked under a hovering pointer would
    otherwise navigate and leave the panel over the new page. */
function MenuItem<T extends ElementType = 'a'>({ as, className, onClick, ...rest }: MenuItemProps<T>) {
  const close = useContext(ItemContext);
  const Tag: ElementType = as ?? 'a';
  const own = 'hatch-menu__item';
  const joined =
    typeof className === 'function'
      ? (state: unknown) => `${own} ${(className as (s: unknown) => string)(state)}`
      : [own, className].filter(Boolean).join(' ');
  return (
    <Tag
      {...rest}
      className={joined}
      onClick={(event: ReactMouseEvent) => {
        (onClick as ((e: ReactMouseEvent) => void) | undefined)?.(event);
        close?.();
      }}
    />
  );
}

export const Menu = Object.assign(MenuRoot, { Bar: MenuBar, Item: MenuItem });
