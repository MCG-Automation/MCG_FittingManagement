# SESSION_LOG.md — Tiến độ theo session

# Claude Code tự cập nhật file này. Không sửa thủ công phần log.

---

## Session 2026-04-20 (3) — Checklist: Remove (đã có) + N/A cho custom items

### Đã làm
- **Remove item**: xác nhận chức năng đã có sẵn — nút `X` ở mỗi dòng chỉ hiển thị khi `IsCustom=true` (Visibility binding qua `BooleanToVisibilityConverter`). Fixed items không có nút X → không xóa được. Giữ nguyên, không thay đổi.
- **N/A cho custom items (mới)**:
  - [Models/CheckList/CheckList.Models.cs](Models/CheckList/CheckList.Models.cs): thêm property `IsNotApplicable`, implement `INotifyPropertyChanged` trên `ChecklistItem`. Setter của `IsChecked` và `IsNotApplicable` có **mutual exclusion** — bật cái này tự tắt cái kia.
  - [Views/CheckList/CheckList.View.xaml](Views/CheckList/CheckList.View.xaml): data template tăng từ 3 cột lên 4 cột (IsChecked | Content | N/A | Delete). Checkbox N/A dùng chung binding `BooleanToVisibilityConverter` với nút X → chỉ hiện cho custom items. Thêm `DataTrigger` trên TextBlock để **strikethrough + xám + italic** khi `IsNotApplicable=true`.
  - [Views/CheckList/CheckList.View.xaml.cs](Views/CheckList/CheckList.View.xaml.cs): thêm handler `NaCheckBox_Click`; đổi logic `UpdateProgress` đếm `IsChecked || IsNotApplicable` là "done" → Sign & Approve enable khi tất cả items đều satisfied.

### Trạng thái
- **Phase:** 1 — Feature Implementation (Checklist module).
- **Build:** Succeeded — 0 errors, 5 warnings (pre-existing).

### Bước tiếp theo
- Test trong AutoCAD: mở checklist → thêm custom item → tick N/A → xác nhận text gạch ngang + Sign & Approve enable khi tất cả items đều satisfied.
- Test backward-compat: mở bản vẽ cũ có checklist đã lưu trước đó → `IsNotApplicable` default = false, không vỡ JSON.

### Ghi chú API
- JSON.NET trên .NET Framework 4.8 serialize/deserialize class có `INotifyPropertyChanged` bình thường (auto-properties và backing fields đều OK). Không cần `[JsonIgnore]` cho sự kiện `PropertyChanged`.
- Mutual exclusion phải thực hiện ở setter **sau khi** `OnPropertyChanged` của property gốc đã được raise, để tránh binding WPF bị nhầm trình tự cập nhật.

---

## Session 2026-04-20 (2) — Thêm nút X (Close) đóng Palette trong QaChecklistView

### Đã làm
- Thêm nút `BtnClosePalette` (ký tự `X`, 24×24) ở **góc trên phải** của [Views/CheckList/CheckList.View.xaml](Views/CheckList/CheckList.View.xaml) — `Grid.Row=0`, `HorizontalAlignment=Right`.
- Shift các row hiện có xuống: Status GroupBox → Row 1, action buttons → Row 2, `PanelChecklist` → Row 3. Tổng grid nay 4 rows (`Auto, Auto, Auto, *`).
- Thêm handler `BtnClosePalette_Click` trong [Views/CheckList/CheckList.View.xaml.cs](Views/CheckList/CheckList.View.xaml.cs) gọi `PaletteManager.Instance.Hide()` (import thêm `MCGCadPlugin.Commands`).

### Trạng thái
- **Phase:** 1 — Feature Implementation (Checklist module).
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test trong AutoCAD: `MCG_Show` → tab Checklist → bấm X → xác nhận Palette ẩn; `MCG_Show` lại → Palette hiện lại đúng trạng thái.

### Ghi chú API
- `PaletteManager.Instance.Hide()` chỉ set `_paletteSet.Visible = false` — không dispose, nên state tab + control giữ nguyên khi mở lại.

---

## Session 2026-04-20 (1) — Gộp ChecklistWindow vào QaChecklistView

### Đã làm
- **Gộp cửa sổ modal `ChecklistWindow` thành panel inline** trong `QaChecklistView`:
  - Panel checklist (header, progress bar, list items, add custom, Save/Approve buttons) nay nằm ở `Grid.Row=2` của `QaChecklistView`, `Visibility=Collapsed` mặc định.
  - Nút `OPEN CHECKLIST` hiển thị panel; nút `Cancel` ẩn panel; `Save Draft` / `SIGN & APPROVE` lưu rồi ẩn panel + `RefreshStatus()`.
  - Khi user đổi Discipline trước khi bấm OPEN → nạp lại default items tương ứng (chỉ khi chưa APPROVED).
  - Khi `_currentDoc.Status == "APPROVED"`: khoá list, ẩn Save Draft, đổi nút thành `ALREADY APPROVED` (giữ hành vi gốc).
- **Xóa 2 file không còn dùng**: `Views/CheckList/CheckList.Window.xaml` và `CheckList.Window.xaml.cs`.
- Thêm `LOG_PREFIX = "[QaChecklistView]"`, thêm `BooleanToVisibilityConverter` vào `UserControl.Resources`.

### Files đã sửa
- [Views/CheckList/CheckList.View.xaml](Views/CheckList/CheckList.View.xaml) — thêm `PanelChecklist` inline.
- [Views/CheckList/CheckList.View.xaml.cs](Views/CheckList/CheckList.View.xaml.cs) — merge toàn bộ logic của `ChecklistWindow`.

### Files đã xóa
- `Views/CheckList/CheckList.Window.xaml`
- `Views/CheckList/CheckList.Window.xaml.cs`

### Trạng thái
- **Phase:** 1 — Feature Implementation (Checklist module).
- **Build:** Succeeded — 0 errors, 2 warnings (pre-existing, không liên quan).

### Bước tiếp theo
- Test trong AutoCAD: `MCG_Show` → tab Checklist → `OPEN CHECKLIST` → tick items → `Save Draft` / `SIGN & APPROVE` → kiểm tra status refresh đúng, không còn cửa sổ modal.
- Cân nhắc di chuyển logic ra ViewModel (MVVM) khi mở rộng thêm tính năng.

### Ghi chú API
- `Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(window)` không còn cần thiết — panel nằm trực tiếp trong PaletteSet nên không bị chặn tương tác với bản vẽ khi cần.

---

## Session 2026-04-10 (1) — Project Audit

### Đã làm
- Thực hiện **Audit dự án** theo skill `audit-project`.
- Đối soát 5 lớp kiến trúc (Commands, Models, Services, Views, Utilities) cho 5 module.

### Trạng thái thực tế
| File/Folder | Tình trạng | Ghi chú |
|---|---|---|
| `Views/*.xaml` | ✅ OK | 5 UserControl đã sẵn sàng. |
| `ViewModels/` | ❌ THIẾU | Chưa triển khai MVVM hoàn chỉnh. |
| `Interfaces/` | ❌ THIẾU | Mới chỉ có cho FittingManagement. |
| `Commands/` | ⚠️ Cần sửa | Thiếu đăng ký CommandMethod cho Palette. |
| `_Template/` | ❌ THIẾU | Chưa có folder mẫu cho các layer. |

### Trạng thái
- **Phase:** 1 — Feature Implementation (Bị nghẽn do thiếu ViewModel).
- **Build:** Succeeded (nhưng code chỉ là placeholder).

### Bước tiếp theo
1. **File:** `Views/DetailDesign/DetailDesignViewModel.cs` | Tạo ViewModel đầu tiên làm mẫu.
2. **File:** `Commands/PaletteManager.cs` | Thêm `[CommandMethod]` cho `MCG_Show` và `MCG_Hide`.
3. **File:** `Services/_Template/ITemplateService.cs` | Tạo bộ template chuẩn.

### Ghi chú API
- Cần chú ý việc bind `DataContext` của View vào ViewModel trong code-behind của UserControl.
- PaletteSet yêu cầu các lệnh Show/Hide phải nằm trong một class được AutoCAD nhận diện (thường là static hoặc singleton).

---

## Session 2026-04-09 (2) — Triển khai Import IDW + Import JSON

### Đã làm

**Triển khai 2 tính năng mới cho FittingManagement module:**

1. **Import IDW (Inventor COM Interop)** — `Services/FittingManagement/Import/FittingManagementService.IdwImport.cs` (MỚI)
   - Dùng late-binding COM (`Marshal.GetActiveObject` / `Activator.CreateInstance`) để kết nối Inventor
   - Trích xuất iProperties: PartNumber, Description, Revision, Mass, Material, Designer, Title
   - Duyệt Sheets → DrawingViews → ViewMetadata
   - Export DWG qua Inventor DWG Translator Add-In (GUID: `{C24E3AC2-122E-11D5-8E91-0010B541CD80}`)
   - Lưu FittingMetadata ra JSON vào `C:\Temp_BIM_Library`

2. **Import JSON + Tạo Block + Catalog** — `Services/FittingManagement/Import/FittingManagementService.JsonImport.cs` (MỚI)
   - Đọc JSON → FittingMetadata, tìm DWG cùng tên
   - Tạo block definition qua `Database.Insert()` từ side database
   - Inject 7 attributes: PART_ID, DESC, MASS, MATERIAL, REVISION, BOM_TYPE, POS_NUM
   - Map layer: PANEL → `MCG_Fitting_Panel` (blue), DETAIL → `MCG_Fitting_Detail` (red)
   - Đăng ký vào MasterCatalog.json qua `MergeItemsToJson()`

**Files đã sửa:**
- `Services/FittingManagement/IFittingManagementService.cs` — Thêm 2 method signatures
- `Views/FittingManagement/FittingManagementView.xaml.cs` — Thay 2 stub MessageBox bằng OpenFileDialog + gọi service

**Files đã tạo:**
- `Services/FittingManagement/Import/FittingManagementService.IdwImport.cs`
- `Services/FittingManagement/Import/FittingManagementService.JsonImport.cs`

### Trạng thái

- **Phase:** 1 — Feature Implementation
- Build succeeded — 0 errors
- Step 1 (Import IDW) và Step 2 (Import JSON) đã hoạt động

### Bước tiếp theo

1. Test thực tế: Load plugin vào AutoCAD → MCG_Show → Fitting tab → Import .idw files (cần Inventor)
2. Test Import JSON: Chọn JSON + DWG pair → kiểm tra block + catalog

### Ghi chú API

- **Inventor COM late-binding**: Dùng `dynamic` + `Type.GetTypeFromProgID("Inventor.Application")` — không cần reference DLL, không sửa .csproj
- **DWG Translator Add-In GUID**: `{C24E3AC2-122E-11D5-8E91-0010B541CD80}` — chuẩn cho tất cả phiên bản Inventor
- **Inventor iProperties path**: `PropertySets["Design Tracking Properties"]` chứa PartNumber, Description, Mass, Material; `PropertySets["Inventor Summary Information"]` chứa Title
- **Mass property**: Inventor trả về `double`, cần convert `.ToString("F3")`
- **COM lifecycle**: Track `weStartedInventor` flag — chỉ `Quit()` nếu ta khởi tạo, tránh kill session đang chạy của user

---

## Session 2026-04-09

### Đã làm

**Fix toàn bộ build errors (4 root causes):**

1. **Fix x:Class namespace trong 5 XAML files** — `ShipAutoCadPlugin.UI.*` → `MCGCadPlugin.Views.FittingManagement.*`:
   - `Views/FittingManagement/BOM/BomPreviewWindow.xaml`
   - `Views/FittingManagement/Library/FittingLibraryWindow.xaml`
   - `Views/FittingManagement/Library/Accessory/AccessoryManagerWindow.xaml`
   - `Views/FittingManagement/Library/Accessory/NewAccessoryWindow.xaml`
   - `Views/FittingManagement/Library/VirtualItemWindow.xaml`

2. **Chuyển 4 placeholder View thành WPF UserControl** (tạo .xaml + .xaml.cs, xóa .cs cũ):
   - `Views/DetailDesign/DetailDesignView.xaml` + `.xaml.cs`
   - `Views/PanelData/PanelDataView.xaml` + `.xaml.cs`
   - `Views/TableOfContent/TableOfContentView.xaml` + `.xaml.cs`
   - `Views/Weight/WeightView.xaml` + `.xaml.cs`

3. **Fix ambiguous Exception trong PaletteManager.cs** — thêm `using Exception = System.Exception;`

4. **Xóa `RecalculateSize`** — property không tồn tại trên PaletteSet AutoCAD 2023

### Trạng thái

- **Phase:** 0 — Scaffold & Setup ✅ HOÀN THÀNH
- Build succeeded — 0 errors, 0 warnings
- Plugin sẵn sàng load vào AutoCAD

### Bước tiếp theo

1. Test load plugin vào AutoCAD 2023 — chạy lệnh `MCG_Show`
2. Bắt đầu Phase 1 — triển khai logic cho từng Module

### Ghi chú API

- **x:Class phải khớp namespace code-behind** — nếu XAML dùng `ShipAutoCadPlugin.UI.X` mà code-behind dùng `MCGCadPlugin.Views.Y.X` thì WPF không generate partial class, gây lỗi `InitializeComponent` và tất cả control names
- **`Autodesk.AutoCAD.Runtime.Exception` xung đột với `System.Exception`** — khi `using Autodesk.AutoCAD.Runtime`, cần disambiguate bằng `using Exception = System.Exception;`
- **`PaletteSet.RecalculateSize` không có trong AutoCAD 2023 .NET API** — property này không tồn tại, xóa bỏ

---

## Session 2026-04-08

### Đã làm

**File đã sửa:**
- `CLAUDE.md` — Thêm bảng danh sách module (5 module), bổ sung pattern PaletteSet (thứ tự khởi tạo bắt buộc), thêm quy tắc `SetFocusToDwgView()`, cập nhật GUID thật, đổi log message sang English (theo user chỉnh)
- `Commands/PaletteManager.cs` — Fix 4 lỗi audit: cú pháp KeepFocus, thứ tự khởi tạo, size 400x600, GUID thật

**File placeholder đã tạo (25 file):**
- `Commands/{Module}/{Module}Command.cs` — 5 file (DetailDesign, FittingManagement, PanelData, TableOfContent, Weight)
- `Models/{Module}/{Module}Model.cs` — 5 file
- `Services/{Module}/{Module}Service.cs` — 5 file
- `Views/{Module}/{Module}View.cs` — 5 file
- `Utilities/{Module}/{Module}Utility.cs` — 5 file (đổi từ Helper → Utility cho khớp folder name)

**Folder đã tạo:**
- 5 module folders trong mỗi layer (`Commands/`, `Models/`, `Services/`, `Views/`, `Utilities/`):
  - `FittingManagement`, `Weight`, `TableOfContent`, `DetailDesign`, `PanelData`
- Folder `Module1`, `Module2` (template gốc) vẫn còn — chưa xóa, chờ user confirm

### Trạng thái

- **Phase:** 0 — Scaffold & Setup
- Cấu trúc folder + placeholder files đã xong, sẵn sàng push lên GitHub
- PaletteManager.cs đã fix xong audit
- Chưa có file `.xaml` nào — plugin chưa build được (5 View chưa chuyển sang UserControl)

### Vấn đề tồn đọng (từ Audit)

| # | Vấn đề | Trạng thái |
|---|---|---|
| #2.5 | Thiếu `[CommandMethod]` cho MCG_Show/Hide/Toggle | Chờ bổ sung sau |
| #6.2 | Chưa có 5 View + ViewModel | Chờ bổ sung sau |
| #3.3 | `Hide()` thiếu try/catch | Chờ bổ sung sau |
| #3.4 | `Toggle()` thiếu log | Chờ bổ sung sau |
| #3.5 | `Initialize()` thiếu try/catch | Chờ bổ sung sau |
| — | Folder `Module1`, `Module2` chưa xóa | Chờ user confirm |

### Bước tiếp theo

1. **Tạo 5 View (UserControl)** — để plugin build được:
   - `Views/DetailDesign/DetailDesignView.xaml` + `.xaml.cs`
   - `Views/FittingManagement/FittingManagementView.xaml` + `.xaml.cs`
   - `Views/PanelData/PanelDataView.xaml` + `.xaml.cs`
   - `Views/TableOfContent/TableOfContentView.xaml` + `.xaml.cs`
   - `Views/Weight/WeightView.xaml` + `.xaml.cs`
2. **Thêm `[CommandMethod]`** cho MCG_Show / MCG_Hide / MCG_Toggle trong PaletteManager.cs
3. **Tạo 5 ViewModel** tương ứng trong `Views/` hoặc folder riêng
4. **Thử build lần đầu** — `dotnet build -c Debug`

### Ghi chú API

- **PaletteSet thứ tự khởi tạo:** `new PaletteSet()` → `AddVisual()` → `DockEnabled/Size/KeepFocus` → `Visible = true`. Nếu set Size/Dock trước AddVisual, Palette sẽ không dock đúng.
- **KeepFocus = true:** Giữ focus trên palette sau click — cần kết hợp với `Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView()` trong button handler khi muốn trả focus về bản vẽ.
- **GUID cố định:** `2b80cfe9-c560-49d6-8a09-9d636260fcf2` — không được thay đổi sau khi deploy.
- **Log message:** User đã chỉnh quy tắc — log message viết bằng **English** (không phải tiếng Việt như ban đầu).
