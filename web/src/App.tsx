import { useState } from 'react'
import { AboutTab, BackupTab } from './components/BackupTab'
import { GroupsTab } from './components/GroupsTab'
import { BusyOverlay } from './components/Modal'
import { ScoreEntryTab } from './components/ScoreEntryTab'
import { AppProvider, useApp } from './state/AppContext'
import './App.css'

type Tab = 'groups' | 'scores' | 'backup' | 'about'

const TABS: { id: Tab; label: string; short: string }[] = [
  { id: 'groups', label: 'Nhóm', short: 'Nhóm' },
  { id: 'scores', label: 'Danh sách & nhập liệu', short: 'Nhập liệu' },
  { id: 'backup', label: 'Sao lưu', short: 'Sao lưu' },
  { id: 'about', label: 'About', short: 'About' },
]

function Shell() {
  const { ready, statusMessage, isBusy, busyMessage } = useApp()
  const [tab, setTab] = useState<Tab>('groups')

  if (!ready) {
    return (
      <div className="boot">
        <div className="spinner" />
        <p>Đang tải dữ liệu...</p>
      </div>
    )
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand-block">
          <p className="brand">Tổng hợp điểm bắn súng</p>
          <p className="tagline">Quản lý nhóm · nhập điểm · xếp loại</p>
        </div>
        <nav className="tabs tabs-top" aria-label="Điều hướng chính">
          {TABS.map(({ id, label }) => (
            <button
              key={id}
              type="button"
              className={tab === id ? 'tab active' : 'tab'}
              onClick={() => setTab(id)}
            >
              {label}
            </button>
          ))}
        </nav>
      </header>

      <main className="app-main">
        {tab === 'groups' && <GroupsTab />}
        {tab === 'scores' && <ScoreEntryTab />}
        {tab === 'backup' && <BackupTab />}
        {tab === 'about' && <AboutTab />}
      </main>

      <footer className="status-bar">{statusMessage}</footer>

      <nav className="tabs-bottom" aria-label="Điều hướng mobile">
        {TABS.map(({ id, short }) => (
          <button
            key={id}
            type="button"
            className={tab === id ? 'tab-bottom active' : 'tab-bottom'}
            onClick={() => setTab(id)}
          >
            <span className="tab-bottom-label">{short}</span>
          </button>
        ))}
      </nav>

      {isBusy && <BusyOverlay message={busyMessage || 'Đang xử lý...'} />}
    </div>
  )
}

export default function App() {
  return (
    <AppProvider>
      <Shell />
    </AppProvider>
  )
}
