import { useMemo, useRef, useState } from 'react'
import { classify, knockDownCount, progressLabel, totalScore } from '../domain/scoreCalculator'
import { parseRosterEntries } from '../domain/rosterParser'
import {
  TargetKind,
  createEmptyShooter,
  flatTargets,
  formatTargetScores,
  getRoundCounts,
  type ScorePreset,
  type Shooter,
} from '../domain/types'
import { exportReport } from '../export/excelExport'
import { useApp } from '../state/AppContext'
import { Modal } from './Modal'

export function ScoreEntryTab() {
  const {
    state,
    selectedSession,
    selectSession,
    createSession,
    deleteSession,
    updateSessionShooters,
    updateShooter,
    getPresetForGroup,
    runBusy,
  } = useApp()

  const [showCreate, setShowCreate] = useState(false)
  const [scoreShooterId, setScoreShooterId] = useState<string | null>(null)
  const [showReport, setShowReport] = useState(false)
  const [search, setSearch] = useState('')
  const [onlySelected, setOnlySelected] = useState(false)
  const [addCount, setAddCount] = useState(5)
  const pasteStartRef = useRef(0)

  const group = selectedSession
    ? state.groups.find((g) => g.id === selectedSession.groupId) ?? null
    : null
  const preset = selectedSession ? getPresetForGroup(selectedSession.groupId) : null
  const targets = preset ? flatTargets(preset) : []

  const filtered = useMemo(() => {
    if (!selectedSession) return []
    const q = search.trim().toLowerCase()
    return selectedSession.shooters.filter((s) => {
      if (onlySelected && !s.isSelected) return false
      if (!q) return true
      const hay = `${s.name} ${s.rank} ${s.position} ${s.unit}`.toLowerCase()
      return hay.includes(q)
    })
  }, [selectedSession, search, onlySelected])

  const scoringShooter = selectedSession?.shooters.find((s) => s.id === scoreShooterId) ?? null

  function patchShooters(mutator: (list: Shooter[]) => Shooter[]) {
    if (!selectedSession) return
    updateSessionShooters(selectedSession.id, mutator([...selectedSession.shooters]))
  }

  function handlePaste(text: string) {
    if (!selectedSession || !preset) return
    const entries = parseRosterEntries(text)
    if (entries.length === 0) return
    const rounds = getRoundCounts(preset)
    patchShooters((list) => {
      const next = [...list]
      let row = pasteStartRef.current
      for (const e of entries) {
        while (row >= next.length) {
          next.push(createEmptyShooter(next.length + 1, rounds))
        }
        next[row] = {
          ...next[row],
          name: e.name,
          rank: e.rank || next[row].rank,
          position: e.position || next[row].position,
          unit: e.unit || next[row].unit,
        }
        row++
      }
      return next.map((s, i) => ({ ...s, order: i + 1 }))
    })
  }

  async function handleExport() {
    if (!selectedSession || !preset || !group) return
    const named = selectedSession.shooters.filter((s) => s.name.trim())
    const selected = named.filter((s) => s.isSelected)
    let rowsSource = selected.length > 0 ? selected : named
    if (selected.length === 0) {
      if (!confirm('Không có người được chọn. Xuất tất cả người đã có tên?')) return
    }
    await runBusy('Đang xuất Excel...', async () => {
      const rows = rowsSource.map((s, i) => ({
        index: i + 1,
        name: s.name,
        rank: s.rank,
        position: s.position,
        unit: s.unit,
        groupName: group.name,
        targetDetails: targets.map((t, ti) => formatTargetScores(s, ti, t.kind)),
        total: totalScore(s, preset),
        knockDownCount: knockDownCount(s, preset),
        classification: classify(s, preset),
      }))
      await exportReport(selectedSession.name, group.name, rows, targets)
    })
  }

  const hasKnockDown = targets.some((t) => t.kind === TargetKind.KnockDown)

  return (
    <div className="score-tab">
      <div className="toolbar-primary">
        <label>
          Đợt
          <select
            value={selectedSession?.id ?? ''}
            onChange={(e) => selectSession(e.target.value || null)}
          >
            <option value="">— Chọn đợt —</option>
            {state.sessions.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </label>
        <div className="session-actions">
          <button type="button" className="btn primary" onClick={() => setShowCreate(true)}>
            Tạo đợt
          </button>
          <button
            type="button"
            className="btn danger"
            disabled={!selectedSession}
            onClick={() => {
              if (!selectedSession) return
              if (confirm(`Xóa đợt "${selectedSession.name}"?`)) deleteSession(selectedSession.id)
            }}
          >
            Xóa đợt
          </button>
        </div>
        <label className="grow">
          Tìm
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Họ tên, cấp bậc..."
          />
        </label>
        <label className="check">
          <input
            type="checkbox"
            checked={onlySelected}
            onChange={(e) => setOnlySelected(e.target.checked)}
          />
          Chỉ hiện đã chọn
        </label>
      </div>

      {!selectedSession || !preset ? (
        <p className="muted pad">Chọn hoặc tạo đợt bắn để nhập liệu.</p>
      ) : (
        <>
          <div className="toolbar-scroll">
            <button
              type="button"
              className="btn small"
              onClick={() =>
                patchShooters((list) =>
                  list.map((s) => ({ ...s, isSelected: !!s.name.trim() })),
                )
              }
            >
              Chọn có tên
            </button>
            <button
              type="button"
              className="btn small"
              onClick={() =>
                patchShooters((list) => list.map((s) => ({ ...s, isSelected: false })))
              }
            >
              Bỏ chọn
            </button>
            <label>
              Thêm dòng
              <input
                type="number"
                min={1}
                max={200}
                value={addCount}
                onChange={(e) => setAddCount(Math.max(1, Number(e.target.value) || 1))}
              />
            </label>
            <button
              type="button"
              className="btn small"
              onClick={() => {
                const rounds = getRoundCounts(preset)
                patchShooters((list) => [
                  ...list,
                  ...Array.from({ length: addCount }, (_, i) =>
                    createEmptyShooter(list.length + i + 1, rounds),
                  ),
                ])
              }}
            >
              Thêm
            </button>
            <button
              type="button"
              className="btn small danger"
              onClick={() =>
                patchShooters((list) => {
                  const kept = list.filter((s) => !s.isSelected)
                  return kept.length > 0
                    ? kept.map((s, i) => ({ ...s, order: i + 1 }))
                    : list
                })
              }
            >
              Xóa dòng chọn
            </button>
            <button type="button" className="btn small" onClick={() => setShowReport(true)}>
              Báo cáo
            </button>
            <button type="button" className="btn small primary" onClick={() => void handleExport()}>
              Xuất Excel
            </button>
          </div>

          <div
            className="table-wrap"
            onPaste={(e) => {
              const text = e.clipboardData.getData('text')
              if (text) {
                e.preventDefault()
                handlePaste(text)
              }
            }}
          >
            <table className="data-table">
              <thead>
                <tr>
                  <th className="sticky-col"></th>
                  <th className="sticky-col-2">STT</th>
                  <th className="sticky-col-3">Họ tên</th>
                  <th>Cấp bậc</th>
                  <th>Chức vụ</th>
                  <th>Đơn vị</th>
                  <th></th>
                  {targets.map((t, i) => (
                    <th key={i}>{t.name}</th>
                  ))}
                  <th>Tổng</th>
                  {hasKnockDown && <th>Bia đổ</th>}
                  <th>Xếp loại</th>
                  <th>Tiến độ</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((s) => {
                  const absIndex = selectedSession.shooters.findIndex((x) => x.id === s.id)
                  return (
                    <tr key={s.id}>
                      <td className="sticky-col">
                        <input
                          type="checkbox"
                          checked={s.isSelected}
                          onChange={(e) =>
                            updateShooter(selectedSession.id, {
                              ...s,
                              isSelected: e.target.checked,
                            })
                          }
                        />
                      </td>
                      <td className="sticky-col-2">{absIndex + 1}</td>
                      <td className="sticky-col-3">
                        <input
                          className="cell-input"
                          value={s.name}
                          onFocus={() => {
                            pasteStartRef.current = absIndex
                          }}
                          onChange={(e) =>
                            updateShooter(selectedSession.id, {
                              ...s,
                              name: e.target.value,
                            })
                          }
                        />
                      </td>
                      {(['rank', 'position', 'unit'] as const).map((field) => (
                        <td key={field}>
                          <input
                            className="cell-input"
                            value={s[field]}
                            onFocus={() => {
                              pasteStartRef.current = absIndex
                            }}
                            onChange={(e) =>
                              updateShooter(selectedSession.id, {
                                ...s,
                                [field]: e.target.value,
                              })
                            }
                          />
                        </td>
                      ))}
                      <td>
                        <button
                          type="button"
                          className="btn small"
                          onClick={() => setScoreShooterId(s.id)}
                        >
                          Nhập
                        </button>
                      </td>
                      {targets.map((t, ti) => (
                        <td key={ti} className="center">
                          {formatTargetScores(s, ti, t.kind)}
                        </td>
                      ))}
                      <td className="center">{totalScore(s, preset)}</td>
                      {hasKnockDown && (
                        <td className="center">{knockDownCount(s, preset)}</td>
                      )}
                      <td className="center">{classify(s, preset)}</td>
                      <td className="center">{progressLabel(s, preset)}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <div
            className="card-list"
            onPaste={(e) => {
              const text = e.clipboardData.getData('text')
              if (text) {
                e.preventDefault()
                handlePaste(text)
              }
            }}
          >
            {filtered.map((s) => {
              const absIndex = selectedSession.shooters.findIndex((x) => x.id === s.id)
              return (
                <article key={s.id} className="shooter-card">
                  <div className="shooter-card-top">
                    <input
                      type="checkbox"
                      checked={s.isSelected}
                      onChange={(e) =>
                        updateShooter(selectedSession.id, {
                          ...s,
                          isSelected: e.target.checked,
                        })
                      }
                      aria-label="Chọn"
                    />
                    <span className="shooter-card-stt">#{absIndex + 1}</span>
                  </div>
                  <div className="shooter-card-fields">
                    <input
                      value={s.name}
                      placeholder="Họ tên"
                      onFocus={() => {
                        pasteStartRef.current = absIndex
                      }}
                      onChange={(e) =>
                        updateShooter(selectedSession.id, { ...s, name: e.target.value })
                      }
                    />
                    <input
                      value={s.rank}
                      placeholder="Cấp bậc"
                      onChange={(e) =>
                        updateShooter(selectedSession.id, { ...s, rank: e.target.value })
                      }
                    />
                    <input
                      value={s.position}
                      placeholder="Chức vụ"
                      onChange={(e) =>
                        updateShooter(selectedSession.id, { ...s, position: e.target.value })
                      }
                    />
                    <input
                      value={s.unit}
                      placeholder="Đơn vị"
                      onChange={(e) =>
                        updateShooter(selectedSession.id, { ...s, unit: e.target.value })
                      }
                    />
                  </div>
                  <div className="shooter-card-meta">
                    <span>
                      Tổng: <strong>{totalScore(s, preset)}</strong>
                    </span>
                    {hasKnockDown && (
                      <span>
                        Bia đổ: <strong>{knockDownCount(s, preset)}</strong>
                      </span>
                    )}
                    <span>
                      XL: <strong>{classify(s, preset) || '—'}</strong>
                    </span>
                    <span>
                      Tiến độ: <strong>{progressLabel(s, preset)}</strong>
                    </span>
                  </div>
                  <div className="shooter-card-actions">
                    <button
                      type="button"
                      className="btn primary block"
                      onClick={() => setScoreShooterId(s.id)}
                    >
                      Nhập điểm
                    </button>
                  </div>
                </article>
              )
            })}
          </div>

          <p className="hint hint-desktop">
            Gợi ý: dán (Ctrl+V) danh sách từ Excel vào ô Họ tên — cột: Họ tên, Cấp bậc, Chức vụ, Đơn
            vị.
          </p>
          <p className="hint hint-mobile">
            Gợi ý: chạm vào ô Họ tên rồi dán danh sách — cột: Họ tên, Cấp bậc, Chức vụ, Đơn vị.
          </p>
        </>
      )}

      {showCreate && (
        <CreateSessionDialog
          groups={state.groups}
          getPreset={getPresetForGroup}
          onCancel={() => setShowCreate(false)}
          onCreate={(name, groupId, count) => {
            createSession(name, groupId, count)
            setShowCreate(false)
          }}
        />
      )}

      {scoringShooter && selectedSession && preset && (
        <ScorePadDialog
          shooter={scoringShooter}
          preset={preset}
          onCancel={() => setScoreShooterId(null)}
          onSave={(next) => {
            updateShooter(selectedSession.id, next)
            setScoreShooterId(null)
          }}
        />
      )}

      {showReport && selectedSession && preset && (
        <ReportDialog
          shooters={selectedSession.shooters}
          preset={preset}
          onClose={() => setShowReport(false)}
        />
      )}
    </div>
  )
}

function CreateSessionDialog({
  groups,
  getPreset,
  onCreate,
  onCancel,
}: {
  groups: { id: string; name: string }[]
  getPreset: (id: string) => ScorePreset | null
  onCreate: (name: string, groupId: string, count: number) => void
  onCancel: () => void
}) {
  const [name, setName] = useState(`Đợt ${new Date().toLocaleDateString('vi-VN')}`)
  const [groupId, setGroupId] = useState(groups[0]?.id ?? '')
  const [count, setCount] = useState(30)
  const preset = getPreset(groupId)
  const ok = name.trim() && groupId && preset && flatTargets(preset).length > 0

  return (
    <Modal title="Tạo đợt bắn" onClose={onCancel}>
      <div className="form-stack">
        <label>
          Tên đợt
          <input value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </label>
        <label>
          Nhóm
          <select value={groupId} onChange={(e) => setGroupId(e.target.value)}>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Số người
          <input
            type="number"
            min={1}
            max={500}
            value={count}
            onChange={(e) => setCount(Math.max(1, Number(e.target.value) || 1))}
          />
        </label>
        {!ok && groupId && (
          <p className="error">Nhóm chưa cấu hình bia. Hãy sửa nhóm trước.</p>
        )}
        <div className="row-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Hủy
          </button>
          <button
            type="button"
            className="btn primary"
            disabled={!ok}
            onClick={() => onCreate(name.trim(), groupId, count)}
          >
            Tạo
          </button>
        </div>
      </div>
    </Modal>
  )
}

const CLUSTER_COLORS = ['#3e6b4f', '#2f5f7a', '#6b5a3e', '#5a4a6b', '#3e6b5f', '#6b4a3e']

function ScorePadDialog({
  shooter,
  preset,
  onSave,
  onCancel,
}: {
  shooter: Shooter
  preset: ScorePreset
  onSave: (s: Shooter) => void
  onCancel: () => void
}) {
  const [draft, setDraft] = useState(() => structuredClone(shooter))

  function setShot(ti: number, ri: number, value: number | null) {
    setDraft((s) => {
      const shots = s.shots.map((row, i) =>
        i === ti ? row.map((v, j) => (j === ri ? value : v)) : [...row],
      )
      return { ...s, shots }
    })
  }

  const liveTotal = totalScore(draft, preset)
  const liveClass = classify(draft, preset)

  let offset = 0
  return (
    <Modal
      title={`Nhập điểm — ${shooter.name || 'Chưa có tên'}`}
      onClose={onCancel}
      wide
      footer={
        <div className="pad-footer">
          <div className="pad-summary">
            Tổng: <strong>{liveTotal}</strong>
            {liveClass ? (
              <>
                {' '}
                · Xếp loại: <strong>{liveClass}</strong>
              </>
            ) : null}
          </div>
          <div className="row-actions">
            <button
              type="button"
              className="btn"
              onClick={() =>
                setDraft((s) => ({
                  ...s,
                  shots: s.shots.map((row) => row.map(() => null)),
                }))
              }
            >
              Xóa hết
            </button>
            <button type="button" className="btn" onClick={onCancel}>
              Hủy
            </button>
            <button type="button" className="btn primary" onClick={() => onSave(draft)}>
              Lưu
            </button>
          </div>
        </div>
      }
    >
      <div className="score-pad">
        {preset.clusters.map((cluster, ci) => {
          const start = offset
          offset += cluster.targets.length
          return (
            <section
              key={cluster.id}
              className="pad-cluster"
              style={{ ['--pad-accent' as string]: CLUSTER_COLORS[ci % CLUSTER_COLORS.length] }}
            >
              <h3 className="pad-cluster-title">{cluster.name}</h3>
              {cluster.targets.map((t, local) => {
                const ti = start + local
                const row = draft.shots[ti] ?? []
                const isKd = t.kind === TargetKind.KnockDown
                return (
                  <div
                    key={ti}
                    className={`pad-target ${isKd ? 'pad-target-kd' : 'pad-target-scored'}`}
                  >
                    <div className="pad-title">
                      <span>{t.name}</span>
                      <span className={`pad-kind-badge ${isKd ? 'kd' : 'scored'}`}>
                        {isKd ? 'Bia đổ' : 'Chấm điểm'}
                      </span>
                    </div>
                    {isKd ? (
                      <div className="pad-keys pad-keys-kd">
                        <button
                          type="button"
                          className={row[0] === 1 ? 'btn pad-btn-hit selected' : 'btn pad-btn-hit'}
                          onClick={() => setShot(ti, 0, 1)}
                        >
                          Đổ
                        </button>
                        <button
                          type="button"
                          className={
                            row[0] === 0 ? 'btn pad-btn-miss selected' : 'btn pad-btn-miss'
                          }
                          onClick={() => setShot(ti, 0, 0)}
                        >
                          Không
                        </button>
                        <button
                          type="button"
                          className="btn pad-btn-clear"
                          onClick={() => setShot(ti, 0, null)}
                        >
                          Xóa
                        </button>
                      </div>
                    ) : (
                      row.map((v, ri) => (
                        <div key={ri} className="pad-round">
                          <div className="pad-keys">
                            {Array.from({ length: 11 }, (_, score) => 10 - score).map((score) => (
                              <button
                                key={score}
                                type="button"
                                className={
                                  v === score
                                    ? 'btn small pad-score selected'
                                    : 'btn small pad-score'
                                }
                                onClick={() => setShot(ti, ri, score)}
                                aria-label={`Phát ${ri + 1}: ${score}`}
                              >
                                {score}
                              </button>
                            ))}
                            <button
                              type="button"
                              className="btn small pad-btn-clear"
                              onClick={() => setShot(ti, ri, null)}
                              aria-label={`Xóa phát ${ri + 1}`}
                            >
                              ×
                            </button>
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                )
              })}
            </section>
          )
        })}
      </div>
    </Modal>
  )
}

function ReportDialog({
  shooters,
  preset,
  onClose,
}: {
  shooters: Shooter[]
  preset: ScorePreset
  onClose: () => void
}) {
  const selected = shooters.filter((s) => s.isSelected && s.name.trim())
  const source = selected.length > 0 ? selected : shooters.filter((s) => s.name.trim())
  const counts = new Map<string, number>()
  for (const s of source) {
    const label = classify(s, preset) || '(chưa xếp)'
    counts.set(label, (counts.get(label) ?? 0) + 1)
  }
  const total = source.length || 1

  return (
    <Modal title="Báo cáo xếp loại" onClose={onClose}>
      <table className="data-table">
        <thead>
          <tr>
            <th>Xếp loại</th>
            <th>Số lượng</th>
            <th>%</th>
          </tr>
        </thead>
        <tbody>
          {[...counts.entries()]
            .sort((a, b) => b[1] - a[1])
            .map(([label, n]) => (
              <tr key={label}>
                <td>{label}</td>
                <td className="center">{n}</td>
                <td className="center">{((n / total) * 100).toFixed(1)}%</td>
              </tr>
            ))}
          <tr>
            <td>
              <strong>Tổng</strong>
            </td>
            <td className="center">
              <strong>{source.length}</strong>
            </td>
            <td className="center">100%</td>
          </tr>
        </tbody>
      </table>
      <p className="muted">
        {selected.length > 0 ? 'Theo người đã chọn.' : 'Theo tất cả người có tên.'}
      </p>
      <div className="row-actions">
        <button type="button" className="btn primary" onClick={onClose}>
          Đóng
        </button>
      </div>
    </Modal>
  )
}
