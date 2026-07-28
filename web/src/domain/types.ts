export const TargetKind = {
  Scored: 0,
  KnockDown: 1,
} as const
export type TargetKind = (typeof TargetKind)[keyof typeof TargetKind]

export const ClassificationConditionKind = {
  TotalScore: 0,
  TargetScore: 1,
  TargetKnockDown: 2,
} as const
export type ClassificationConditionKind =
  (typeof ClassificationConditionKind)[keyof typeof ClassificationConditionKind]

export interface TargetDefinition {
  name: string
  roundCount: number
  kind: TargetKind
  missPenalty: number
  hitBonus: number
}

export interface TargetCluster {
  id: string
  name: string
  targets: TargetDefinition[]
}

export interface ClassificationCondition {
  kind: ClassificationConditionKind
  targetIndex: number
  minValue: number
}

export interface ClassificationRule {
  label: string
  minScore: number
  priority: number
  conditions: ClassificationCondition[]
}

export interface ScorePreset {
  id: string
  name: string
  clusters: TargetCluster[]
  classificationRules: ClassificationRule[]
}

export interface Shooter {
  id: string
  name: string
  rank: string
  position: string
  unit: string
  order: number
  isSelected: boolean
  /** shots[targetIndex][roundIndex]; null = chưa nhập */
  shots: (number | null)[][]
}

export interface Group {
  id: string
  name: string
  presetId: string
  shooters: Shooter[]
}

export interface ShootingSession {
  id: string
  name: string
  groupId: string
  createdAt: string
  shooters: Shooter[]
}

export interface AppState {
  presets: ScorePreset[]
  groups: Group[]
  sessions: ShootingSession[]
  activeGroupId: string | null
  activeSessionId: string | null
}

export function newId(): string {
  return crypto.randomUUID()
}

export function flatTargets(preset: ScorePreset): TargetDefinition[] {
  return preset.clusters.flatMap((c) => c.targets)
}

export function effectiveRoundCount(t: TargetDefinition): number {
  return t.kind === TargetKind.KnockDown ? 1 : Math.max(1, t.roundCount)
}

export function getRoundCounts(preset: ScorePreset): number[] {
  return flatTargets(preset).map(effectiveRoundCount)
}

export function createDefaultTarget(name: string, rounds = 5): TargetDefinition {
  return {
    name,
    roundCount: rounds,
    kind: TargetKind.Scored,
    missPenalty: 0,
    hitBonus: 0,
  }
}

export function createEmptyShooter(order: number, rounds: number[]): Shooter {
  return {
    id: newId(),
    name: '',
    rank: '',
    position: '',
    unit: '',
    order,
    isSelected: false,
    shots: rounds.map((r) => Array.from({ length: Math.max(0, r) }, () => null)),
  }
}

export function ensureShotMatrix(shooter: Shooter, rounds: number[]): Shooter {
  const shots = rounds.map((r, i) => {
    const existing = shooter.shots[i] ?? []
    const next = existing.slice(0, Math.max(0, r))
    while (next.length < r) next.push(null)
    return next
  })
  return { ...shooter, shots }
}

export function targetTotal(shooter: Shooter, targetIndex: number): number {
  const row = shooter.shots[targetIndex]
  if (!row) return 0
  let sum = 0
  for (const v of row) {
    if (v !== null) sum += v
  }
  return sum
}

export function formatTargetScores(
  shooter: Shooter,
  targetIndex: number,
  kind: TargetKind = TargetKind.Scored,
): string {
  const row = shooter.shots[targetIndex]
  if (!row) return ''
  if (kind === TargetKind.KnockDown) {
    const v = row[0]
    if (v === 1) return 'Đổ'
    if (v === 0) return 'Không'
    return ''
  }
  const entered = row.filter((s): s is number => s !== null)
  return entered.length === 0 ? '' : entered.join(',')
}

export function createDefaultPreset(name = 'Nhóm 1'): ScorePreset {
  return {
    id: newId(),
    name,
    clusters: [
      {
        id: newId(),
        name: 'Phần 1',
        targets: [createDefaultTarget('Bia 1'), createDefaultTarget('Bia 2')],
      },
    ],
    classificationRules: [],
  }
}

export function createDefaultState(): AppState {
  const preset = createDefaultPreset('Nhóm 1')
  const group: Group = {
    id: newId(),
    name: 'Nhóm 1',
    presetId: preset.id,
    shooters: [],
  }
  return {
    presets: [preset],
    groups: [group],
    sessions: [],
    activeGroupId: group.id,
    activeSessionId: null,
  }
}

export function clonePreset(preset: ScorePreset, name?: string): ScorePreset {
  return {
    id: newId(),
    name: name ?? preset.name,
    clusters: preset.clusters.map((c) => ({
      id: newId(),
      name: c.name,
      targets: c.targets.map((t) => ({ ...t })),
    })),
    classificationRules: preset.classificationRules.map((r) => ({
      ...r,
      conditions: r.conditions.map((c) => ({ ...c })),
    })),
  }
}

export function isConditionTotal(c: ClassificationCondition): boolean {
  return c.kind === ClassificationConditionKind.TotalScore || c.targetIndex < 0
}

export function isConditionKnockDown(c: ClassificationCondition): boolean {
  return c.kind === ClassificationConditionKind.TargetKnockDown
}

export function ensureLegacyCondition(rule: ClassificationRule): ClassificationRule {
  if (rule.conditions.length > 0) return rule
  return {
    ...rule,
    conditions: [
      {
        kind: ClassificationConditionKind.TotalScore,
        targetIndex: -1,
        minValue: rule.minScore,
      },
    ],
    priority: rule.priority !== 0 ? rule.priority : rule.minScore,
  }
}

export function effectivePriority(rule: ClassificationRule): number {
  const r = ensureLegacyCondition(rule)
  if (r.priority !== 0) return r.priority
  const scored = r.conditions.filter((c) => !isConditionKnockDown(c))
  if (scored.length > 0) return Math.max(...scored.map((c) => c.minValue))
  return r.minScore
}

export function describeCondition(
  c: ClassificationCondition,
  targets?: TargetDefinition[],
): string {
  if (isConditionTotal(c)) return `Tổng ≥ ${c.minValue}`
  const name =
    targets && c.targetIndex >= 0 && c.targetIndex < targets.length
      ? targets[c.targetIndex].name
      : `Bia ${c.targetIndex + 1}`
  const isKd =
    isConditionKnockDown(c) ||
    (targets != null &&
      c.targetIndex >= 0 &&
      c.targetIndex < targets.length &&
      targets[c.targetIndex].kind === TargetKind.KnockDown)
  if (isKd) return c.minValue >= 1 ? `${name} = Đổ` : `${name} = Không đổ`
  return `${name} ≥ ${c.minValue}`
}

export function conditionSummary(rule: ClassificationRule, targets?: TargetDefinition[]): string {
  const r = ensureLegacyCondition(rule)
  if (r.conditions.length === 0) return `Tổng ≥ ${r.minScore}`
  return r.conditions.map((c) => describeCondition(c, targets)).join('  và  ')
}
