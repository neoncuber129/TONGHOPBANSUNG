import { useMemo, useState } from 'react'
import {
  conditionSummary,
  createDefaultPreset,
  flatTargets,
  type ScorePreset,
} from '../domain/types'
import { useApp } from '../state/AppContext'
import { PresetEditorDialog } from './PresetEditorDialog'

export function GroupsTab() {
  const { state, selectedGroup, selectedPreset, selectGroup, addGroup, updatePreset, deleteGroup } =
    useApp()
  const [draft, setDraft] = useState<ScorePreset | null>(null)

  const summary = useMemo(() => {
    if (!selectedPreset) return ''
    const targets = flatTargets(selectedPreset)
    const parts = selectedPreset.clusters.length
    const rounds = targets.reduce(
      (n, t) => n + (t.kind === 1 ? 1 : Math.max(1, t.roundCount)),
      0,
    )
    return `${parts} phần · ${targets.length} bia · ${rounds} phát`
  }, [selectedPreset])

  return (
    <div className="split-pane">
      <aside className="side-list">
        <div className="section-head">
          <h3>Nhóm</h3>
          <button
            type="button"
            className="btn small primary"
            onClick={() => {
              const g = addGroup()
              const empty = createDefaultPreset(g.name)
              empty.id = g.presetId
              empty.clusters = []
              setDraft(empty)
            }}
          >
            Thêm
          </button>
        </div>
        <ul>
          {state.groups.map((g) => (
            <li key={g.id}>
              <button
                type="button"
                className={selectedGroup?.id === g.id ? 'list-item active' : 'list-item'}
                onClick={() => selectGroup(g.id)}
                onDoubleClick={() => {
                  selectGroup(g.id)
                  const p = state.presets.find((x) => x.id === g.presetId)
                  if (p) setDraft({ ...p })
                }}
              >
                {g.name}
              </button>
            </li>
          ))}
        </ul>
        <div className="row-actions wrap">
          <button
            type="button"
            className="btn"
            disabled={!selectedPreset}
            onClick={() => selectedPreset && setDraft({ ...selectedPreset })}
          >
            Sửa
          </button>
          <button
            type="button"
            className="btn danger"
            disabled={!selectedGroup || state.groups.length <= 1}
            onClick={() => {
              if (!selectedGroup) return
              if (confirm(`Xóa nhóm "${selectedGroup.name}"?`)) deleteGroup(selectedGroup.id)
            }}
          >
            Xóa
          </button>
        </div>
      </aside>

      <section className="detail-pane">
        {!selectedPreset || !selectedGroup ? (
          <p className="muted">Chọn một nhóm để xem cấu hình.</p>
        ) : (
          <>
            <div className="detail-pane-head">
              <h2>{selectedGroup.name}</h2>
              <button
                type="button"
                className="btn primary"
                onClick={() => setDraft({ ...selectedPreset })}
              >
                Sửa
              </button>
            </div>
            <p className="muted">{summary || 'Chưa cấu hình bia — hãy Sửa nhóm.'}</p>

            {selectedPreset.clusters.map((c) => (
              <div key={c.id} className="cluster-block">
                <h4>{c.name}</h4>
                <ul>
                  {c.targets.map((t, i) => (
                    <li key={i}>
                      {t.name} —{' '}
                      {t.kind === 1
                        ? `Đổ (cộng ${t.hitBonus}, trừ ${t.missPenalty})`
                        : `${t.roundCount} đạn`}
                    </li>
                  ))}
                </ul>
              </div>
            ))}

            <h3>Xếp loại</h3>
            {selectedPreset.classificationRules.length === 0 ? (
              <p className="muted">Chưa có hạng xếp loại.</p>
            ) : (
              <ul className="rule-list">
                {selectedPreset.classificationRules.map((r, i) => (
                  <li key={i}>
                    <strong>{r.label}</strong>
                    <div className="muted">
                      {conditionSummary(r, flatTargets(selectedPreset))}
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </section>

      {draft && (
        <PresetEditorDialog
          initial={draft}
          onCancel={() => setDraft(null)}
          onSave={(preset) => {
            updatePreset(preset)
            setDraft(null)
          }}
        />
      )}
    </div>
  )
}
