import { useMemo, useState } from 'react'
import {
  ClassificationConditionKind,
  TargetKind,
  conditionSummary,
  createDefaultTarget,
  flatTargets,
  newId,
  type ClassificationCondition,
  type ClassificationRule,
  type ScorePreset,
  type TargetDefinition,
} from '../domain/types'
import { Modal } from './Modal'

type Step = 'name' | 'clusters' | 'targets' | 'detail'

interface ClusterSetup {
  name: string
  targetCount: number
}

export function PresetEditorDialog({
  initial,
  onSave,
  onCancel,
}: {
  initial: ScorePreset
  onSave: (preset: ScorePreset) => void
  onCancel: () => void
}) {
  const hasLayout = flatTargets(initial).length > 0
  const [step, setStep] = useState<Step>(hasLayout ? 'detail' : 'name')
  const [name, setName] = useState(initial.name)
  const [clusterCount, setClusterCount] = useState(
    Math.max(1, initial.clusters.length || 1),
  )
  const [setups, setSetups] = useState<ClusterSetup[]>(
    initial.clusters.length > 0
      ? initial.clusters.map((c) => ({ name: c.name, targetCount: c.targets.length || 1 }))
      : [{ name: 'Phần 1', targetCount: 2 }],
  )
  const [preset, setPreset] = useState<ScorePreset>({ ...initial, name: initial.name })
  const [editingRule, setEditingRule] = useState<ClassificationRule | null>(null)
  const [editingRuleIndex, setEditingRuleIndex] = useState<number | null>(null)

  const targets = useMemo(() => flatTargets(preset), [preset])

  function applySetups() {
    const clusters = setups.map((s, i) => ({
      id: newId(),
      name: s.name.trim() || `Phần ${i + 1}`,
      targets: Array.from({ length: Math.max(1, s.targetCount) }, (_, ti) =>
        createDefaultTarget(`Bia ${ti + 1}`),
      ),
    }))
    setPreset((p) => ({ ...p, name, clusters }))
    setStep('detail')
  }

  function updateTarget(globalIndex: number, patch: Partial<TargetDefinition>) {
    setPreset((p) => {
      let remaining = globalIndex
      const clusters = p.clusters.map((c) => {
        if (remaining < 0 || remaining >= c.targets.length) {
          remaining -= c.targets.length
          return c
        }
        const targets = c.targets.map((t, i) => {
          if (i !== remaining) return t
          const next = { ...t, ...patch }
          if (next.kind === TargetKind.KnockDown) {
            next.roundCount = 1
          } else {
            next.missPenalty = 0
            next.hitBonus = 0
            if (next.roundCount < 1) next.roundCount = 5
          }
          return next
        })
        remaining = -1
        return { ...c, targets }
      })
      return { ...p, clusters }
    })
  }

  function toggleKind(globalIndex: number) {
    const t = targets[globalIndex]
    if (!t) return
    if (t.kind === TargetKind.KnockDown) {
      updateTarget(globalIndex, { kind: TargetKind.Scored, roundCount: 5, missPenalty: 0, hitBonus: 0 })
    } else {
      updateTarget(globalIndex, { kind: TargetKind.KnockDown, roundCount: 1 })
    }
  }

  return (
    <Modal title="Cấu hình nhóm / bia" onClose={onCancel} wide>
      {step === 'name' && (
        <div className="form-stack">
          <label>
            Tên nhóm
            <input value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </label>
          <div className="row-actions">
            <button type="button" className="btn" onClick={onCancel}>
              Hủy
            </button>
            <button
              type="button"
              className="btn primary"
              onClick={() => {
                setPreset((p) => ({ ...p, name }))
                setStep('clusters')
              }}
              disabled={!name.trim()}
            >
              Tiếp
            </button>
          </div>
        </div>
      )}

      {step === 'clusters' && (
        <div className="form-stack">
          <label>
            Số phần
            <input
              type="number"
              min={1}
              max={20}
              value={clusterCount}
              onChange={(e) => {
                const n = Math.max(1, Number(e.target.value) || 1)
                setClusterCount(n)
                setSetups((prev) => {
                  const next = [...prev]
                  while (next.length < n) next.push({ name: `Phần ${next.length + 1}`, targetCount: 2 })
                  return next.slice(0, n)
                })
              }}
            />
          </label>
          <div className="row-actions">
            <button type="button" className="btn" onClick={() => setStep('name')}>
              Quay lại
            </button>
            <button type="button" className="btn primary" onClick={() => setStep('targets')}>
              Tiếp
            </button>
          </div>
        </div>
      )}

      {step === 'targets' && (
        <div className="form-stack">
          {setups.map((s, i) => (
            <div key={i} className="inline-fields">
              <label>
                Tên phần {i + 1}
                <input
                  value={s.name}
                  onChange={(e) =>
                    setSetups((prev) =>
                      prev.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)),
                    )
                  }
                />
              </label>
              <label>
                Số bia
                <input
                  type="number"
                  min={1}
                  max={50}
                  value={s.targetCount}
                  onChange={(e) =>
                    setSetups((prev) =>
                      prev.map((x, j) =>
                        j === i ? { ...x, targetCount: Math.max(1, Number(e.target.value) || 1) } : x,
                      ),
                    )
                  }
                />
              </label>
            </div>
          ))}
          <div className="row-actions">
            <button type="button" className="btn" onClick={() => setStep('clusters')}>
              Quay lại
            </button>
            <button type="button" className="btn primary" onClick={applySetups}>
              Tạo bảng
            </button>
          </div>
        </div>
      )}

      {step === 'detail' && (
        <div className="form-stack">
          <label>
            Tên nhóm
            <input
              value={preset.name}
              onChange={(e) => setPreset((p) => ({ ...p, name: e.target.value }))}
            />
          </label>

          {preset.clusters.map((cluster, ci) => {
            let base = 0
            for (let k = 0; k < ci; k++) base += preset.clusters[k].targets.length
            return (
              <section key={cluster.id} className="cluster-block">
                <input
                  className="cluster-name"
                  value={cluster.name}
                  onChange={(e) =>
                    setPreset((p) => ({
                      ...p,
                      clusters: p.clusters.map((c, i) =>
                        i === ci ? { ...c, name: e.target.value } : c,
                      ),
                    }))
                  }
                />
                <div className="target-table">
                  {cluster.targets.map((t, ti) => {
                    const gi = base + ti
                    return (
                      <div key={gi} className="target-row">
                        <input
                          value={t.name}
                          onChange={(e) => updateTarget(gi, { name: e.target.value })}
                        />
                        <span className="muted">{t.kind === TargetKind.KnockDown ? 'Bia đổ' : 'Chấm điểm'}</span>
                        {t.kind === TargetKind.Scored ? (
                          <label>
                            Số đạn
                            <input
                              type="number"
                              min={1}
                              max={50}
                              value={t.roundCount}
                              onChange={(e) =>
                                updateTarget(gi, {
                                  roundCount: Math.max(1, Number(e.target.value) || 1),
                                })
                              }
                            />
                          </label>
                        ) : (
                          <>
                            <label>
                              Cộng khi Đổ
                              <input
                                type="number"
                                min={0}
                                value={t.hitBonus}
                                onChange={(e) =>
                                  updateTarget(gi, { hitBonus: Math.max(0, Number(e.target.value) || 0) })
                                }
                              />
                            </label>
                            <label>
                              Trừ khi Không
                              <input
                                type="number"
                                min={0}
                                value={t.missPenalty}
                                onChange={(e) =>
                                  updateTarget(gi, {
                                    missPenalty: Math.max(0, Number(e.target.value) || 0),
                                  })
                                }
                              />
                            </label>
                          </>
                        )}
                        <button type="button" className="btn small" onClick={() => toggleKind(gi)}>
                          {t.kind === TargetKind.KnockDown ? '→ Chấm điểm' : '→ Bia đổ'}
                        </button>
                      </div>
                    )
                  })}
                </div>
              </section>
            )
          })}

          <section className="rules-block">
            <div className="section-head">
              <h3>Xếp loại</h3>
              <button
                type="button"
                className="btn small"
                onClick={() => {
                  setEditingRuleIndex(null)
                  setEditingRule({
                    label: '',
                    minScore: 0,
                    priority: 0,
                    conditions: [
                      {
                        kind: ClassificationConditionKind.TotalScore,
                        targetIndex: -1,
                        minValue: 0,
                      },
                    ],
                  })
                }}
              >
                Thêm hạng
              </button>
            </div>
            <ul className="rule-list">
              {preset.classificationRules.map((r, i) => (
                <li key={i}>
                  <div>
                    <strong>{r.label || '(không tên)'}</strong>
                    <div className="muted">{conditionSummary(r, targets)}</div>
                  </div>
                  <div className="row-actions">
                    <button
                      type="button"
                      className="btn small"
                      onClick={() => {
                        setEditingRuleIndex(i)
                        setEditingRule({ ...r, conditions: r.conditions.map((c) => ({ ...c })) })
                      }}
                    >
                      Sửa
                    </button>
                    <button
                      type="button"
                      className="btn small danger"
                      onClick={() =>
                        setPreset((p) => ({
                          ...p,
                          classificationRules: p.classificationRules.filter((_, j) => j !== i),
                        }))
                      }
                    >
                      Xóa
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          </section>

          <div className="row-actions">
            <button
              type="button"
              className="btn"
              onClick={() => {
                if (confirm('Thiết lập lại bố cục bia? Điểm đã nhập có thể bị ảnh hưởng.')) {
                  setStep('name')
                }
              }}
            >
              Thiết lập lại
            </button>
            <button type="button" className="btn" onClick={onCancel}>
              Hủy
            </button>
            <button
              type="button"
              className="btn primary"
              onClick={() => onSave({ ...preset, name: preset.name.trim() || name })}
              disabled={targets.length === 0 || !preset.name.trim()}
            >
              Lưu
            </button>
          </div>
        </div>
      )}

      {editingRule && (
        <RuleEditor
          rule={editingRule}
          targets={targets}
          onCancel={() => setEditingRule(null)}
          onSave={(rule) => {
            setPreset((p) => {
              const rules = [...p.classificationRules]
              if (editingRuleIndex == null) rules.push(rule)
              else rules[editingRuleIndex] = rule
              return { ...p, classificationRules: rules }
            })
            setEditingRule(null)
          }}
        />
      )}
    </Modal>
  )
}

function RuleEditor({
  rule,
  targets,
  onSave,
  onCancel,
}: {
  rule: ClassificationRule
  targets: TargetDefinition[]
  onSave: (rule: ClassificationRule) => void
  onCancel: () => void
}) {
  const [draft, setDraft] = useState(rule)

  function updateCondition(index: number, patch: Partial<ClassificationCondition>) {
    setDraft((r) => ({
      ...r,
      conditions: r.conditions.map((c, i) => (i === index ? { ...c, ...patch } : c)),
    }))
  }

  return (
    <Modal title="Hạng xếp loại" onClose={onCancel}>
      <div className="form-stack">
        <label>
          Nhãn
          <input
            value={draft.label}
            onChange={(e) => setDraft((r) => ({ ...r, label: e.target.value }))}
            autoFocus
          />
        </label>
        <label>
          Ưu tiên
          <input
            type="number"
            value={draft.priority}
            onChange={(e) => setDraft((r) => ({ ...r, priority: Number(e.target.value) || 0 }))}
          />
        </label>

        {draft.conditions.map((c, i) => (
          <div key={i} className="condition-row">
            <select
              value={
                c.kind === ClassificationConditionKind.TotalScore || c.targetIndex < 0
                  ? 'total'
                  : String(c.targetIndex)
              }
              onChange={(e) => {
                if (e.target.value === 'total') {
                  updateCondition(i, {
                    kind: ClassificationConditionKind.TotalScore,
                    targetIndex: -1,
                  })
                } else {
                  const ti = Number(e.target.value)
                  const t = targets[ti]
                  updateCondition(i, {
                    targetIndex: ti,
                    kind:
                      t?.kind === TargetKind.KnockDown
                        ? ClassificationConditionKind.TargetKnockDown
                        : ClassificationConditionKind.TargetScore,
                  })
                }
              }}
            >
              <option value="total">Tổng điểm</option>
              {targets.map((t, ti) => (
                <option key={ti} value={ti}>
                  {t.name}
                </option>
              ))}
            </select>

            {c.kind === ClassificationConditionKind.TargetKnockDown ||
            (c.targetIndex >= 0 && targets[c.targetIndex]?.kind === TargetKind.KnockDown) ? (
              <select
                value={c.minValue >= 1 ? 1 : 0}
                onChange={(e) => updateCondition(i, { minValue: Number(e.target.value) })}
              >
                <option value={1}>Đổ</option>
                <option value={0}>Không đổ</option>
              </select>
            ) : (
              <label>
                ≥
                <input
                  type="number"
                  value={c.minValue}
                  onChange={(e) => updateCondition(i, { minValue: Number(e.target.value) || 0 })}
                />
              </label>
            )}

            <button
              type="button"
              className="btn small danger"
              disabled={draft.conditions.length <= 1}
              onClick={() =>
                setDraft((r) => ({
                  ...r,
                  conditions: r.conditions.filter((_, j) => j !== i),
                }))
              }
            >
              Xóa
            </button>
          </div>
        ))}

        <button
          type="button"
          className="btn small"
          onClick={() =>
            setDraft((r) => ({
              ...r,
              conditions: [
                ...r.conditions,
                {
                  kind: ClassificationConditionKind.TotalScore,
                  targetIndex: -1,
                  minValue: 0,
                },
              ],
            }))
          }
        >
          Thêm điều kiện
        </button>

        <div className="row-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Hủy
          </button>
          <button
            type="button"
            className="btn primary"
            disabled={!draft.label.trim()}
            onClick={() => onSave(draft)}
          >
            Lưu
          </button>
        </div>
      </div>
    </Modal>
  )
}
