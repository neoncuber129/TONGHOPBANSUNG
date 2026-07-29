import {
  TargetKind,
  ensureLegacyCondition,
  ensureShotMatrix,
  flatTargets,
  getRoundCounts,
  newId,
  type Group,
  type ScorePreset,
  type Shooter,
  type ShootingSession,
} from './types'

export const SESSION_TRANSFER_FORMAT = 'tonghopbansung.session'
export const SESSION_TRANSFER_VERSION = 1
export const SESSION_TRANSFER_EXT = '.thbss'

export interface SessionTransferFile {
  format: typeof SESSION_TRANSFER_FORMAT
  version: number
  exportedAt: string
  preset: ScorePreset
  session: {
    id: string
    name: string
    createdAt: string
    shooters: Shooter[]
  }
  sourceGroup?: {
    id: string
    name: string
  }
}

/** Chuẩn hóa preset để so khớp cấu hình, bỏ qua UUID và tên preset. */
export function presetFingerprint(preset: ScorePreset): string {
  const clusters = preset.clusters.map((c) => ({
    name: c.name.trim(),
    targets: c.targets.map((t) => ({
      name: t.name.trim(),
      roundCount: t.roundCount,
      kind: t.kind,
      missPenalty: t.missPenalty,
      hitBonus: t.hitBonus,
    })),
  }))
  const rules = preset.classificationRules.map((source) => {
    const r = ensureLegacyCondition(source)
    return {
      label: r.label.trim(),
      minScore: r.minScore,
      priority: r.priority,
      conditions: r.conditions.map((c) => ({
        kind: c.kind,
        targetIndex: c.targetIndex,
        minValue: c.minValue,
      })),
    }
  })
  return JSON.stringify({
    clusters,
    classificationRules: rules,
  })
}

export function presetsMatchExactly(a: ScorePreset, b: ScorePreset): boolean {
  return presetFingerprint(a) === presetFingerprint(b)
}

function requireInteger(value: unknown, field: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value)) {
    throw new Error(`${field} không hợp lệ.`)
  }
  return value
}

function validatePreset(raw: unknown): ScorePreset {
  if (!raw || typeof raw !== 'object') {
    throw new Error('File thiếu cấu hình bia (preset).')
  }
  const preset = raw as Partial<ScorePreset>
  if (!Array.isArray(preset.clusters) || !Array.isArray(preset.classificationRules)) {
    throw new Error('Cấu trúc preset trong file không hợp lệ.')
  }
  for (const [ci, cluster] of preset.clusters.entries()) {
    if (
      !cluster ||
      typeof cluster !== 'object' ||
      typeof cluster.name !== 'string' ||
      !Array.isArray(cluster.targets)
    ) {
      throw new Error(`Phần bia #${ci + 1} không hợp lệ.`)
    }
    for (const [ti, target] of cluster.targets.entries()) {
      if (
        !target ||
        typeof target !== 'object' ||
        typeof target.name !== 'string' ||
        (target.kind !== TargetKind.Scored && target.kind !== TargetKind.KnockDown)
      ) {
        throw new Error(`Bia #${ti + 1} trong phần #${ci + 1} không hợp lệ.`)
      }
      requireInteger(target.roundCount, `Số phát bia #${ti + 1}`)
      requireInteger(target.missPenalty, `Điểm phạt bia #${ti + 1}`)
      requireInteger(target.hitBonus, `Điểm thưởng bia #${ti + 1}`)
    }
  }
  for (const [ri, rule] of preset.classificationRules.entries()) {
    if (
      !rule ||
      typeof rule !== 'object' ||
      typeof rule.label !== 'string' ||
      !Array.isArray(rule.conditions)
    ) {
      throw new Error(`Quy tắc xếp loại #${ri + 1} không hợp lệ.`)
    }
    requireInteger(rule.minScore, `Ngưỡng xếp loại #${ri + 1}`)
    requireInteger(rule.priority, `Ưu tiên xếp loại #${ri + 1}`)
    for (const condition of rule.conditions) {
      if (!condition || typeof condition !== 'object') {
        throw new Error(`Điều kiện xếp loại #${ri + 1} không hợp lệ.`)
      }
      requireInteger(condition.kind, `Loại điều kiện xếp loại #${ri + 1}`)
      requireInteger(condition.targetIndex, `Bia điều kiện xếp loại #${ri + 1}`)
      requireInteger(condition.minValue, `Ngưỡng điều kiện xếp loại #${ri + 1}`)
    }
  }
  return raw as ScorePreset
}

export function buildSessionTransfer(
  session: ShootingSession,
  preset: ScorePreset,
  group?: Group | null,
): SessionTransferFile {
  return parseSessionTransfer({
    format: SESSION_TRANSFER_FORMAT,
    version: SESSION_TRANSFER_VERSION,
    exportedAt: new Date().toISOString(),
    preset: structuredClone(preset),
    session: {
      id: session.id,
      name: session.name,
      createdAt: session.createdAt,
      shooters: structuredClone(session.shooters),
    },
    sourceGroup: group
      ? {
          id: group.id,
          name: group.name,
        }
      : undefined,
  })
}

export function parseSessionTransfer(raw: unknown): SessionTransferFile {
  if (!raw || typeof raw !== 'object') {
    throw new Error('File đợt bắn không hợp lệ.')
  }
  const obj = raw as Record<string, unknown>
  if (obj.format !== SESSION_TRANSFER_FORMAT) {
    throw new Error(
      'File không phải định dạng đợt bắn (.thbss). Dùng tab Sao lưu nếu đây là file .thbs.',
    )
  }
  if (obj.version !== SESSION_TRANSFER_VERSION) {
    throw new Error(`Phiên bản file đợt bắn không hỗ trợ (version=${String(obj.version)}).`)
  }
  if (!obj.session || typeof obj.session !== 'object') {
    throw new Error('File thiếu dữ liệu đợt bắn.')
  }

  const preset = validatePreset(obj.preset)
  const session = obj.session as SessionTransferFile['session']
  if (!Array.isArray(session.shooters)) {
    throw new Error('Danh sách xạ thủ không hợp lệ.')
  }
  if (typeof session.name !== 'string' || !session.name.trim()) {
    throw new Error('Tên đợt trong file không hợp lệ.')
  }
  const targets = flatTargets(preset)
  if (targets.length === 0) {
    throw new Error('Preset trong file không có bia.')
  }

  const rounds = getRoundCounts(preset)
  for (let i = 0; i < session.shooters.length; i++) {
    const s = session.shooters[i]
    if (!s || typeof s !== 'object') {
      throw new Error(`Xạ thủ #${i + 1} không hợp lệ.`)
    }
    if (!Array.isArray(s.shots)) {
      throw new Error(`Xạ thủ #${i + 1} thiếu ma trận điểm.`)
    }
    if (s.shots.length !== rounds.length) {
      throw new Error(
        `Xạ thủ #${i + 1}: số bia điểm (${s.shots.length}) không khớp preset (${rounds.length}).`,
      )
    }
    for (let ti = 0; ti < rounds.length; ti++) {
      const row = s.shots[ti]
      if (!Array.isArray(row) || row.length !== rounds[ti]) {
        throw new Error(
          `Xạ thủ #${i + 1}: bia ${ti + 1} có ${Array.isArray(row) ? row.length : 0} phát, cần ${rounds[ti]}.`,
        )
      }
      for (const score of row) {
        if (score === null) continue
        const target = targets[ti]
        const valid =
          typeof score === 'number' &&
          Number.isInteger(score) &&
          (target.kind === TargetKind.KnockDown
            ? score === 0 || score === 1
            : score >= 0 && score <= 10)
        if (!valid) {
          throw new Error(`Xạ thủ #${i + 1}: điểm không hợp lệ tại bia ${ti + 1}.`)
        }
      }
    }
  }

  const sourceGroup =
    obj.sourceGroup && typeof obj.sourceGroup === 'object'
      ? (obj.sourceGroup as SessionTransferFile['sourceGroup'])
      : undefined

  return {
    format: SESSION_TRANSFER_FORMAT,
    version: SESSION_TRANSFER_VERSION,
    exportedAt: typeof obj.exportedAt === 'string' ? obj.exportedAt : new Date().toISOString(),
    preset,
    session: {
      id: typeof session.id === 'string' ? session.id : newId(),
      name: session.name.trim(),
      createdAt:
        typeof session.createdAt === 'string' ? session.createdAt : new Date().toISOString(),
      shooters: session.shooters,
    },
    sourceGroup,
  }
}

export async function readSessionTransferFile(file: File): Promise<SessionTransferFile> {
  const text = await file.text()
  let raw: unknown
  try {
    raw = JSON.parse(text)
  } catch {
    throw new Error('Không đọc được JSON file đợt bắn.')
  }
  return parseSessionTransfer(raw)
}

export function downloadSessionTransfer(pack: SessionTransferFile): void {
  const safe = pack.session.name.replace(/[\\/:*?"<>|]+/g, '_').trim() || 'dot'
  const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, (c) => (c === 'T' ? '_' : ''))
  const blob = new Blob([JSON.stringify(pack, null, 2)], {
    type: 'application/json;charset=utf-8',
  })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${safe}_${stamp}${SESSION_TRANSFER_EXT}`
  a.click()
  URL.revokeObjectURL(url)
}

export function remintShooters(shooters: Shooter[], startOrder: number, rounds: number[]): Shooter[] {
  return shooters.map((s, i) =>
    ensureShotMatrix(
      {
        ...structuredClone(s),
        id: newId(),
        order: startOrder + i,
        isSelected: false,
      },
      rounds,
    ),
  )
}

export function findMatchingGroups(
  groups: Group[],
  presets: ScorePreset[],
  importedPreset: ScorePreset,
): Group[] {
  return groups.filter((g) => {
    const p = presets.find((x) => x.id === g.presetId)
    return p ? presetsMatchExactly(p, importedPreset) : false
  })
}

export function createSessionFromTransfer(
  pack: SessionTransferFile,
  targetGroupId: string,
): ShootingSession {
  const rounds = getRoundCounts(pack.preset)
  return {
    id: newId(),
    name: pack.session.name,
    groupId: targetGroupId,
    createdAt: new Date().toISOString(),
    shooters: remintShooters(pack.session.shooters, 1, rounds),
  }
}

export function appendShootersFromTransfer(
  current: ShootingSession,
  pack: SessionTransferFile,
  localPreset: ScorePreset,
): Shooter[] {
  const rounds = getRoundCounts(localPreset)
  const existing = current.shooters.map((s, i) => ({ ...s, order: i + 1 }))
  const imported = remintShooters(pack.session.shooters, existing.length + 1, rounds)
  return [...existing, ...imported]
}
