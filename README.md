# Tổng hợp điểm bắn súng

Ứng dụng quản lý nhóm, nhập điểm và xếp loại — **hai bản song song**:

| Bản | Công nghệ | Dữ liệu |
|-----|-----------|---------|
| **Web** | React · Vite · TypeScript | SQLite (sql.js) trong trình duyệt — **cùng schema** với desktop |
| **Desktop (Windows)** | WPF · .NET 10 | SQLite `data.db` cạnh exe |

Repo: [neoncuber129/TONGHOPBANSUNG](https://github.com/neoncuber129/TONGHOPBANSUNG)

Web (GitHub Pages): https://neoncuber129.github.io/TONGHOPBANSUNG/

## Chức năng

- Nhóm / cấu hình bia (phần, bia chấm điểm, bia đổ)
- Quy tắc xếp loại (AND điều kiện)
- Đợt bắn, danh sách người, dán từ Excel
- Nhập điểm, báo cáo, xuất Excel
- Sao lưu / phục hồi **SQLite `.thbs` / `.db`** (dùng chung giữa Web và Windows; vẫn đọc `.json` cũ)

Hai bản **không đồng bộ tự động trên cloud**. Chuyển dữ liệu bằng file `.thbs` (tab Sao lưu).

## Chạy bản Web (local)

```bash
cd web
npm install
npm run dev
```

Build production:

```bash
cd web
npm run build
npm run preview
```

`vite.config.ts` dùng `base: '/TONGHOPBANSUNG/'` cho GitHub Pages.

## Chạy bản Desktop

Mở `Tonghopbansung.slnx` bằng Visual Studio / `dotnet run` (Windows, .NET 10).

## Deploy Pages

Push nhánh `main` → workflow `.github/workflows/deploy.yml` build `web/` và publish GitHub Pages.

Cần bật **Settings → Pages → Source: GitHub Actions**.

## Lưu ý dữ liệu Web

Lần đầu mở (chưa có dữ liệu local), web tự nạp `public/Tonghopbansungdb.thbs` làm CSDL mặc định rồi lưu vào trình duyệt.
Dữ liệu web sau đó nằm SQLite trong trình duyệt hiện tại. Xóa cache / site data sẽ quay lại bản mặc định (nếu file seed còn).
Phục hồi file `.thbs` từ bản Windows trên web (và ngược lại) dùng cùng schema.
