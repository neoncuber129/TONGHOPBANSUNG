export interface RosterEntry {
  name: string
  rank: string
  position: string
  unit: string
}

function normalizeName(name: string): string {
  let trimmed = name.trim()
  if (trimmed.length > 2 && /\d/.test(trimmed[0])) {
    let i = 0
    while (i < trimmed.length && /[\d.\s)]/.test(trimmed[i])) i++
    if (i < trimmed.length) trimmed = trimmed.slice(i).trim()
  }
  return trimmed
}

function looksLikePersonNameList(parts: string[]): boolean {
  return parts.length > 4
}

/** Tách văn bản dán thành lưới ô như Excel (tab hoặc dấu chấm phẩy). */
export function parseRosterGrid(text: string | null | undefined): string[][] {
  if (!text || !text.trim()) return []
  const lines = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n')
  // Ô trống giữa vùng dán phải giữ nguyên dòng để không lệch hàng; chỉ bỏ dòng thừa ở cuối.
  while (lines.length > 0 && !lines[lines.length - 1].trim()) lines.pop()
  return lines.map((line) => line.split(/[\t;]/).map((cell) => cell.trim()))
}

export function parseRosterEntries(text: string | null | undefined): RosterEntry[] {
  if (!text || !text.trim()) return []

  const entries: RosterEntry[] = []
  const lines = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n')

  for (const rawLine of lines) {
    const line = rawLine.trim()
    if (!line) continue

    if (line.includes('\t') || line.includes(';')) {
      const parts = line.split(/[\t;]/).map((p) => p.trim())
      const name = parts.length > 0 ? normalizeName(parts[0]) : ''
      if (!name.trim()) continue
      entries.push({
        name,
        rank: parts[1] ?? '',
        position: parts[2] ?? '',
        unit: parts[3] ?? '',
      })
    } else if (line.includes(',')) {
      const parts = line.split(',').map((p) => p.trim())
      if (parts.length >= 2 && parts.length <= 4 && !looksLikePersonNameList(parts)) {
        const name = normalizeName(parts[0])
        if (!name.trim()) continue
        entries.push({
          name,
          rank: parts[1] ?? '',
          position: parts[2] ?? '',
          unit: parts[3] ?? '',
        })
      } else {
        for (const part of parts) {
          const name = normalizeName(part)
          if (name.trim()) entries.push({ name, rank: '', position: '', unit: '' })
        }
      }
    } else {
      const name = normalizeName(line)
      if (name.trim()) entries.push({ name, rank: '', position: '', unit: '' })
    }
  }

  return entries
}
