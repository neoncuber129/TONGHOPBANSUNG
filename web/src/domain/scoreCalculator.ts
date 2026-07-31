import {
  TargetKind,
  effectivePriority,
  ensureLegacyCondition,
  flatTargets,
  isConditionKnockDown,
  isConditionTotal,
  targetTotal,
  type ClassificationRule,
  type ScorePreset,
  type Shooter,
} from './types'

export function totalScore(shooter: Shooter, preset: ScorePreset): number {
  const flat = flatTargets(preset)
  let total = 0
  for (let i = 0; i < flat.length && i < shooter.shots.length; i++) {
    const def = flat[i]
    if (def.kind === TargetKind.KnockDown) {
      const v = shooter.shots[i]?.[0]
      if (v === 1) total += Math.max(0, def.hitBonus)
      else if (v === 0) total -= Math.max(0, def.missPenalty)
      continue
    }
    for (const s of shooter.shots[i] ?? []) {
      if (s !== null) total += s
    }
  }
  return total
}

export function knockDownCount(shooter: Shooter, preset: ScorePreset): number {
  const flat = flatTargets(preset)
  let count = 0
  for (let i = 0; i < flat.length && i < shooter.shots.length; i++) {
    if (flat[i].kind !== TargetKind.KnockDown) continue
    if (targetTotal(shooter, i) >= 1) count++
  }
  return count
}

export function matchesRule(
  shooter: Shooter,
  rule: ClassificationRule,
  preset: ScorePreset,
): boolean {
  const r = ensureLegacyCondition(rule)
  const targets = flatTargets(preset)

  for (const condition of r.conditions) {
    if (isConditionTotal(condition)) {
      if (totalScore(shooter, preset) < condition.minValue) return false
      continue
    }

    const targetIndex = condition.targetIndex
    if (targetIndex < 0 || targetIndex >= targets.length) return false

    const def = targets[targetIndex]
    const isKnockDown =
      isConditionKnockDown(condition) || def.kind === TargetKind.KnockDown
    const value = targetTotal(shooter, targetIndex)

    if (isKnockDown) {
      const required = condition.minValue >= 1 ? 1 : 0
      if (value !== required) return false
    } else if (value < condition.minValue) {
      return false
    }
  }

  return r.conditions.length > 0
}

export function classify(shooter: Shooter, preset: ScorePreset): string {
  const ordered = [...preset.classificationRules].sort((a, b) => {
    const pa = effectivePriority(a)
    const pb = effectivePriority(b)
    if (pb !== pa) return pb - pa
    return a.label.localeCompare(b.label, 'vi', { sensitivity: 'base' })
  })

  if (ordered.length === 0) return ''

  for (const rule of ordered) {
    if (matchesRule(shooter, rule, preset)) {
      return rule.label.trim() || ''
    }
  }
  return ''
}

export function enteredShotCount(shooter: Shooter): number {
  return shooter.shots.flat().filter((s) => s !== null).length
}

/** Đếm số lần từng điểm chấm (0–10), bỏ ô trống và bia đổ. */
export function scoreValueCounts(
  shooter: Shooter,
  preset: ScorePreset,
): { value: number; count: number }[] {
  const flat = flatTargets(preset)
  const map = new Map<number, number>()
  for (let i = 0; i < flat.length && i < shooter.shots.length; i++) {
    if (flat[i].kind === TargetKind.KnockDown) continue
    for (const s of shooter.shots[i] ?? []) {
      if (s === null) continue
      map.set(s, (map.get(s) ?? 0) + 1)
    }
  }
  return [...map.entries()]
    .sort((a, b) => b[0] - a[0])
    .map(([value, count]) => ({ value, count }))
}

export function progressLabel(shooter: Shooter, preset: ScorePreset): string {
  const totalSlots = flatTargets(preset).reduce(
    (n, t) => n + (t.kind === TargetKind.KnockDown ? 1 : Math.max(1, t.roundCount)),
    0,
  )
  const entered = enteredShotCount(shooter)
  return `${entered}/${totalSlots}`
}
