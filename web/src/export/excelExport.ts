import ExcelJS from 'exceljs'
import { saveAs } from 'file-saver'
import { TargetKind, type TargetDefinition } from '../domain/types'

export interface ExcelReportRow {
  index: number
  name: string
  rank: string
  position: string
  unit: string
  groupName: string
  targetDetails: string[]
  total: number
  knockDownCount: number
  classification: string
}

export async function exportReport(
  sessionName: string,
  groupName: string,
  rows: ExcelReportRow[],
  targets: TargetDefinition[],
): Promise<void> {
  const wb = new ExcelJS.Workbook()
  const ws = wb.addWorksheet('Kết quả')
  const hasKnockDown = targets.some((t) => t.kind === TargetKind.KnockDown)
  const lastCol = 6 + targets.length + 2 + (hasKnockDown ? 1 : 0)

  ws.mergeCells(1, 1, 1, lastCol)
  const title = ws.getCell(1, 1)
  title.value = 'BÁO CÁO KẾT QUẢ BẮN SÚNG'
  title.font = { bold: true, size: 16 }
  title.alignment = { horizontal: 'center' }

  ws.getCell(2, 1).value = `Đợt: ${sessionName}`
  ws.getCell(2, 3).value = `Nhóm: ${groupName}`
  ws.getCell(2, 5).value = `Ngày xuất: ${new Date().toLocaleString('vi-VN')}`

  const headerRow = 4
  const headers = ['STT', 'Họ tên', 'Cấp bậc', 'Chức vụ', 'Đơn vị', 'Nhóm', ...targets.map((t) => t.name), 'Tổng']
  if (hasKnockDown) headers.push('Bia đổ')
  headers.push('Xếp loại')

  headers.forEach((h, i) => {
    const cell = ws.getCell(headerRow, i + 1)
    cell.value = h
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF3E6B4F' } }
    cell.alignment = { horizontal: 'center' }
    cell.border = thinBorder()
  })

  rows.forEach((row, idx) => {
    const r = headerRow + 1 + idx
    const values: (string | number)[] = [
      row.index,
      row.name,
      row.rank,
      row.position,
      row.unit,
      row.groupName,
      ...targets.map((_, t) => row.targetDetails[t] ?? ''),
      row.total,
    ]
    if (hasKnockDown) values.push(row.knockDownCount)
    values.push(row.classification)

    values.forEach((v, i) => {
      const cell = ws.getCell(r, i + 1)
      cell.value = v
      cell.border = thinBorder()
      if (r % 2 === 0) {
        cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFF3F6F3' } }
      }
    })
  })

  ws.views = [{ state: 'frozen', ySplit: headerRow }]
  ws.columns.forEach((col) => {
    col.width = 14
  })

  const ws2 = wb.addWorksheet('Thống kê xếp loại')
  ws2.getCell(1, 1).value = 'Xếp loại'
  ws2.getCell(1, 2).value = 'Số lượng'
  ;[1, 2].forEach((c) => {
    const cell = ws2.getCell(1, c)
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF3E6B4F' } }
  })

  const groups = new Map<string, number>()
  for (const row of rows) {
    const key = row.classification || '(trống)'
    groups.set(key, (groups.get(key) ?? 0) + 1)
  }
  let sr = 2
  ;[...groups.entries()]
    .sort((a, b) => b[1] - a[1])
    .forEach(([label, count]) => {
      ws2.getCell(sr, 1).value = label
      ws2.getCell(sr, 2).value = count
      sr++
    })
  ws2.getCell(sr, 1).value = 'Tổng'
  ws2.getCell(sr, 1).font = { bold: true }
  ws2.getCell(sr, 2).value = rows.length
  ws2.getCell(sr, 2).font = { bold: true }
  ws2.columns = [{ width: 20 }, { width: 12 }]

  const buffer = await wb.xlsx.writeBuffer()
  saveAs(
    new Blob([buffer], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    }),
    `BaoCao_${sessionName.replace(/[\\/:*?"<>|]/g, '_')}_${Date.now()}.xlsx`,
  )
}

function thinBorder(): Partial<ExcelJS.Borders> {
  const edge: Partial<ExcelJS.Border> = { style: 'thin', color: { argb: 'FFAAAAAA' } }
  return { top: edge, left: edge, bottom: edge, right: edge }
}
