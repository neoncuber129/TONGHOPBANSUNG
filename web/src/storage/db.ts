import { openDB, type DBSchema, type IDBPDatabase } from 'idb'
import { createDefaultState, type AppState } from '../domain/types'
import {
  decodeStateFromSqlite,
  encodeStateToSqlite,
  isSqliteBytes,
} from './sqliteCodec'

interface ThbsDb extends DBSchema {
  meta: {
    key: string
    value: AppState | Uint8Array | ArrayBuffer | string
  }
}

const DB_NAME = 'tonghopbansung-web'
const DB_VERSION = 2
const STATE_KEY = 'appState'
const SQLITE_KEY = 'data.db'
/** '1' = đã nạp DB mặc định → ẩn nút; '0'/missing = hiện nút */
const DEFAULT_DB_FLAG = 'defaultDbConsumed'

let dbPromise: Promise<IDBPDatabase<ThbsDb>> | null = null

function getDb() {
  if (!dbPromise) {
    dbPromise = openDB<ThbsDb>(DB_NAME, DB_VERSION, {
      upgrade(db) {
        if (!db.objectStoreNames.contains('meta')) {
          db.createObjectStore('meta')
        }
      },
    })
  }
  return dbPromise
}

export async function loadState(): Promise<AppState> {
  const db = await getDb()

  const sqliteRaw = await db.get('meta', SQLITE_KEY)
  if (sqliteRaw) {
    const bytes = toUint8Array(sqliteRaw)
    if (bytes && isSqliteBytes(bytes)) {
      const state = normalizeState(await decodeStateFromSqlite(bytes))
      // Người dùng cũ đã có data trước khi có nút → coi như đã dùng, ẩn nút
      if ((await getDefaultDbFlag()) === null && !isWorkspaceEmpty(state)) {
        await markDefaultDbConsumed()
      }
      return state
    }
  }

  // Migrate legacy JSON AppState blob → SQLite
  const legacy = await db.get('meta', STATE_KEY)
  if (
    legacy &&
    typeof legacy === 'object' &&
    !(legacy instanceof Uint8Array) &&
    !ArrayBuffer.isView(legacy)
  ) {
    const state = normalizeState(legacy as AppState)
    await saveState(state)
    await db.delete('meta', STATE_KEY)
    if (!isWorkspaceEmpty(state)) await markDefaultDbConsumed()
    return state
  }

  // First visit: seed from bundled default .thbs
  const seeded = await loadBundledDefaultState()
  if (seeded) {
    await saveState(seeded)
    await markDefaultDbConsumed()
    return seeded
  }

  const fresh = createDefaultState()
  await saveState(fresh)
  return fresh
}

export async function loadBundledDefaultState(): Promise<AppState | null> {
  try {
    const url = `${import.meta.env.BASE_URL}Tonghopbansungdb.thbs`
    const res = await fetch(url)
    if (!res.ok) return null
    const buffer = new Uint8Array(await res.arrayBuffer())
    if (!isSqliteBytes(buffer)) return null
    return normalizeState(await decodeStateFromSqlite(buffer))
  } catch {
    return null
  }
}

async function getDefaultDbFlag(): Promise<string | null> {
  const db = await getDb()
  const v = await db.get('meta', DEFAULT_DB_FLAG)
  if (v === undefined || v === null) return null
  return String(v)
}

export async function isDefaultDbOfferVisible(): Promise<boolean> {
  return (await getDefaultDbFlag()) !== '1'
}

export async function markDefaultDbConsumed(): Promise<void> {
  const db = await getDb()
  await db.put('meta', '1', DEFAULT_DB_FLAG)
}

export async function reopenDefaultDbOffer(): Promise<void> {
  const db = await getDb()
  await db.put('meta', '0', DEFAULT_DB_FLAG)
}

export function isWorkspaceEmpty(state: AppState): boolean {
  if (state.sessions.length > 0) return false
  const people = state.groups.reduce((n, g) => n + (g.shooters?.length ?? 0), 0)
  return people === 0
}

export async function saveState(state: AppState): Promise<void> {
  const db = await getDb()
  const bytes = await encodeStateToSqlite(state, false)
  await db.put('meta', bytes, SQLITE_KEY)
}

function normalizeState(state: AppState): AppState {
  return {
    presets: state.presets ?? [],
    groups: (state.groups ?? []).map((g) => ({
      ...g,
      shooters: g.shooters ?? [],
    })),
    sessions: (state.sessions ?? []).map((s) => ({
      ...s,
      shooters: s.shooters ?? [],
    })),
    activeGroupId: state.activeGroupId ?? null,
    activeSessionId: state.activeSessionId ?? null,
  }
}

export async function downloadSqliteBackup(state: AppState, filename?: string): Promise<void> {
  const bytes = await encodeStateToSqlite(state, true)
  const blob = new Blob(
    [bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer],
    {
      type: 'application/x-sqlite3',
    },
  )
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename ?? `Tonghopbansung_${formatStamp()}.thbs`
  a.click()
  URL.revokeObjectURL(url)
}

/** Phục hồi .thbs / .db (SQLite WPF) hoặc .json legacy. */
export async function readBackupFile(file: File): Promise<AppState> {
  const buffer = new Uint8Array(await file.arrayBuffer())
  if (isSqliteBytes(buffer)) {
    return normalizeState(await decodeStateFromSqlite(buffer))
  }

  const text = new TextDecoder().decode(buffer)
  const parsed = JSON.parse(text) as AppState
  if (!parsed || !Array.isArray(parsed.presets) || !Array.isArray(parsed.groups)) {
    throw new Error('File không hợp lệ. Hãy chọn .thbs / .db (SQLite) hoặc .json cũ.')
  }
  return normalizeState(parsed)
}

function toUint8Array(value: unknown): Uint8Array | null {
  if (value instanceof Uint8Array) return value
  if (value instanceof ArrayBuffer) return new Uint8Array(value)
  if (ArrayBuffer.isView(value)) {
    return new Uint8Array(value.buffer, value.byteOffset, value.byteLength)
  }
  return null
}

function formatStamp(): string {
  const d = new Date()
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}_${p(d.getHours())}${p(d.getMinutes())}`
}
