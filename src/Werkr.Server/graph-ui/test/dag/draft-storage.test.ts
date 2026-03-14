import { describe, it, expect, beforeEach, vi } from "vitest";
import { saveDraft, loadDraft, clearDraft, hasDraft } from "../../src/dag/draft-storage";
import { Changeset } from "../../src/dag/changeset";

/** Minimal localStorage mock for Node/vitest. */
function createStorageMock(): Storage {
  const store = new Map<string, string>();
  return {
    getItem: ( key: string ) => store.get( key ) ?? null,
    setItem: ( key: string, value: string ) => { store.set( key, value ); },
    removeItem: ( key: string ) => { store.delete( key ); },
    clear: () => store.clear(),
    get length() { return store.size; },
    key: ( index: number ) => [...store.keys()][index] ?? null,
  };
}

describe( "draft-storage", () => {
  let storage: Storage;

  beforeEach( () => {
    storage = createStorageMock();
    vi.stubGlobal( "localStorage", storage );
  } );

  it( "round-trips a changeset through save and load", () => {
    const cs = new Changeset();
    cs.addStep( cs.nextTempId(), 10, 1, { x: 100, y: 200 } );
    cs.addDependency( -1, 5 );

    saveDraft( "user1", 42, cs );
    const restored = loadDraft( "user1", 42 );

    expect( restored ).not.toBeNull();
    expect( restored!.getDirtyCount() ).toBe( cs.getDirtyCount() );
    expect( restored!.isEmpty() ).toBe( false );
  } );

  it( "hasDraft returns true when a draft exists", () => {
    const cs = new Changeset();
    cs.addStep( -1, 10, 1 );

    expect( hasDraft( "user1", 42 ) ).toBe( false );
    saveDraft( "user1", 42, cs );
    expect( hasDraft( "user1", 42 ) ).toBe( true );
  } );

  it( "clearDraft removes the stored draft", () => {
    const cs = new Changeset();
    cs.addStep( -1, 10, 1 );
    saveDraft( "user1", 42, cs );

    expect( hasDraft( "user1", 42 ) ).toBe( true );
    clearDraft( "user1", 42 );
    expect( hasDraft( "user1", 42 ) ).toBe( false );
    expect( loadDraft( "user1", 42 ) ).toBeNull();
  } );

  it( "isolates drafts by user and workflow ID", () => {
    const cs = new Changeset();
    cs.addStep( -1, 10, 1 );

    saveDraft( "alice", 1, cs );
    saveDraft( "bob", 1, cs );
    saveDraft( "alice", 2, cs );

    expect( hasDraft( "alice", 1 ) ).toBe( true );
    expect( hasDraft( "bob", 1 ) ).toBe( true );
    expect( hasDraft( "alice", 2 ) ).toBe( true );
    expect( hasDraft( "bob", 2 ) ).toBe( false );

    clearDraft( "alice", 1 );
    expect( hasDraft( "alice", 1 ) ).toBe( false );
    expect( hasDraft( "bob", 1 ) ).toBe( true );
  } );

  it( "returns null for corrupted JSON in localStorage", () => {
    storage.setItem( "werkr-draft-user1-42", "not-valid-json{{{" );
    expect( loadDraft( "user1", 42 ) ).toBeNull();
  } );

  it( "returns null for version mismatch", () => {
    const payload = JSON.stringify( {
      version: 999,
      savedAt: new Date().toISOString(),
      changeset: { stepChanges: [], depChanges: [], nextTempId: -1 },
    } );
    storage.setItem( "werkr-draft-user1-42", payload );
    expect( loadDraft( "user1", 42 ) ).toBeNull();
  } );

  it( "does not save when changeset is empty", () => {
    const cs = new Changeset();
    saveDraft( "user1", 42, cs );
    expect( hasDraft( "user1", 42 ) ).toBe( false );
  } );

  it( "handles localStorage.setItem throwing (storage full)", () => {
    const cs = new Changeset();
    cs.addStep( -1, 10, 1 );

    vi.spyOn( storage, "setItem" ).mockImplementation( () => {
      throw new DOMException( "QuotaExceededError" );
    } );

    // Should not throw
    expect( () => saveDraft( "user1", 42, cs ) ).not.toThrow();
  } );
} );
