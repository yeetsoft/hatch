import type { LocalModelCreateRequest } from '../types';

/** What the add form holds: three strings, the way an input does. The footprint
    is in binary gigabytes, the unit the table draws it in. */
export interface LocalModelDraft {
  ollamaId: string;
  contextLength: string;
  footprintGigabytes: string;
}

export const EMPTY_LOCAL_MODEL_DRAFT: LocalModelDraft = { ollamaId: '', contextLength: '', footprintGigabytes: '' };

/** The draft as the wire takes it, or null while it could not be sent - which
    only gates the button. Whether the id is a real Ollama id, or a duplicate,
    is the server's to say. */
export function localModelRequest(draft: LocalModelDraft): LocalModelCreateRequest | null {
  const ollamaId = draft.ollamaId.trim();
  if (ollamaId === '') return null;

  const contextLengthTokens = positive(draft.contextLength);
  const gigabytes = positive(draft.footprintGigabytes);
  if (contextLengthTokens === null || !Number.isInteger(contextLengthTokens) || gigabytes === null) return null;

  const memoryFootprintBytes = Math.round(gigabytes * 1024 ** 3);
  return memoryFootprintBytes > 0 ? { ollamaId, contextLengthTokens, memoryFootprintBytes } : null;
}

function positive(text: string): number | null {
  if (text.trim() === '') return null;
  const value = Number(text);
  return Number.isFinite(value) && value > 0 ? value : null;
}

/** The line a session's Machine panel draws for what its runner is asked to
    hold - `Holds a, b (a pulled)` - or null for an empty hold. An id is
    "pulled" where the runner's own beat already lists it; the parenthesis is
    left off when none is. */
export function heldLine(hold: string[], pulled: string[]): string | null {
  if (hold.length === 0) return null;

  const have = hold.filter((id) => pulled.includes(id));
  return `Holds ${hold.join(', ')}${have.length > 0 ? ` (${have.join(', ')} pulled)` : ''}`;
}
