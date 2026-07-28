import initSqlJs, { type Database, type SqlJsStatic } from 'sql.js'
import wasmUrl from 'sql.js/dist/sql-wasm.wasm?url'
import {
  ClassificationConditionKind,
  TargetKind,
  createDefaultState,
  ensureLegacyCondition,
  ensureShotMatrix,
  getRoundCounts,
  type AppState,
  type ClassificationCondition,
  type ClassificationRule,
  type Group,
  type ScorePreset,
  type Shooter,
  type ShootingSession,
  type TargetCluster,
  type TargetDefinition,
} from '../domain/types'

let sqlPromise: Promise<SqlJsStatic> | null = null

export function getSqlJs(): Promise<SqlJsStatic> {
  if (!sqlPromise) {
    sqlPromise = initSqlJs({ locateFile: () => wasmUrl })
  }
  return sqlPromise
}

const SCHEMA_SQL = `
CREATE TABLE IF NOT EXISTS meta (
  key TEXT PRIMARY KEY NOT NULL,
  value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS presets (
  id TEXT PRIMARY KEY NOT NULL,
  name TEXT NOT NULL,
  sort_order INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS clusters (
  id TEXT PRIMARY KEY NOT NULL,
  preset_id TEXT NOT NULL,
  name TEXT NOT NULL,
  sort_order INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS targets (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  cluster_id TEXT NOT NULL,
  name TEXT NOT NULL,
  round_count INTEGER NOT NULL,
  kind INTEGER NOT NULL,
  miss_penalty INTEGER NOT NULL DEFAULT 0,
  hit_bonus INTEGER NOT NULL DEFAULT 0,
  sort_order INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS classification_rules (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  preset_id TEXT NOT NULL,
  label TEXT NOT NULL,
  min_score INTEGER NOT NULL,
  priority INTEGER NOT NULL,
  sort_order INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS classification_conditions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  rule_id INTEGER NOT NULL,
  kind INTEGER NOT NULL,
  target_index INTEGER NOT NULL,
  min_value INTEGER NOT NULL,
  sort_order INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS groups (
  id TEXT PRIMARY KEY NOT NULL,
  name TEXT NOT NULL,
  preset_id TEXT NOT NULL,
  sort_order INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS sessions (
  id TEXT PRIMARY KEY NOT NULL,
  name TEXT NOT NULL,
  group_id TEXT NOT NULL,
  created_at TEXT NOT NULL,
  sort_order INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS shooters (
  id TEXT PRIMARY KEY NOT NULL,
  session_id TEXT NULL,
  group_id TEXT NULL,
  name TEXT NOT NULL,
  rank TEXT NOT NULL,
  position TEXT NOT NULL,
  unit TEXT NOT NULL,
  sort_order INTEGER NOT NULL,
  is_selected INTEGER NOT NULL,
  shots_json TEXT NOT NULL
);
`

export function isSqliteBytes(bytes: Uint8Array): boolean {
  if (bytes.length < 16) return false
  const header = 'SQLite format 3\0'
  for (let i = 0; i < header.length; i++) {
    if (bytes[i] !== header.charCodeAt(i)) return false
  }
  return true
}

export async function encodeStateToSqlite(state: AppState, compact = true): Promise<Uint8Array> {
  const SQL = await getSqlJs()
  const db = new SQL.Database()
  try {
    db.run(SCHEMA_SQL)
    ensureColumn(db, 'presets', 'knock_down_miss_penalty', 'INTEGER NOT NULL DEFAULT 0')
    writeAll(db, state)
    if (compact) db.run('VACUUM;')
    return db.export()
  } finally {
    db.close()
  }
}

export async function decodeStateFromSqlite(bytes: Uint8Array): Promise<AppState> {
  const SQL = await getSqlJs()
  const db = new SQL.Database(bytes)
  try {
    ensureSchema(db)
    const state = readAll(db)
    if (state.presets.length === 0) {
      throw new Error('File sao lưu không có dữ liệu nhóm / preset.')
    }
    ensureShotMatrices(state)
    return state
  } finally {
    db.close()
  }
}

function ensureSchema(db: Database) {
  db.run(SCHEMA_SQL)
  ensureColumn(db, 'presets', 'knock_down_miss_penalty', 'INTEGER NOT NULL DEFAULT 0')
  ensureColumn(db, 'targets', 'miss_penalty', 'INTEGER NOT NULL DEFAULT 0')
  ensureColumn(db, 'targets', 'hit_bonus', 'INTEGER NOT NULL DEFAULT 0')
}

function ensureColumn(db: Database, table: string, column: string, definition: string) {
  const rows = db.exec(`PRAGMA table_info(${table})`)
  const cols = rows[0]?.values.map((v) => String(v[1]).toLowerCase()) ?? []
  if (!cols.includes(column.toLowerCase())) {
    db.run(`ALTER TABLE ${table} ADD COLUMN ${column} ${definition}`)
  }
}

function writeAll(db: Database, state: AppState) {
  db.run('BEGIN')
  try {
    for (const table of [
      'classification_conditions',
      'classification_rules',
      'targets',
      'clusters',
      'shooters',
      'sessions',
      'groups',
      'presets',
      'meta',
    ]) {
      db.run(`DELETE FROM ${table}`)
    }

    setMeta(db, 'active_group_id', state.activeGroupId ?? '')
    setMeta(db, 'active_session_id', state.activeSessionId ?? '')
    setMeta(db, 'schema_version', '2')

    state.presets.forEach((preset, pi) => {
      db.run('INSERT INTO presets(id, name, sort_order) VALUES (?, ?, ?)', [
        preset.id,
        preset.name ?? '',
        pi,
      ])

      preset.clusters.forEach((cluster, ci) => {
        db.run('INSERT INTO clusters(id, preset_id, name, sort_order) VALUES (?, ?, ?, ?)', [
          cluster.id,
          preset.id,
          cluster.name ?? '',
          ci,
        ])
        cluster.targets.forEach((t, ti) => {
          db.run(
            'INSERT INTO targets(cluster_id, name, round_count, kind, miss_penalty, hit_bonus, sort_order) VALUES (?, ?, ?, ?, ?, ?, ?)',
            [
              cluster.id,
              t.name ?? '',
              t.roundCount,
              t.kind,
              t.kind === TargetKind.KnockDown ? Math.max(0, t.missPenalty) : 0,
              t.kind === TargetKind.KnockDown ? Math.max(0, t.hitBonus) : 0,
              ti,
            ],
          )
        })
      })

      preset.classificationRules.forEach((rule, ri) => {
        const r = ensureLegacyCondition(rule)
        db.run(
          'INSERT INTO classification_rules(preset_id, label, min_score, priority, sort_order) VALUES (?, ?, ?, ?, ?)',
          [preset.id, r.label ?? '', r.minScore, r.priority, ri],
        )
        const ruleId = Number(db.exec('SELECT last_insert_rowid()')[0]?.values[0]?.[0] ?? 0)
        r.conditions.forEach((c, cdi) => {
          db.run(
            'INSERT INTO classification_conditions(rule_id, kind, target_index, min_value, sort_order) VALUES (?, ?, ?, ?, ?)',
            [ruleId, c.kind, c.targetIndex, c.minValue, cdi],
          )
        })
      })
    })

    state.groups.forEach((g, gi) => {
      db.run('INSERT INTO groups(id, name, preset_id, sort_order) VALUES (?, ?, ?, ?)', [
        g.id,
        g.name ?? '',
        g.presetId,
        gi,
      ])
      g.shooters.forEach((s, si) => insertShooter(db, s, null, g.id, si))
    })

    state.sessions.forEach((s, sei) => {
      db.run(
        'INSERT INTO sessions(id, name, group_id, created_at, sort_order) VALUES (?, ?, ?, ?, ?)',
        [s.id, s.name ?? '', s.groupId, s.createdAt, sei],
      )
      s.shooters.forEach((sh, si) => insertShooter(db, sh, s.id, null, si))
    })

    db.run('COMMIT')
  } catch (e) {
    db.run('ROLLBACK')
    throw e
  }
}

function insertShooter(
  db: Database,
  shooter: Shooter,
  sessionId: string | null,
  groupId: string | null,
  sortOrder: number,
) {
  db.run(
    `INSERT INTO shooters(id, session_id, group_id, name, rank, position, unit, sort_order, is_selected, shots_json)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    [
      shooter.id,
      sessionId,
      groupId,
      shooter.name ?? '',
      shooter.rank ?? '',
      shooter.position ?? '',
      shooter.unit ?? '',
      sortOrder,
      shooter.isSelected ? 1 : 0,
      JSON.stringify(shooter.shots ?? []),
    ],
  )
}

function setMeta(db: Database, key: string, value: string) {
  db.run('INSERT INTO meta(key, value) VALUES (?, ?)', [key, value])
}

function getMeta(db: Database, key: string): string {
  const stmt = db.prepare('SELECT value FROM meta WHERE key = ?')
  try {
    stmt.bind([key])
    if (stmt.step()) {
      const row = stmt.get()
      return String(row[0] ?? '')
    }
    return ''
  } finally {
    stmt.free()
  }
}

function readAll(db: Database): AppState {
  const state: AppState = {
    presets: [],
    groups: [],
    sessions: [],
    activeGroupId: parseGuidOrNull(getMeta(db, 'active_group_id')),
    activeSessionId: parseGuidOrNull(getMeta(db, 'active_session_id')),
  }

  const presets = new Map<string, ScorePreset>()
  const legacyPenalties = new Map<string, number>()

  const hasLegacyCol = tableHasColumn(db, 'presets', 'knock_down_miss_penalty')
  const presetSql = hasLegacyCol
    ? 'SELECT id, name, knock_down_miss_penalty FROM presets ORDER BY sort_order, name'
    : 'SELECT id, name FROM presets ORDER BY sort_order, name'

  for (const row of query(db, presetSql)) {
    const id = String(row[0])
    const name = String(row[1] ?? '')
    const legacy = hasLegacyCol ? Math.max(0, Number(row[2] ?? 0)) : 0
    const p: ScorePreset = { id, name, clusters: [], classificationRules: [] }
    if (legacy > 0) legacyPenalties.set(id, legacy)
    presets.set(id, p)
    state.presets.push(p)
  }

  const clustersByPreset = new Map<string, { id: string; name: string; ord: number }[]>()
  for (const row of query(db, 'SELECT id, preset_id, name, sort_order FROM clusters ORDER BY sort_order')) {
    const cid = String(row[0])
    const pid = String(row[1])
    const list = clustersByPreset.get(pid) ?? []
    list.push({ id: cid, name: String(row[2] ?? ''), ord: Number(row[3] ?? 0) })
    clustersByPreset.set(pid, list)
  }

  const targetsByCluster = new Map<
    string,
    { name: string; roundCount: number; kind: TargetKind; miss: number; bonus: number; ord: number }[]
  >()
  for (const row of query(
    db,
    'SELECT cluster_id, name, round_count, kind, miss_penalty, hit_bonus, sort_order FROM targets ORDER BY sort_order',
  )) {
    const cid = String(row[0])
    const list = targetsByCluster.get(cid) ?? []
    list.push({
      name: String(row[1] ?? ''),
      roundCount: Number(row[2] ?? 5),
      kind: Number(row[3] ?? 0) as TargetKind,
      miss: Math.max(0, Number(row[4] ?? 0)),
      bonus: Math.max(0, Number(row[5] ?? 0)),
      ord: Number(row[6] ?? 0),
    })
    targetsByCluster.set(cid, list)
  }

  for (const [presetId, clusterRows] of clustersByPreset) {
    const preset = presets.get(presetId)
    if (!preset) continue
    const legacy = legacyPenalties.get(presetId) ?? 0
    for (const c of [...clusterRows].sort((a, b) => a.ord - b.ord)) {
      const cluster: TargetCluster = { id: c.id, name: c.name, targets: [] }
      const targets = targetsByCluster.get(c.id) ?? []
      for (const t of [...targets].sort((a, b) => a.ord - b.ord)) {
        let miss = t.miss
        if (t.kind === TargetKind.KnockDown && miss <= 0 && legacy > 0) miss = legacy
        const def: TargetDefinition = {
          name: t.name,
          roundCount: t.roundCount,
          kind: t.kind,
          missPenalty: t.kind === TargetKind.KnockDown ? miss : 0,
          hitBonus: t.kind === TargetKind.KnockDown ? t.bonus : 0,
        }
        cluster.targets.push(def)
      }
      preset.clusters.push(cluster)
    }
  }

  const rulesByPreset = new Map<
    string,
    { ruleId: number; label: string; minScore: number; priority: number; ord: number }[]
  >()
  for (const row of query(
    db,
    'SELECT id, preset_id, label, min_score, priority, sort_order FROM classification_rules ORDER BY sort_order',
  )) {
    const pid = String(row[1])
    const list = rulesByPreset.get(pid) ?? []
    list.push({
      ruleId: Number(row[0]),
      label: String(row[2] ?? ''),
      minScore: Number(row[3] ?? 0),
      priority: Number(row[4] ?? 0),
      ord: Number(row[5] ?? 0),
    })
    rulesByPreset.set(pid, list)
  }

  const conditionsByRule = new Map<
    number,
    { kind: ClassificationConditionKind; targetIndex: number; minValue: number; ord: number }[]
  >()
  for (const row of query(
    db,
    'SELECT rule_id, kind, target_index, min_value, sort_order FROM classification_conditions ORDER BY sort_order',
  )) {
    const rid = Number(row[0])
    const list = conditionsByRule.get(rid) ?? []
    list.push({
      kind: Number(row[1] ?? 0) as ClassificationConditionKind,
      targetIndex: Number(row[2] ?? -1),
      minValue: Number(row[3] ?? 0),
      ord: Number(row[4] ?? 0),
    })
    conditionsByRule.set(rid, list)
  }

  for (const [presetId, ruleRows] of rulesByPreset) {
    const preset = presets.get(presetId)
    if (!preset) continue
    for (const row of [...ruleRows].sort((a, b) => a.ord - b.ord)) {
      const rule: ClassificationRule = {
        label: row.label,
        minScore: row.minScore,
        priority: row.priority,
        conditions: [],
      }
      const conds = conditionsByRule.get(row.ruleId) ?? []
      for (const c of [...conds].sort((a, b) => a.ord - b.ord)) {
        const condition: ClassificationCondition = {
          kind: c.kind,
          targetIndex: c.targetIndex,
          minValue: c.minValue,
        }
        rule.conditions.push(condition)
      }
      preset.classificationRules.push(ensureLegacyCondition(rule))
    }
  }

  for (const row of query(db, 'SELECT id, name, preset_id FROM groups ORDER BY sort_order, name')) {
    const g: Group = {
      id: String(row[0]),
      name: String(row[1] ?? ''),
      presetId: String(row[2]),
      shooters: [],
    }
    state.groups.push(g)
  }

  const groupsById = new Map(state.groups.map((g) => [g.id, g]))

  for (const row of query(
    db,
    'SELECT id, name, group_id, created_at FROM sessions ORDER BY sort_order, created_at',
  )) {
    const s: ShootingSession = {
      id: String(row[0]),
      name: String(row[1] ?? ''),
      groupId: String(row[2]),
      createdAt: String(row[3] ?? new Date().toISOString()),
      shooters: [],
    }
    state.sessions.push(s)
  }

  const sessionsById = new Map(state.sessions.map((s) => [s.id, s]))

  for (const row of query(
    db,
    'SELECT id, session_id, group_id, name, rank, position, unit, sort_order, is_selected, shots_json FROM shooters ORDER BY sort_order',
  )) {
    const shooter: Shooter = {
      id: String(row[0]),
      name: row[3] == null ? '' : String(row[3]),
      rank: row[4] == null ? '' : String(row[4]),
      position: row[5] == null ? '' : String(row[5]),
      unit: row[6] == null ? '' : String(row[6]),
      order: Number(row[7] ?? 0),
      isSelected: Number(row[8] ?? 0) !== 0,
      shots: deserializeShots(row[9] == null ? '[]' : String(row[9])),
    }

    const sid = row[1] == null ? null : String(row[1])
    const gid = row[2] == null ? null : String(row[2])
    if (sid && sessionsById.has(sid)) {
      sessionsById.get(sid)!.shooters.push(shooter)
    } else if (gid && groupsById.has(gid)) {
      groupsById.get(gid)!.shooters.push(shooter)
    }
  }

  return state
}

function query(db: Database, sql: string): unknown[][] {
  const result = db.exec(sql)
  return result[0]?.values ?? []
}

function tableHasColumn(db: Database, table: string, column: string): boolean {
  const rows = db.exec(`PRAGMA table_info(${table})`)
  const cols = rows[0]?.values.map((v) => String(v[1]).toLowerCase()) ?? []
  return cols.includes(column.toLowerCase())
}

function deserializeShots(json: string): (number | null)[][] {
  try {
    const parsed = JSON.parse(json) as (number | null)[][]
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

function parseGuidOrNull(s: string): string | null {
  return s && s.trim() ? s.trim() : null
}

function ensureShotMatrices(state: AppState) {
  const presets = new Map(state.presets.map((p) => [p.id, p]))
  const groups = new Map(state.groups.map((g) => [g.id, g]))

  for (const group of state.groups) {
    const preset = presets.get(group.presetId)
    if (!preset) continue
    const rounds = getRoundCounts(preset)
    group.shooters = group.shooters.map((s) => ensureShotMatrix(s, rounds))
  }

  for (const session of state.sessions) {
    const group = groups.get(session.groupId)
    if (!group) continue
    const preset = presets.get(group.presetId)
    if (!preset) continue
    const rounds = getRoundCounts(preset)
    session.shooters = session.shooters.map((s) => ensureShotMatrix(s, rounds))
  }
}

export function emptyDefaultState(): AppState {
  return createDefaultState()
}
