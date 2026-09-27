import { createContext, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { getMe } from '../api/client';
import type { Me } from '../types';
import { LOCAL_PERSON_CHANGED } from './localPerson';

interface MeValue {
  /** Null until answered, and for a caller that is nobody (a key). */
  me: Me | null;
  isAdmin: boolean;
}

const MeContext = createContext<MeValue>({ me: null, isAdmin: false });

/**
 * Asks who is here once per load, and again when the Settings page says the
 * local name changed, so the nav and any page can read the role without each
 * making the call. Silent on failure: the bar is not where a fetch failure is
 * announced, and no answer reads as no role.
 */
export function MeProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);

  useEffect(() => {
    let cancelled = false;

    const read = async () => {
      try {
        const answer = await getMe();
        if (!cancelled) setMe(answer);
      } catch {
        // Deliberately silent - see the note above.
      }
    };

    void read();

    const onChanged = () => void read();
    window.addEventListener(LOCAL_PERSON_CHANGED, onChanged);

    return () => {
      cancelled = true;
      window.removeEventListener(LOCAL_PERSON_CHANGED, onChanged);
    };
  }, []);

  const value = useMemo(() => ({ me, isAdmin: me?.role === 'admin' }), [me]);

  return <MeContext.Provider value={value}>{children}</MeContext.Provider>;
}

export const useMe = () => useContext(MeContext);
