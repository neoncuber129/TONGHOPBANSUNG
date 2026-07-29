import {
  buildSessionTransfer,
  parseSessionTransfer,
  presetsMatchExactly,
  createSessionFromTransfer,
  appendShootersFromTransfer,
  findMatchingGroups,
} from '../src/domain/sessionTransfer.ts'
import {
  ClassificationConditionKind,
  createDefaultPreset,
  createEmptyShooter,
  getRoundCounts,
  newId,
  type Group,
  type ShootingSession,
} from '../src/domain/types.ts'

const preset = createDefaultPreset('Nhóm 1')
const rounds = getRoundCounts(preset)
const group: Group = { id: newId(), name: 'Nhóm 1', presetId: preset.id, shooters: [] }
const session: ShootingSession = {
  id: newId(),
  name: 'Đợt A',
  groupId: group.id,
  createdAt: new Date().toISOString(),
  shooters: [
    {
      ...createEmptyShooter(1, rounds),
      name: 'An',
      shots: rounds.map((r) => Array.from({ length: r }, (_, i) => (i === 0 ? 10 : null))),
    },
  ],
}

const pack = buildSessionTransfer(session, preset, group)
const parsed = parseSessionTransfer(JSON.parse(JSON.stringify(pack)))
if (!presetsMatchExactly(parsed.preset, preset)) throw new Error('fingerprint mismatch')

const created = createSessionFromTransfer(parsed, group.id)
if (created.id === session.id) throw new Error('session id not reminted')
if (created.shooters[0].id === session.shooters[0].id) throw new Error('shooter id not reminted')
if (created.shooters[0].name !== 'An') throw new Error('name lost')
if (created.shooters[0].shots[0][0] !== 10) throw new Error('score lost')

const other = structuredClone(preset)
other.name = 'Khác'
if (!presetsMatchExactly(other, preset)) throw new Error('preset name should be ignored')

const renamedConfigItems = structuredClone(preset)
renamedConfigItems.clusters[0].name = `  ${renamedConfigItems.clusters[0].name}  `
renamedConfigItems.clusters[0].targets[0].name =
  `  ${renamedConfigItems.clusters[0].targets[0].name}  `
if (!presetsMatchExactly(renamedConfigItems, preset)) {
  throw new Error('configuration labels should be trimmed')
}

const legacyPreset = structuredClone(preset)
legacyPreset.classificationRules = [
  { label: 'Đạt', minScore: 50, priority: 0, conditions: [] },
]
const normalizedLegacyPreset = structuredClone(legacyPreset)
normalizedLegacyPreset.classificationRules[0].priority = 50
normalizedLegacyPreset.classificationRules[0].conditions = [
  {
    kind: ClassificationConditionKind.TotalScore,
    targetIndex: -1,
    minValue: 50,
  },
]
if (!presetsMatchExactly(legacyPreset, normalizedLegacyPreset)) {
  throw new Error('legacy classification rule should normalize without mutation')
}
if (legacyPreset.classificationRules[0].conditions.length !== 0) {
  throw new Error('matching should not mutate legacy rules')
}

const different = structuredClone(preset)
different.clusters[0].targets[0].roundCount++
if (presetsMatchExactly(different, preset)) throw new Error('layout change should mismatch')

const groups = findMatchingGroups([group], [preset], parsed.preset)
if (groups.length !== 1) throw new Error('matching group missing')

const appended = appendShootersFromTransfer(session, parsed, preset)
if (appended.length !== 2) throw new Error('append count wrong')
if (appended[0].id !== session.shooters[0].id) throw new Error('existing shooter replaced')
if (appended[0].shots[0][0] !== 10) throw new Error('existing score changed')
if (appended[1].order !== 2) throw new Error('order wrong')
if (appended[1].id === session.shooters[0].id) throw new Error('appended id not reminted')

let rejected = false
try {
  parseSessionTransfer({ format: 'x', version: 1 })
} catch {
  rejected = true
}
if (!rejected) throw new Error('bad format should reject')

const invalidScore = structuredClone(pack)
invalidScore.session.shooters[0].shots[0][0] = 11
rejected = false
try {
  parseSessionTransfer(invalidScore)
} catch {
  rejected = true
}
if (!rejected) throw new Error('invalid score should reject')

console.log('sessionTransfer checks OK')
