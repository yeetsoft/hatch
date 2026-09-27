/* The Menu's state machine, with no React and no DOM in it so a node test can
   drive it. The timers are injected for the same reason: a test hands over a
   fake scheduler and advances it by hand instead of waiting on a clock.

   One controller is one bar. It holds which menu is open (never more than one)
   and the single timer that may be pending, because a bar has only one pointer
   and so only one intent to open or to close at a time. */

export const OPEN_DELAY_MS = 120;
export const CLOSE_DELAY_MS = 200;

export interface MenuBarOptions {
  setTimeout: (fn: () => void, ms: number) => unknown;
  clearTimeout: (handle: unknown) => void;
  openDelay?: number;
  closeDelay?: number;
  /** Called after every change to which menu is open. */
  onChange?: (openId: string | null) => void;
}

export interface MenuBar {
  readonly openId: string | null;
  isOpen: (id: string) => boolean;
  /** Pointer entered the trigger or the panel. Ignored unless the pointer can
      hover: a tap is a click, and must not also be an intent to open. */
  pointerEnter: (id: string, hoverCapable: boolean) => void;
  pointerLeave: (id: string) => void;
  /** Click or tap. */
  toggle: (id: string) => void;
  /** Keyboard: immediate. */
  open: (id: string) => void;
  /** Escape, outside press, focus leaving, an item activated. With no id,
      whichever menu is open; with one, a no-op unless it is that one. */
  close: (id?: string) => void;
  subscribe: (listener: () => void) => () => void;
}

export function createMenuBar(options: MenuBarOptions): MenuBar {
  const openDelay = options.openDelay ?? OPEN_DELAY_MS;
  const closeDelay = options.closeDelay ?? CLOSE_DELAY_MS;
  let openId: string | null = null;
  let pending: unknown = null;
  let hasPending = false;
  const listeners = new Set<() => void>();

  function cancel() {
    if (hasPending) {
      options.clearTimeout(pending);
      hasPending = false;
      pending = null;
    }
  }

  function schedule(fn: () => void, ms: number) {
    cancel();
    hasPending = true;
    pending = options.setTimeout(() => {
      hasPending = false;
      pending = null;
      fn();
    }, ms);
  }

  function set(next: string | null) {
    if (next === openId) return;
    openId = next;
    options.onChange?.(openId);
    listeners.forEach((listener) => listener());
  }

  return {
    get openId() {
      return openId;
    },
    isOpen: (id) => openId === id,
    pointerEnter(id, hoverCapable) {
      if (!hoverCapable) return;
      cancel();
      if (openId === id) return;
      // Already in the bar with one open: switch at once, so the pointer
      // crossing to the next trigger never shows two panels or none.
      if (openId !== null) {
        set(id);
        return;
      }
      schedule(() => set(id), openDelay);
    },
    pointerLeave(id) {
      if (openId === id) {
        schedule(() => set(null), closeDelay);
      } else {
        // Only an open of this menu can be pending; leaving cancels it.
        cancel();
      }
    },
    toggle(id) {
      cancel();
      set(openId === id ? null : id);
    },
    open(id) {
      cancel();
      set(id);
    },
    close(id) {
      cancel();
      if (id === undefined || openId === id) set(null);
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}
