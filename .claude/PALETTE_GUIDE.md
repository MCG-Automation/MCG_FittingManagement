# PALETTE_GUIDE.md — Kiến trúc PaletteSet

> Quyết định kiến trúc đã được chốt. Không thảo luận lại.
> Đây là tài liệu tham chiếu duy nhất cho mọi vấn đề liên quan đến PaletteSet.

---

## Nguyên tắc cốt lõi

```
Toàn plugin có DUY NHẤT 1 PaletteSet.
Mọi thứ liên quan PaletteSet đều đi qua PaletteManager.
```

---

## Kiến trúc tổng quan

```
┌─────────────────────────────────────────────────┐
│               MCG Tools (PaletteSet)             │
│  ┌──────────┬──────────┬───────┬───────┬───────┐ │
│  │  Detail  │ Fitting  │ Panel │ Table │Weight │ │
│  │  Design  │  Mgmt    │ Data  │  of   │       │ │
│  │          │          │       │Content│       │ │
│  └──────────┴──────────┴───────┴───────┴───────┘ │
└─────────────────────────────────────────────────┘
           ▲
           │ .Show() / .Hide() / .Toggle()
           │
  ┌─────────────────┐
  │  PaletteManager │  ← Singleton — Commands/PaletteManager.cs
  │    (Singleton)  │
  └─────────────────┘
           ▲
    ┌──────┴───────────────────────────────────┐
    │              Các Command gọi vào         │
    │                                          │
  DetailDesign  FittingMgmt  PanelData  ...   │
  Command.cs    Command.cs   Command.cs        │
    └──────────────────────────────────────────┘
```

---

## Cấu trúc file

```
MCG_FittingManagement/
├── Commands/
│   ├── PaletteManager.cs              ← NGUỒN GỐC DUY NHẤT
│   ├── DetailDesign/
│   │   └── DetailDesignCommand.cs     ← Chỉ gọi PaletteManager.Instance.Show()
│   ├── FittingManagement/
│   │   └── FittingManagementCommand.cs
│   ├── PanelData/
│   │   └── PanelDataCommand.cs
│   ├── TableOfContent/
│   │   └── TableOfContentCommand.cs
│   └── Weight/
│       └── WeightCommand.cs
└── Views/
    ├── DetailDesign/
    │   └── DetailDesignView.xaml      ← UserControl của tab 1
    ├── FittingManagement/
    │   └── FittingManagementView.xaml
    ├── PanelData/
    │   └── PanelDataView.xaml
    ├── TableOfContent/
    │   └── TableOfContentView.xaml
    └── Weight/
        └── WeightView.xaml
```

---

## GUID — Quy tắc vàng

```
GUID được tạo 1 lần duy nhất khi setup project.
Sau khi commit lên GitHub → KHÔNG BAO GIỜ thay đổi.
```

AutoCAD dùng GUID để nhớ:
- Vị trí dock (trái/phải)
- Kích thước cửa sổ
- Trạng thái ẩn/hiện

**Cách tạo GUID (chạy 1 lần duy nhất):**
```powershell
# Chạy trong PowerShell
[guid]::NewGuid()
```

**Điền vào `PaletteManager.cs`:**
```csharp
private static readonly Guid PaletteGuid =
    new Guid("XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX"); // Kết quả từ PowerShell
```

---

## Cách Command gọi PaletteManager

### Pattern chuẩn cho mọi Command file

```csharp
// Commands/DetailDesign/DetailDesignCommand.cs
namespace MCG_FittingManagement.Commands.DetailDesign
{
    /// <summary>Các lệnh AutoCAD cho module Detail Design</summary>
    public class DetailDesignCommand
    {
        private const string LOG_PREFIX = "[DetailDesignCommand]";

        /// <summary>Mở panel Detail Design</summary>
        [CommandMethod("MCG_DetailDesign", CommandFlags.Modal)]
        public void OpenDetailDesign()
        {
            Debug.WriteLine($"{LOG_PREFIX} Gọi MCG_DetailDesign...");
            try
            {
                // Chỉ 1 dòng — gọi qua Singleton, KHÔNG tự tạo PaletteSet
                PaletteManager.Instance.Show();
                Debug.WriteLine($"{LOG_PREFIX} MCG_DetailDesign THÀNH CÔNG.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI: {ex.Message}");
                Application.DocumentManager.MdiActiveDocument
                    .Editor.WriteMessage($"\nLỗi: {ex.Message}");
            }
        }
    }
}
```

---

## Danh sách lệnh CAD

| Lệnh | Command file | Chức năng |
|---|---|---|
| `MCG_Show` | `PaletteManager` trực tiếp | Mở toàn bộ panel |
| `MCG_Hide` | `PaletteManager` trực tiếp | Đóng toàn bộ panel |
| `MCG_Toggle` | `PaletteManager` trực tiếp | Bật/tắt panel |
| `MCG_DetailDesign` | `DetailDesignCommand` | Mở và focus tab Detail Design |
| `MCG_FittingManagement` | `FittingManagementCommand` | Mở và focus tab Fitting |
| `MCG_PanelData` | `PanelDataCommand` | Mở và focus tab Panel Data |
| `MCG_TableOfContent` | `TableOfContentCommand` | Mở và focus tab Table |
| `MCG_Weight` | `WeightCommand` | Mở và focus tab Weight |

---

## Quy tắc bắt buộc

```
✅ ĐƯỢC phép
- Gọi PaletteManager.Instance.Show() / Hide() / Toggle()
- Thêm logic vào UserControl (View) của từng tab
- Thêm tab mới vào PaletteManager.Initialize() khi có Module mới

❌ KHÔNG được phép
- Tạo PaletteSet mới ở bất kỳ file nào ngoài PaletteManager.cs
- Thay đổi PaletteGuid sau khi đã commit lên GitHub
- Thay đổi thứ tự tab sau khi đã deploy cho user
- Để logic nghiệp vụ trong PaletteManager (chỉ quản lý UI)
```

---

## Khi cần thêm Module mới

Chỉ cần 3 bước:

**1. Tạo View mới:**
```
Views/TenModule/TenModuleView.xaml
```

**2. Thêm 1 dòng vào `PaletteManager.Initialize()`:**
```csharp
_paletteSet.AddVisual("Ten Module", new TenModuleView());
```

**3. Tạo Command file:**
```
Commands/TenModule/TenModuleCommand.cs
```
Nội dung: gọi `PaletteManager.Instance.Show()` như pattern chuẩn ở trên.

> ⚠️ Thêm tab vào **cuối danh sách** để không làm xáo trộn index các tab cũ.

---

## Troubleshooting

| Vấn đề | Nguyên nhân | Giải pháp |
|---|---|---|
| Palette không nhớ vị trí dock | GUID bị thay đổi | Khôi phục GUID gốc từ Git |
| Palette mở 2 lần | Tạo PaletteSet ngoài PaletteManager | Xóa, dùng Singleton |
| Tab bị đổi thứ tự | Thay đổi thứ tự `AddVisual` | Không đổi thứ tự sau deploy |
| NullReferenceException khi Show | View chưa được khởi tạo | Kiểm tra constructor của View |