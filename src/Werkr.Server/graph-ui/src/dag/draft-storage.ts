import { Changeset } from "./changeset";

const DRAFT_KEY_PREFIX = "werkr-draft-";
const DRAFT_VERSION = 1;

interface DraftPayload {
  version: number;
  savedAt: string;
  changeset: ReturnType<Changeset["toSnapshot"]>;
}

/**
 * Persist changeset to localStorage keyed by userId + workflowId.
 * Keys include userId to prevent cross-user draft leakage on shared machines.
 */
export function saveDraft( userId: string, workflowId: number, changeset: Changeset ): void {
  if ( changeset.isEmpty() ) return;

  const key = `${DRAFT_KEY_PREFIX}${userId}-${workflowId}`;
  const payload: DraftPayload = {
    version: DRAFT_VERSION,
    savedAt: new Date().toISOString(),
    changeset: changeset.toSnapshot(),
  };

  try {
    localStorage.setItem( key, JSON.stringify( payload ) );
  } catch {
    // localStorage full or unavailable — silently skip
  }
}

/**
 * Load a draft changeset from localStorage.
 * Returns null if no draft exists, version mismatches, or data is corrupt.
 */
export function loadDraft( userId: string, workflowId: number ): Changeset | null {
  const key = `${DRAFT_KEY_PREFIX}${userId}-${workflowId}`;
  const raw = localStorage.getItem( key );
  if ( !raw ) return null;

  try {
    const parsed: DraftPayload = JSON.parse( raw );
    if ( parsed.version !== DRAFT_VERSION ) return null;
    return Changeset.fromSnapshot( parsed.changeset );
  } catch {
    return null;
  }
}

/** Remove a draft from localStorage. */
export function clearDraft( userId: string, workflowId: number ): void {
  localStorage.removeItem( `${DRAFT_KEY_PREFIX}${userId}-${workflowId}` );
}

/** Check whether a draft exists for the given user and workflow. */
export function hasDraft( userId: string, workflowId: number ): boolean {
  return localStorage.getItem( `${DRAFT_KEY_PREFIX}${userId}-${workflowId}` ) !== null;
}
