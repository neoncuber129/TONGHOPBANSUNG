import { useMemo, useRef, useState } from 'react'
import { useApp } from '../state/AppContext'
import { Modal } from './Modal'

export function BackupTab() {
  const {
    state,
    persistNow,
    exportBackup,
    importBackup,
    loadDefaultDb,
    deleteSelectedData,
    runBusy,
  } = useApp()
  const fileRef = useRef<HTMLInputElement>(null)
  const [showDelete, setShowDelete] = useState(false)

  const info = useMemo(() => {
    const people = state.sessions.reduce((n, s) => n + s.shooters.length, 0)
    return [
      'CSDL: SQLite (cùng schema bản Windows)',
      'Lưu cục bộ: file data.db trong IndexedDB trình duyệt',
      `Số nhóm: ${state.groups.length}`,
      `Số đợt bắn: ${state.sessions.length}`,
      `Tổng số người: ${people}`,
    ].join('\n')
  }, [state])

  function handleLoadDefaultDb() {
    if (
      !confirm(
        'Nạp CSDL mặc định sẽ ghi đè dữ liệu hiện tại trên trình duyệt này. Tiếp tục?',
      )
    ) {
      return
    }
    void runBusy('Đang nạp CSDL mặc định...', async () => {
      await loadDefaultDb()
    })
  }

  return (
    <div className="backup-tab">
      <pre className="info-box">{info}</pre>
      <div className="backup-actions">
        <button
          type="button"
          className="btn primary"
          onClick={() => {
            exportBackup()
          }}
        >
          Sao lưu (.thbs)
        </button>
        <button type="button" className="btn" onClick={() => fileRef.current?.click()}>
          Phục hồi
        </button>
        <input
          ref={fileRef}
          type="file"
          accept=".thbs,.db,.json,application/json,application/x-sqlite3"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0]
            e.target.value = ''
            if (!file) return
            if (!confirm('Phục hồi sẽ ghi đè toàn bộ dữ liệu hiện tại. Tiếp tục?')) return
            void runBusy('Đang phục hồi...', async () => {
              await importBackup(file)
            })
          }}
        />
        <button type="button" className="btn" onClick={handleLoadDefaultDb}>
          Lấy DB mặc định
        </button>
        <button type="button" className="btn danger" onClick={() => setShowDelete(true)}>
          Xóa dữ liệu...
        </button>
        <button
          type="button"
          className="btn"
          onClick={() =>
            void runBusy('Đang lưu...', async () => {
              await persistNow()
            })
          }
        >
          Lưu ngay
        </button>
      </div>
      <p className="hint">
        Sao lưu/phục hồi dùng SQLite (.thbs / .db) — cùng format với bản Windows (WPF).
        Vẫn đọc được file .json cũ. Dữ liệu web lưu trên trình duyệt này; xóa cache có thể mất data nếu chưa sao lưu.
        Nút «Lấy DB mặc định» nạp lại bộ dữ liệu mẫu kèm theo ứng dụng (ghi đè dữ liệu local).
      </p>

      {showDelete && (
        <DeleteDataDialog
          sessions={state.sessions.map((s) => ({ id: s.id, name: s.name }))}
          groups={state.groups.map((g) => ({ id: g.id, name: g.name }))}
          onCancel={() => setShowDelete(false)}
          onConfirm={(sessionIds, groupIds) => {
            deleteSelectedData(sessionIds, groupIds)
            setShowDelete(false)
          }}
        />
      )}
    </div>
  )
}

function DeleteDataDialog({
  sessions,
  groups,
  onConfirm,
  onCancel,
}: {
  sessions: { id: string; name: string }[]
  groups: { id: string; name: string }[]
  onConfirm: (sessionIds: string[], groupIds: string[]) => void
  onCancel: () => void
}) {
  const [sessionIds, setSessionIds] = useState<string[]>([])
  const [groupIds, setGroupIds] = useState<string[]>([])

  function toggle(list: string[], id: string, set: (v: string[]) => void) {
    set(list.includes(id) ? list.filter((x) => x !== id) : [...list, id])
  }

  return (
    <Modal title="Xóa dữ liệu" onClose={onCancel}>
      <div className="form-stack">
        <h4>Đợt bắn</h4>
        {sessions.length === 0 ? (
          <p className="muted">Không có đợt.</p>
        ) : (
          sessions.map((s) => (
            <label key={s.id} className="check">
              <input
                type="checkbox"
                checked={sessionIds.includes(s.id)}
                onChange={() => toggle(sessionIds, s.id, setSessionIds)}
              />
              {s.name}
            </label>
          ))
        )}
        <h4>Nhóm</h4>
        <p className="muted">Phải giữ ít nhất 1 nhóm. Xóa nhóm sẽ xóa đợt liên quan.</p>
        {groups.map((g) => (
          <label key={g.id} className="check">
            <input
              type="checkbox"
              checked={groupIds.includes(g.id)}
              onChange={() => {
                if (!groupIds.includes(g.id) && groupIds.length >= groups.length - 1) {
                  alert('Phải giữ ít nhất 1 nhóm.')
                  return
                }
                toggle(groupIds, g.id, setGroupIds)
              }}
            />
            {g.name}
          </label>
        ))}
        <div className="row-actions">
          <button type="button" className="btn" onClick={onCancel}>
            Hủy
          </button>
          <button
            type="button"
            className="btn danger"
            disabled={sessionIds.length === 0 && groupIds.length === 0}
            onClick={() => {
              if (!confirm('Xóa các mục đã chọn?')) return
              onConfirm(sessionIds, groupIds)
            }}
          >
            Xóa
          </button>
        </div>
      </div>
    </Modal>
  )
}

export function AboutTab() {
  return (
    <div className="about-tab">
      <h2>Tổng hợp điểm bắn súng</h2>
      <p>Ứng dụng quản lý danh sách, nhập điểm và xếp loại.</p>
      <dl className="about-dl">
        <dt>Tác giả</dt>
        <dd>TM</dd>
        <dt>Nền tảng</dt>
        <dd>Web · React · SQLite (sql.js) — cùng schema bản Windows</dd>
        <dt>Bản desktop</dt>
        <dd>WPF · .NET — SQLite data.db; trao đổi qua file .thbs</dd>
      </dl>
    </div>
  )
}
