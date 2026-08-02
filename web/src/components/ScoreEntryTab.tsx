import { useEffect, useMemo, useRef, useState } from 'react'
import { classify, knockDownCount, progressLabel, scoreValueCounts, totalScore } from '../domain/scoreCalculator'
import { parseRosterEntries, parseRosterGrid } from '../domain/rosterParser'
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

const ROSTER_FIELDS = ['name', 'rank', 'position', 'unit'] as const

interface PasteCell {
  row: number
  col: number
}

/** Ô trong bảng đã lọc: row = index trong filtered, col = 0..3 thông tin. */
interface ViewCell {
  row: number
  col: number
}

interface CellRange {
  anchor: ViewCell
  end: ViewCell
}

function normalizeRange(range: CellRange): { r0: number; r1: number; c0: number; c1: number } {
  return {
    r0: Math.min(range.anchor.row, range.end.row),
    r1: Math.max(range.anchor.row, range.end.row),
    c0: Math.min(range.anchor.col, range.end.col),
    c1: Math.max(range.anchor.col, range.end.col),
  }
}

function cellInRange(range: CellRange | null, row: number, col: number): boolean {
  if (!range) return false
  const { r0, r1, c0, c1 } = normalizeRange(range)
  return row >= r0 && row <= r1 && col >= c0 && col <= c1
}

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
    exportSession,
    previewSessionTransfer,
    importSessionCreate,
    importSessionAppend,
    setStatus,
  } = useApp()

  const [showCreate, setShowCreate] = useState(false)
  const [scoreShooterId, setScoreShooterId] = useState<string | null>(null)
  const [showReport, setShowReport] = useState(false)
  const [importPreview, setImportPreview] = useState<{
    file: File
    pack: Awaited<ReturnType<typeof previewSessionTransfer>>['pack']
    matchingGroups: { id: string; name: string }[]
    canAppendToActive: boolean
  } | null>(null)
  const [search, setSearch] = useState('')
  const [onlySelected, setOnlySelected] = useState(false)
  const [addCount, setAddCount] = useState<number | ''>(5)
  const pasteStartRef = useRef<PasteCell>({ row: 0, col: 0 })
  const importFileRef = useRef<HTMLInputElement>(null)
  const undoStackRef = useRef<Shooter[][]>([])
  const editBaselineRef = useRef<Shooter[] | null>(null)
  const editPushedRef = useRef(false)
  const [cellRange, setCellRange] = useState<CellRange | null>(null)
  const cellRangeRef = useRef<CellRange | null>(null)
  const draggingSelectRef = useRef(false)
  const dragMovedRef = useRef(false)
  const suppressFocusRef = useRef(false)
  const tableWrapRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    cellRangeRef.current = cellRange
  }, [cellRange])

  useEffect(() => {
    undoStackRef.current = []
    editBaselineRef.current = null
    editPushedRef.current = false
    setCellRange(null)
  }, [selectedSession?.id])

  useEffect(() => {
    const endDrag = () => {
      if (!draggingSelectRef.current) return
      draggingSelectRef.current = false
      const range = cellRangeRef.current
      suppressFocusRef.current = false
      if (!dragMovedRef.current && range) {
        const { r0, c0 } = normalizeRange(range)
        const el = document.querySelector<HTMLInputElement>(
          `[data-view-row="${r0}"][data-view-col="${c0}"]`,
        )
        el?.focus()
      } else {
        tableWrapRef.current?.focus()
      }
    }
    window.addEventListener('mouseup', endDrag)
    return () => window.removeEventListener('mouseup', endDrag)
  }, [])

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

  function pushUndo(snapshot?: Shooter[]) {
    if (!selectedSession) return
    const snap = structuredClone(snapshot ?? selectedSession.shooters)
    undoStackRef.current.push(snap)
    if (undoStackRef.current.length > 40) undoStackRef.current.shift()
  }

  function undoRoster() {
    if (!selectedSession) return
    const prev = undoStackRef.current.pop()
    if (!prev) return
    // Chỉ hoàn tác thông tin/danh sách — giữ điểm hiện tại theo id.
    const currentShots = new Map(selectedSession.shooters.map((s) => [s.id, s.shots]))
    const restored = prev.map((s) => ({
      ...s,
      shots: currentShots.get(s.id) ?? s.shots,
    }))
    updateSessionShooters(selectedSession.id, restored)
  }

  function beginFieldEdit() {
    if (!selectedSession) return
    editBaselineRef.current = structuredClone(selectedSession.shooters)
    editPushedRef.current = false
  }

  function ensureEditUndo() {
    if (editPushedRef.current || !editBaselineRef.current) return
    pushUndo(editBaselineRef.current)
    editPushedRef.current = true
  }

  function patchShooters(mutator: (list: Shooter[]) => Shooter[]) {
    if (!selectedSession) return
    pushUndo()
    updateSessionShooters(selectedSession.id, mutator([...selectedSession.shooters]))
  }

  function handleRosterKeyDown(e: React.KeyboardEvent<HTMLElement>) {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z' && !e.altKey && !e.shiftKey) {
      e.preventDefault()
      undoRoster()
      return
    }

    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'c' && !e.altKey) {
      if (copySelectedCells()) {
        e.preventDefault()
      }
      return
    }

    if (e.key === 'Delete' || e.key === 'Backspace') {
      // Trong lúc gõ ô đơn: để xóa ký tự bình thường
      const target = e.target as HTMLElement | null
      const editing =
        target instanceof HTMLInputElement &&
        cellRange &&
        cellRange.anchor.row === cellRange.end.row &&
        cellRange.anchor.col === cellRange.end.col &&
        document.activeElement === target
      if (editing) return
      if (clearSelectedCells()) {
        e.preventDefault()
      }
    }
  }

  function absRowFromView(viewRow: number): number {
    const shooter = filtered[viewRow]
    if (!shooter || !selectedSession) return viewRow
    return selectedSession.shooters.findIndex((x) => x.id === shooter.id)
  }

  function selectCell(view: ViewCell, extend: boolean) {
    setCellRange((prev) => {
      if (extend && prev) return { anchor: prev.anchor, end: view }
      return { anchor: view, end: view }
    })
    pasteStartRef.current = {
      row: absRowFromView(view.row),
      col: view.col,
    }
  }

  function beginCellSelect(e: React.MouseEvent, view: ViewCell) {
    if (e.button !== 0) return
    // Cho phép chọn vùng bằng kéo; tránh highlight text trình duyệt
    e.preventDefault()
    draggingSelectRef.current = true
    dragMovedRef.current = false
    suppressFocusRef.current = true
    selectCell(view, e.shiftKey)
  }

  function extendCellSelect(view: ViewCell) {
    if (!draggingSelectRef.current) return
    dragMovedRef.current = true
    setCellRange((prev) => (prev ? { anchor: prev.anchor, end: view } : { anchor: view, end: view }))
  }

  function clearSelectedCells(): boolean {
    if (!selectedSession || !cellRange) return false
    const { r0, r1, c0, c1 } = normalizeRange(cellRange)
    pushUndo()
    const next = selectedSession.shooters.map((s) => ({ ...s }))
    for (let vr = r0; vr <= r1; vr++) {
      const shooter = filtered[vr]
      if (!shooter) continue
      const idx = next.findIndex((x) => x.id === shooter.id)
      if (idx < 0) continue
      const updated = { ...next[idx] }
      for (let c = c0; c <= c1; c++) {
        const field = ROSTER_FIELDS[c]
        if (field) updated[field] = ''
      }
      next[idx] = updated
    }
    updateSessionShooters(
      selectedSession.id,
      next.map((s, i) => ({ ...s, order: i + 1 })),
    )
    return true
  }

  function copySelectedCells(): boolean {
    if (!selectedSession || !cellRange) return false
    const { r0, r1, c0, c1 } = normalizeRange(cellRange)
    const lines: string[] = []
    for (let vr = r0; vr <= r1; vr++) {
      const shooter = filtered[vr]
      if (!shooter) continue
      const cells: string[] = []
      for (let c = c0; c <= c1; c++) {
        const field = ROSTER_FIELDS[c]
        cells.push(field ? shooter[field] : '')
      }
      lines.push(cells.join('\t'))
    }
    if (lines.length === 0) return false
    void navigator.clipboard.writeText(lines.join('\n'))
    return true
  }

  function handlePaste(text: string, start: PasteCell) {
    if (!selectedSession || !preset) return
    const startCol = Math.min(Math.max(start.col, 0), ROSTER_FIELDS.length - 1)
    const startRow = Math.max(0, start.row)
    const grid =
      startCol === 0
        ? parseRosterEntries(text).map((e) => [e.name, e.rank, e.position, e.unit])
        : parseRosterGrid(text)
    if (grid.length === 0) return
    const rounds = getRoundCounts(preset)
    pasteStartRef.current = { row: startRow, col: startCol }
    patchShooters((list) => {
      const next = [...list]
      let row = startRow
      for (const cells of grid) {
        while (row >= next.length) {
          next.push(createEmptyShooter(next.length + 1, rounds))
        }
        const shooter = { ...next[row] }
        cells.forEach((value, i) => {
          const field = ROSTER_FIELDS[startCol + i]
          if (!field) return
          if (field === 'name' || value) shooter[field] = value
        })
        next[row] = shooter
        row++
      }
      return next.map((s, i) => ({ ...s, order: i + 1 }))
    })
    // Giữ focus đúng ô vừa dán — tránh nhảy về cột Họ tên sau re-render.
    requestAnimationFrame(() => {
      const el = document.querySelector<HTMLElement>(
        `[data-paste-row="${startRow}"][data-paste-col="${startCol}"]`,
      )
      el?.focus()
    })
  }

  function pasteCellFromEvent(e: React.ClipboardEvent<HTMLElement>): PasteCell {
    const cell = (e.target as HTMLElement | null)?.closest<HTMLElement>('[data-paste-row]')
    if (!cell) return pasteStartRef.current
    const row = Number(cell.dataset.pasteRow)
    const col = Number(cell.dataset.pasteCol)
    if (!Number.isFinite(row) || !Number.isFinite(col)) return pasteStartRef.current
    return { row, col }
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

  function handleExportSession() {
    if (!selectedSession) return
    exportSession(selectedSession.id)
  }

  function handleImportFile(file: File) {
    void runBusy('Đang đọc file đợt...', async () => {
      try {
        const preview = await previewSessionTransfer(file)
        if (preview.matchingGroups.length === 0 && !preview.canAppendToActive) {
          alert(
            'Không có nhóm/đợt nào có cùng cấu hình bia với file này. Chỉ nhập khi preset cùng cấu hình.',
          )
          return
        }
        setImportPreview({
          file,
          pack: preview.pack,
          matchingGroups: preview.matchingGroups.map((g) => ({ id: g.id, name: g.name })),
          canAppendToActive: preview.canAppendToActive,
        })
      } catch (e) {
        const msg = e instanceof Error ? e.message : String(e)
        setStatus(`Lỗi đọc file đợt: ${msg}`)
        alert(msg)
      }
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
          <button
            type="button"
            className="btn"
            disabled={!selectedSession}
            onClick={handleExportSession}
            title="Xuất riêng đợt đang chọn (.thbss) để máy khác nhập"
          >
            Xuất đợt
          </button>
          <button
            type="button"
            className="btn"
            onClick={() => importFileRef.current?.click()}
            title="Nhập file đợt (.thbss) — tạo đợt mới hoặc nối vào đợt hiện tại"
          >
            Nhập đợt
          </button>
          <input
            ref={importFileRef}
            type="file"
            accept=".thbss,application/json"
            hidden
            onChange={(e) => {
              const file = e.target.files?.[0]
              e.target.value = ''
              if (file) handleImportFile(file)
            }}
          />
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
                onChange={(e) => {
                  const v = e.target.value
                  if (v === '') {
                    setAddCount('')
                    return
                  }
                  const n = Number(v)
                  if (Number.isFinite(n)) setAddCount(n)
                }}
              />
            </label>
            <button
              type="button"
              className="btn small"
              disabled={!(typeof addCount === 'number' && addCount >= 1 && addCount <= 200)}
              onClick={() => {
                if (typeof addCount !== 'number' || addCount < 1) return
                const n = Math.min(200, addCount)
                const rounds = getRoundCounts(preset)
                patchShooters((list) => [
                  ...list,
                  ...Array.from({ length: n }, (_, i) =>
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
            ref={tableWrapRef}
            className="table-wrap"
            tabIndex={-1}
            onKeyDown={handleRosterKeyDown}
            onPaste={(e) => {
              const text = e.clipboardData.getData('text')
              if (text) {
                let cell = pasteCellFromEvent(e)
                if (cellRange) {
                  const { r0, c0 } = normalizeRange(cellRange)
                  cell = { row: absRowFromView(r0), col: c0 }
                }
                e.preventDefault()
                handlePaste(text, cell)
              }
            }}
          >
            <table className="data-table roster-select-table">
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
                {filtered.map((s, viewRow) => {
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
                      <td
                        className={
                          cellInRange(cellRange, viewRow, 0)
                            ? 'sticky-col-3 cell-selected'
                            : 'sticky-col-3'
                        }
                        onMouseDown={(e) => beginCellSelect(e, { row: viewRow, col: 0 })}
                        onMouseEnter={() => extendCellSelect({ row: viewRow, col: 0 })}
                      >
                        <input
                          className="cell-input"
                          value={s.name}
                          data-paste-row={absIndex}
                          data-paste-col={0}
                          data-view-row={viewRow}
                          data-view-col={0}
                          onFocus={() => {
                            if (suppressFocusRef.current) return
                            pasteStartRef.current = { row: absIndex, col: 0 }
                            if (!draggingSelectRef.current) {
                              setCellRange({
                                anchor: { row: viewRow, col: 0 },
                                end: { row: viewRow, col: 0 },
                              })
                            }
                            beginFieldEdit()
                          }}
                          onChange={(e) => {
                            ensureEditUndo()
                            updateShooter(selectedSession.id, {
                              ...s,
                              name: e.target.value,
                            })
                          }}
                        />
                      </td>
                      {(['rank', 'position', 'unit'] as const).map((field, fi) => {
                        const col = fi + 1
                        return (
                          <td
                            key={field}
                            className={cellInRange(cellRange, viewRow, col) ? 'cell-selected' : undefined}
                            onMouseDown={(e) => beginCellSelect(e, { row: viewRow, col })}
                            onMouseEnter={() => extendCellSelect({ row: viewRow, col })}
                          >
                            <input
                              className="cell-input"
                              value={s[field]}
                              data-paste-row={absIndex}
                              data-paste-col={col}
                              data-view-row={viewRow}
                              data-view-col={col}
                              onFocus={() => {
                                if (suppressFocusRef.current) return
                                pasteStartRef.current = { row: absIndex, col }
                                if (!draggingSelectRef.current) {
                                  setCellRange({
                                    anchor: { row: viewRow, col },
                                    end: { row: viewRow, col },
                                  })
                                }
                                beginFieldEdit()
                              }}
                              onChange={(e) => {
                                ensureEditUndo()
                                updateShooter(selectedSession.id, {
                                  ...s,
                                  [field]: e.target.value,
                                })
                              }}
                            />
                          </td>
                        )
                      })}
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
            tabIndex={-1}
            onKeyDown={handleRosterKeyDown}
            onPaste={(e) => {
              const text = e.clipboardData.getData('text')
              if (text) {
                const cell = pasteCellFromEvent(e)
                e.preventDefault()
                handlePaste(text, cell)
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
                    {(
                      [
                        ['name', 'Họ tên'],
                        ['rank', 'Cấp bậc'],
                        ['position', 'Chức vụ'],
                        ['unit', 'Đơn vị'],
                      ] as const
                    ).map(([field, placeholder], fi) => (
                      <input
                        key={field}
                        value={s[field]}
                        placeholder={placeholder}
                        data-paste-row={absIndex}
                        data-paste-col={fi}
                        onFocus={() => {
                          pasteStartRef.current = { row: absIndex, col: fi }
                          beginFieldEdit()
                        }}
                        onChange={(e) => {
                          ensureEditUndo()
                          updateShooter(selectedSession.id, { ...s, [field]: e.target.value })
                        }}
                      />
                    ))}
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

      {importPreview && (
        <ImportSessionDialog
          packName={importPreview.pack.session.name}
          shooterCount={importPreview.pack.session.shooters.length}
          matchingGroups={importPreview.matchingGroups}
          canAppend={importPreview.canAppendToActive}
          activeSessionName={selectedSession?.name ?? null}
          onCancel={() => setImportPreview(null)}
          onCreate={(groupId) => {
            const file = importPreview.file
            setImportPreview(null)
            void runBusy('Đang nhập đợt...', async () => {
              try {
                await importSessionCreate(file, groupId)
              } catch (e) {
                const msg = e instanceof Error ? e.message : String(e)
                setStatus(`Lỗi nhập đợt: ${msg}`)
                alert(msg)
              }
            })
          }}
          onAppend={() => {
            if (!selectedSession) return
            const file = importPreview.file
            const sessionId = selectedSession.id
            setImportPreview(null)
            void runBusy('Đang nối đợt...', async () => {
              try {
                await importSessionAppend(file, sessionId)
              } catch (e) {
                const msg = e instanceof Error ? e.message : String(e)
                setStatus(`Lỗi nối đợt: ${msg}`)
                alert(msg)
              }
            })
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

function suggestSessionName(now = new Date()) {
  const pad = (n: number) => String(n).padStart(2, '0')
  const d = `${pad(now.getDate())}/${pad(now.getMonth() + 1)}/${now.getFullYear()}`
  const t = `${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`
  return `Đợt ${d} ${t}`
}

function ImportSessionDialog({
  packName,
  shooterCount,
  matchingGroups,
  canAppend,
  activeSessionName,
  onCreate,
  onAppend,
  onCancel,
}: {
  packName: string
  shooterCount: number
  matchingGroups: { id: string; name: string }[]
  canAppend: boolean
  activeSessionName: string | null
  onCreate: (groupId: string) => void
  onAppend: () => void
  onCancel: () => void
}) {
  const [mode, setMode] = useState<'create' | 'append'>(
    canAppend && matchingGroups.length === 0 ? 'append' : 'create',
  )
  const [groupId, setGroupId] = useState(matchingGroups[0]?.id ?? '')
  const createOk = mode === 'create' && !!groupId && matchingGroups.length > 0
  const appendOk = mode === 'append' && canAppend

  return (
    <Modal title="Nhập đợt bắn" onClose={onCancel}>
      <div className="form-stack">
        <p>
          File: <strong>{packName}</strong> · {shooterCount} người
        </p>
        <fieldset className="form-stack" style={{ border: 'none', padding: 0, margin: 0 }}>
          <legend className="muted">Cách nhập</legend>
          <label className="check">
            <input
              type="radio"
              name="import-mode"
              checked={mode === 'create'}
              disabled={matchingGroups.length === 0}
              onChange={() => setMode('create')}
            />
            Tạo đợt mới
          </label>
          <label className="check">
            <input
              type="radio"
              name="import-mode"
              checked={mode === 'append'}
              disabled={!canAppend}
              onChange={() => setMode('append')}
            />
            Nối vào đợt hiện tại
            {activeSessionName ? ` («${activeSessionName}»)` : ''}
          </label>
        </fieldset>
        {mode === 'create' && (
          <label>
            Nhóm đích (preset cùng cấu hình)
            <select
              value={groupId}
              onChange={(e) => setGroupId(e.target.value)}
              disabled={matchingGroups.length === 0}
            >
              {matchingGroups.length === 0 ? (
                <option value="">— Không có nhóm khớp —</option>
              ) : (
                matchingGroups.map((g) => (
                  <option key={g.id} value={g.id}>
                    {g.name}
                  </option>
                ))
              )}
            </select>
          </label>
        )}
        {mode === 'append' && !canAppend && (
          <p className="error">
            Đợt đang mở không có cùng cấu hình bia với file. Hãy chọn «Tạo đợt mới» hoặc mở đúng
            đợt.
          </p>
        )}
        {matchingGroups.length === 0 && !canAppend && (
          <p className="error">Không có nhóm/đợt nào khớp preset trong file.</p>
        )}
        <div className="row-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Hủy
          </button>
          <button
            type="button"
            className="btn primary"
            disabled={!(createOk || appendOk)}
            onClick={() => {
              if (mode === 'append') onAppend()
              else onCreate(groupId)
            }}
          >
            {mode === 'append' ? 'Nối đợt' : 'Tạo đợt'}
          </button>
        </div>
      </div>
    </Modal>
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
  const [name, setName] = useState(() => suggestSessionName())
  const [groupId, setGroupId] = useState(groups[0]?.id ?? '')
  const [count, setCount] = useState<number | ''>(30)
  const preset = getPreset(groupId)
  const personCount = typeof count === 'number' ? count : 0
  const hasTargets = !!(preset && flatTargets(preset).length > 0)
  const ok =
    !!name.trim() &&
    !!groupId &&
    hasTargets &&
    personCount >= 1 &&
    personCount <= 500

  return (
    <Modal title="Tạo đợt bắn" onClose={onCancel}>
      <div className="form-stack">
        <label>
          Tên đợt
          <input value={name} onChange={(e) => setName(e.target.value)} />
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
            onChange={(e) => {
              const v = e.target.value
              if (v === '') {
                setCount('')
                return
              }
              const n = Number(v)
              if (Number.isFinite(n)) setCount(n)
            }}
          />
        </label>
        {groupId && !hasTargets && (
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
            onClick={() => onCreate(name.trim(), groupId, personCount)}
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
  const valueCounts = scoreValueCounts(draft, preset)

  let offset = 0
  return (
    <Modal
      title="Nhập điểm"
      onClose={onCancel}
      wide
      footer={
        <div className="pad-footer">
          <div className="pad-summary">
            {valueCounts.length > 0 ? (
              <div className="pad-value-counts" aria-label="Số lượng từng điểm">
                {valueCounts.map(({ value, count }) => (
                  <span key={value} className="pad-value-count">
                    <em>{value}</em>
                    <span>×{count}</span>
                  </span>
                ))}
              </div>
            ) : null}
            <div className="pad-summary-main">
              <span className="pad-summary-label">Tổng điểm</span>
              <strong className="pad-summary-total">{liveTotal}</strong>
              {liveClass ? <span className="pad-summary-class">{liveClass}</span> : null}
            </div>
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
        <header className="pad-hero">
          <p className="pad-hero-name">{shooter.name.trim() || 'Chưa có tên'}</p>
          {valueCounts.length > 0 ? (
            <div className="pad-value-counts" aria-label="Số lượng từng điểm">
              {valueCounts.map(({ value, count }) => (
                <span key={value} className="pad-value-count">
                  <em>{value}</em>
                  <span>×{count}</span>
                </span>
              ))}
            </div>
          ) : null}
          <p className="pad-hero-total">
            <span className="pad-hero-total-label">Tổng</span>
            <strong>{liveTotal}</strong>
            {liveClass ? <span className="pad-hero-class">{liveClass}</span> : null}
          </p>
        </header>
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
                        <div key={ri} className="pad-round pad-round-inline">
                          <span className="pad-round-label">Phát {ri + 1}</span>
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
