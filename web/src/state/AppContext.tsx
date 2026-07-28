import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import {
  clonePreset,
  createDefaultPreset,
  createEmptyShooter,
  ensureShotMatrix,
  flatTargets,
  getRoundCounts,
  newId,
  type AppState,
  type Group,
  type ScorePreset,
  type Shooter,
  type ShootingSession,
} from '../domain/types'
import {
  downloadSqliteBackup,
  isDefaultDbOfferVisible,
  isWorkspaceEmpty,
  loadBundledDefaultState,
  loadState,
  markDefaultDbConsumed,
  readBackupFile,
  reopenDefaultDbOffer,
  saveState,
} from '../storage/db'

interface AppContextValue {
  state: AppState
  ready: boolean
  statusMessage: string
  isBusy: boolean
  busyMessage: string
  showDefaultDbButton: boolean
  selectedGroup: Group | null
  selectedSession: ShootingSession | null
  selectedPreset: ScorePreset | null
  setStatus: (msg: string) => void
  runBusy: (message: string, work: () => Promise<void>) => Promise<void>
  persistNow: () => Promise<void>
  selectGroup: (id: string) => void
  selectSession: (id: string | null) => void
  addGroup: () => Group
  updatePreset: (preset: ScorePreset) => void
  renameGroup: (groupId: string, name: string) => void
  deleteGroup: (groupId: string) => void
  createSession: (name: string, groupId: string, personCount: number) => void
  deleteSession: (sessionId: string) => void
  updateSessionShooters: (sessionId: string, shooters: Shooter[]) => void
  updateShooter: (sessionId: string, shooter: Shooter) => void
  deleteSelectedData: (sessionIds: string[], groupIds: string[]) => void
  exportBackup: () => void
  importBackup: (file: File) => Promise<void>
  loadDefaultDb: () => Promise<void>
  replaceState: (next: AppState) => Promise<void>
  getPresetForGroup: (groupId: string) => ScorePreset | null
}

const AppContext = createContext<AppContextValue | null>(null)

export function AppProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AppState | null>(null)
  const [statusMessage, setStatusMessage] = useState('Đang tải...')
  const [isBusy, setIsBusy] = useState(false)
  const [busyMessage, setBusyMessage] = useState('')
  const [showDefaultDbButton, setShowDefaultDbButton] = useState(false)
  const persistTimer = useRef<number | null>(null)
  const stateRef = useRef<AppState | null>(null)

  useEffect(() => {
    stateRef.current = state
  }, [state])

  useEffect(() => {
    void (async () => {
      const loaded = await loadState()
      const normalized = ensureOnePresetPerGroup(loaded)
      setState(normalized)
      setShowDefaultDbButton(await isDefaultDbOfferVisible())
      setStatusMessage('Đã tải dữ liệu')
    })()
  }, [])

  const schedulePersist = useCallback((next: AppState) => {
    if (persistTimer.current) window.clearTimeout(persistTimer.current)
    persistTimer.current = window.setTimeout(() => {
      void saveState(next).then(() => {
        setStatusMessage(`Đã lưu ${new Date().toLocaleTimeString('vi-VN')}`)
      })
    }, 900)
  }, [])

  const commit = useCallback(
    (updater: (prev: AppState) => AppState, persist = true) => {
      setState((prev) => {
        if (!prev) return prev
        const next = ensureOnePresetPerGroup(updater(prev))
        if (persist) schedulePersist(next)
        return next
      })
    },
    [schedulePersist],
  )

  const persistNow = useCallback(async () => {
    if (persistTimer.current) {
      window.clearTimeout(persistTimer.current)
      persistTimer.current = null
    }
    const current = stateRef.current
    if (!current) return
    await saveState(current)
    setStatusMessage(`Đã lưu ${new Date().toLocaleTimeString('vi-VN')}`)
  }, [])

  const runBusy = useCallback(async (message: string, work: () => Promise<void>) => {
    setIsBusy(true)
    setBusyMessage(message)
    try {
      await work()
    } finally {
      setIsBusy(false)
      setBusyMessage('')
    }
  }, [])

  const selectedGroup = useMemo(() => {
    if (!state) return null
    return state.groups.find((g) => g.id === state.activeGroupId) ?? state.groups[0] ?? null
  }, [state])

  const selectedSession = useMemo(() => {
    if (!state) return null
    return state.sessions.find((s) => s.id === state.activeSessionId) ?? null
  }, [state])

  const selectedPreset = useMemo(() => {
    if (!state || !selectedGroup) return null
    return state.presets.find((p) => p.id === selectedGroup.presetId) ?? null
  }, [state, selectedGroup])

  const getPresetForGroup = useCallback(
    (groupId: string) => {
      if (!state) return null
      const group = state.groups.find((g) => g.id === groupId)
      if (!group) return null
      return state.presets.find((p) => p.id === group.presetId) ?? null
    },
    [state],
  )

  const value: AppContextValue = {
    state: state ?? {
      presets: [],
      groups: [],
      sessions: [],
      activeGroupId: null,
      activeSessionId: null,
    },
    ready: state !== null,
    statusMessage,
    isBusy,
    busyMessage,
    showDefaultDbButton,
    selectedGroup,
    selectedSession,
    selectedPreset,
    setStatus: setStatusMessage,
    runBusy,
    persistNow,
    selectGroup: (id) =>
      commit((prev) => ({ ...prev, activeGroupId: id })),
    selectSession: (id) =>
      commit((prev) => ({ ...prev, activeSessionId: id })),
    addGroup: () => {
      const preset = createDefaultPreset('Nhóm mới')
      preset.clusters = []
      const group: Group = {
        id: newId(),
        name: 'Nhóm mới',
        presetId: preset.id,
        shooters: [],
      }
      commit((prev) => ({
        ...prev,
        presets: [...prev.presets, preset],
        groups: [...prev.groups, group],
        activeGroupId: group.id,
      }))
      return group
    },
    updatePreset: (preset) =>
      commit((prev) => {
        const group = prev.groups.find((g) => g.presetId === preset.id)
        const presets = prev.presets.map((p) => (p.id === preset.id ? preset : p))
        const groups = group
          ? prev.groups.map((g) => (g.id === group.id ? { ...g, name: preset.name } : g))
          : prev.groups
        const rounds = getRoundCounts(preset)
        const sessions = prev.sessions.map((s) => {
          if (!group || s.groupId !== group.id) return s
          return {
            ...s,
            shooters: s.shooters.map((sh) => ensureShotMatrix(sh, rounds)),
          }
        })
        return { ...prev, presets, groups, sessions }
      }),
    renameGroup: (groupId, name) =>
      commit((prev) => {
        const group = prev.groups.find((g) => g.id === groupId)
        if (!group) return prev
        return {
          ...prev,
          groups: prev.groups.map((g) => (g.id === groupId ? { ...g, name } : g)),
          presets: prev.presets.map((p) =>
            p.id === group.presetId ? { ...p, name } : p,
          ),
        }
      }),
    deleteGroup: (groupId) =>
      commit((prev) => {
        if (prev.groups.length <= 1) return prev
        const group = prev.groups.find((g) => g.id === groupId)
        if (!group) return prev
        const fallback = prev.groups.find((g) => g.id !== groupId)!
        const groups = prev.groups.filter((g) => g.id !== groupId)
        const presets = prev.presets.filter((p) => p.id !== group.presetId)
        const sessions = prev.sessions.map((s) =>
          s.groupId === groupId ? { ...s, groupId: fallback.id } : s,
        )
        return {
          ...prev,
          groups,
          presets,
          sessions,
          activeGroupId:
            prev.activeGroupId === groupId ? fallback.id : prev.activeGroupId,
        }
      }),
    createSession: (name, groupId, personCount) =>
      commit((prev) => {
        const preset = prev.presets.find(
          (p) => p.id === prev.groups.find((g) => g.id === groupId)?.presetId,
        )
        if (!preset || flatTargets(preset).length === 0) return prev
        const rounds = getRoundCounts(preset)
        const shooters = Array.from({ length: Math.max(1, personCount) }, (_, i) =>
          createEmptyShooter(i + 1, rounds),
        )
        const session: ShootingSession = {
          id: newId(),
          name,
          groupId,
          createdAt: new Date().toISOString(),
          shooters,
        }
        return {
          ...prev,
          sessions: [...prev.sessions, session],
          activeSessionId: session.id,
        }
      }),
    deleteSession: (sessionId) =>
      commit((prev) => {
        const next = {
          ...prev,
          sessions: prev.sessions.filter((s) => s.id !== sessionId),
          activeSessionId:
            prev.activeSessionId === sessionId ? null : prev.activeSessionId,
        }
        if (isWorkspaceEmpty(next)) {
          void reopenDefaultDbOffer().then(() => setShowDefaultDbButton(true))
        }
        return next
      }),
    updateSessionShooters: (sessionId, shooters) =>
      commit((prev) => ({
        ...prev,
        sessions: prev.sessions.map((s) =>
          s.id === sessionId ? { ...s, shooters } : s,
        ),
      })),
    updateShooter: (sessionId, shooter) =>
      commit((prev) => ({
        ...prev,
        sessions: prev.sessions.map((s) =>
          s.id !== sessionId
            ? s
            : {
                ...s,
                shooters: s.shooters.map((sh) => (sh.id === shooter.id ? shooter : sh)),
              },
        ),
      })),
    deleteSelectedData: (sessionIds, groupIds) => {
      commit((prev) => {
        let sessions = prev.sessions.filter((s) => !sessionIds.includes(s.id))
        let groups = prev.groups
        let presets = prev.presets
        let activeGroupId = prev.activeGroupId

        if (groupIds.length > 0) {
          const remaining = prev.groups.filter((g) => !groupIds.includes(g.id))
          if (remaining.length === 0) return prev
          const removedPresetIds = new Set(
            prev.groups.filter((g) => groupIds.includes(g.id)).map((g) => g.presetId),
          )
          groups = remaining
          presets = prev.presets.filter((p) => !removedPresetIds.has(p.id))
          sessions = sessions.filter((s) => !groupIds.includes(s.groupId))
          if (activeGroupId && groupIds.includes(activeGroupId)) {
            activeGroupId = remaining[0].id
          }
        }

        const next = {
          ...prev,
          groups,
          presets,
          sessions,
          activeGroupId,
          activeSessionId: sessions.some((s) => s.id === prev.activeSessionId)
            ? prev.activeSessionId
            : null,
        }

        if (isWorkspaceEmpty(next)) {
          void reopenDefaultDbOffer().then(() => setShowDefaultDbButton(true))
        }

        return next
      })
    },
    exportBackup: () => {
      if (!stateRef.current) return
      void (async () => {
        try {
          await downloadSqliteBackup(stateRef.current!)
          setStatusMessage('Đã tải file sao lưu SQLite (.thbs)')
        } catch (e) {
          setStatusMessage(`Lỗi sao lưu: ${e instanceof Error ? e.message : String(e)}`)
        }
      })()
    },
    importBackup: async (file) => {
      const next = ensureOnePresetPerGroup(await readBackupFile(file))
      setState(next)
      await saveState(next)
      await markDefaultDbConsumed()
      setShowDefaultDbButton(false)
      setStatusMessage('Phục hồi thành công')
    },
    loadDefaultDb: async () => {
      const seeded = await loadBundledDefaultState()
      if (!seeded) throw new Error('Không tải được file DB mặc định.')
      const next = ensureOnePresetPerGroup(seeded)
      setState(next)
      await saveState(next)
      await markDefaultDbConsumed()
      setShowDefaultDbButton(false)
      setStatusMessage('Đã nạp CSDL mặc định')
    },
    replaceState: async (next) => {
      const normalized = ensureOnePresetPerGroup(next)
      setState(normalized)
      await saveState(normalized)
    },
    getPresetForGroup,
  }

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>
}

export function useApp(): AppContextValue {
  const ctx = useContext(AppContext)
  if (!ctx) throw new Error('useApp must be used within AppProvider')
  return ctx
}

function ensureOnePresetPerGroup(state: AppState): AppState {
  const presets = [...state.presets]
  const claimed = new Set<string>()
  const groups = state.groups.map((g) => {
    let preset = presets.find((p) => p.id === g.presetId)
    if (!preset) {
      const created = createDefaultPreset(g.name)
      presets.push(created)
      claimed.add(created.id)
      return { ...g, presetId: created.id }
    }
    if (claimed.has(preset.id)) {
      const clone = clonePreset(preset, g.name)
      presets.push(clone)
      claimed.add(clone.id)
      return { ...g, presetId: clone.id }
    }
    claimed.add(preset.id)
    return g
  })

  const used = new Set(groups.map((g) => g.presetId))
  return {
    ...state,
    groups,
    presets: presets.filter((p) => used.has(p.id)),
  }
}
