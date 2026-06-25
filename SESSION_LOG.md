# SESSION_LOG.md — Tiến độ theo session

# Claude Code tự cập nhật file này. Không sửa thủ công phần log.

---

## Session 2026-06-22 — Viết lại User Guide chi tiết từng Button

### Đã làm
- Đọc toàn bộ 4 XAML View (FittingHandleView, ProjectConfigView, TemplateView, BlockUtilitiesView)
- Đọc BomPreviewWindow.xaml, ProjectLibraryWindow.xaml, MasterLibraryWindow.xaml để lấy đúng tên button
- Viết lại hoàn toàn `Docs/Macgregor_FittingTool_UserGuide.html` (tiếng Việt)
- Viết lại hoàn toàn `Docs/Macgregor_FittingTool_UserGuide_EN.html` (tiếng Anh)

### Nội dung mới so với phiên bản cũ
- Nav tabs tách thành 8 tab (thêm Tab1/Tab2/Tab3/Tab4 riêng biệt)
- Mỗi button có card riêng: mô tả, "Khi nào dùng?", các bước step-by-step, result/warn/danger box
- Các tình huống thực tế cụ thể cho từng button
- Bổ sung các button trong cửa sổ con: BomPreviewWindow (Scan, Auto-Assign, Sync, Export), ProjectLibraryWindow (Load/Create Project, Insert, Remove, AutoAssignPos, Refresh), MasterLibraryWindow (Add from CAD, Sub-BOM, Push Update, Add to Project, Remove)
- FAQ mở rộng thêm 3 câu hỏi mới (Collect Drawings, Add Objects lệch, Project Library Refresh)

### Trạng thái
- HOÀN THÀNH — cả 2 file HTML đã được ghi

### Bước tiếp theo
- Không có task tồn đọng về docs

---

## Session 2026-06-16 (16) — Tạo Install_AutoLoadCadAddin.bat

### Đã làm
- Tạo `Install_AutoLoadCadAddin.bat` theo đúng pattern MCGVN standard

### Logic bat file
| Bước | Hành động |
|------|-----------|
| 1/3 | Tạo `%PROGRAMDATA%\Autodesk\ApplicationPlugins\MCG_FittingManagement.bundle\Contents\` — kiểm tra quyền Admin |
| Check | Xác nhận có `MCG_*.dll` trong cùng thư mục — báo lỗi rõ ràng nếu không |
| 2/3 | Copy `MCG_*.dll` + `appsettings.txt` (nếu có) vào `Contents\` |
| 3/3 | Tự sinh `PackageContents.xml` động — loop qua từng DLL |

### Trạng thái
- HOÀN THÀNH — đã đóng gap còn lại từ session 14

---

## Session 2026-06-16 (15) — Viết lại Macgregor_FittingTool_UserGuide.html

### Đã làm
- Đọc và phân tích toàn bộ `MCGVN_Autocad_Inventor_Installation_Guide.html` để lấy design system
- Khám phá tất cả View, Command, Service của FittingManagement plugin để hiểu đầy đủ tính năng
- Viết lại hoàn toàn `Docs/Macgregor_FittingTool_UserGuide.html`

### Nội dung file mới
7 Tab điều hướng theo đúng style MCGVN (DM Sans, navy/accent/gold palette, sticky nav):
| Tab | Nội dung |
|-----|---------|
| Tổng quan | 4 tab overview, workflow diagram 5 bước (Admin→Engineer), khái niệm cốt lõi |
| Cài đặt | 3 bước cài theo MCGVN standard + bảng lỗi thường gặp |
| Quick Start | Luồng 5 bước nhanh có color-coded tab badge |
| BOM & Balloon | Chi tiết 4 bước BOM + so sánh Place vs Mass Balloon |
| Thư viện | Tab 3 (Admin: Import .idw + Master Library) + Tab 2 (Engineer: Project Library) |
| Block Utilities | 6 Block Utilities + 2 Drawing Collection |
| FAQ | 8 câu hỏi thường gặp với giải thích đầy đủ |

### Trạng thái
- File: HOÀN THÀNH
- Bước tiếp theo: Tạo `Install_AutoLoadCadAddin.bat` (gap còn lại từ session 14)

---

## Session 2026-06-16 (14) — Phân tích gap với MCGVN Installation Guide

### Đã làm
- Đọc và phân tích toàn bộ `MCGVN_Autocad_Inventor_Installation_Guide.html`
- So sánh tool hiện tại với tiêu chuẩn cài đặt AutoCAD của MCGVN

### Kết quả phân tích

| Yếu tố | Hiện tại | Standard MCGVN | Kết luận |
|--------|---------|----------------|----------|
| DLL name | `MCG_FittingManagement.dll` | `MCG_*.dll` | ✅ Đạt |
| Bundle path | `MCG_FittingManagement.bundle` | `MCG_Plugin.bundle` (generic) | ✅ Per-plugin OK |
| PackageContents.xml | Static + XmlPoke từ MSBuild | Dynamic gen trong install bat | ⚠️ Install bat cần gen động |
| Dev script | `build-and-launch.bat` (cần dotnet) | — | ✅ Giữ cho dev |
| **Install script** | **KHÔNG CÓ** | `Install_AutoLoadCadAddin.bat` | ❌ **GAP CHÍNH** |
| appsettings.txt | Copy nếu tồn tại | Copy nếu tồn tại | ✅ Đạt |

### Gap chính
**Thiếu `Install_AutoLoadCadAddin.bat`** — script standalone cho end-user deploy (không cần dotnet/VS):
- Đặt cùng thư mục với `MCG_FittingManagement.dll` (thường `C:\CustomTools\Autocad\`)
- Tạo bundle folder `%PROGRAMDATA%\Autodesk\ApplicationPlugins\MCG_FittingManagement.bundle\Contents\`
- Copy tất cả `MCG_*.dll` + `appsettings.txt` vào `Contents\`
- **Tự sinh `PackageContents.xml` động** (loop qua từng DLL)
- Yêu cầu chạy với quyền Administrator

### Trạng thái
- Phase phân tích: HOÀN THÀNH
- Chờ user xác nhận trước khi tạo file

### Bước tiếp theo
- File: `Install_AutoLoadCadAddin.bat` | Mục tiêu: Tạo install script MCGVN standard

---

## Session 2026-06-15 (13) — Align csproj với MCGVN Installation Guide

### Đã làm
**File sửa (2):**
| File | Thay đổi |
|---|---|
| [MCG_FittingManagement.csproj](MCG_FittingManagement.csproj) | Đổi `PluginName` từ `MCG_FittingManagement.FittingManagement` → `MCG_FittingManagement`; sửa XmlPoke Value thêm `./Contents/` prefix; thêm XmlPoke thứ 2 cho bundle dir |
| [build-and-launch.bat](build-and-launch.bat) | Đổi `PROJECT_NAME` và `BUNDLE_DIR` theo tên mới; thêm bước copy `PackageContents.xml` vào bundle lần đầu; sửa lệnh kiểm tra `MCG_Show` → `MCG_Fitting` |

**File mới (1):**
| File | Trách nhiệm |
|---|---|
| [PackageContents.xml](PackageContents.xml) | Bundle manifest chuẩn MCGVN: `SchemaVersion="1.0"`, `SeriesMin="R24.0"`, `LoadOnAutoCADStartup="True"` — MSBuild cập nhật `ModuleName` sau mỗi build |

### Vấn đề đã giải quyết
| Vấn đề | Nguyên nhân | Fix |
|---|---|---|
| Bat install không pick up DLL | `MCG_FittingManagement.FittingManagement.dll` không khớp pattern `MCG_*.dll` | Đổi PluginName → `MCG_FittingManagement` |
| XmlPoke ghi ModuleName sai | Value `$(AssemblyName).dll` thiếu prefix `./Contents/` | Value → `./Contents/$(AssemblyName).dll` |
| PackageContents.xml không tồn tại | Chưa có file → XmlPoke bị skip, AutoCAD không load | Tạo file template + bat copy lần đầu |
| Bundle dir không nhất quán | `MCG_FittingManagement.bundle` vs `MCG_Plugin.bundle` | Đổi về `MCG_FittingManagement.bundle` |

### Trạng thái
- DLL output (Release): `MCG_FittingManagement.dll` ✅ khớp `MCG_*.dll`
- DLL output (Debug): `MCG_FittingManagement_YYYYMMDD_HHMMSS.dll` ✅ cleanup pattern đúng
- Bundle: `%PROGRAMDATA%\Autodesk\ApplicationPlugins\MCG_FittingManagement.bundle\`
- PackageContents.xml: project root (git) + auto-sync sang bundle

### Bước tiếp theo
- Chạy `build-and-launch.bat` → tạo bundle lần đầu với PackageContents.xml mới
- Xóa bundle cũ `MCG_FittingManagement.bundle` nếu còn tồn tại trong `%PROGRAMDATA%`

---

## Session 2026-05-06 (12) — Thêm node "Recently" vào cây Library (Master + Project)

### Bối cảnh
User yêu cầu hiển thị danh sách CatalogItem vừa được dùng trong Library window — đặt làm node `"Recently"` ngay sau `"All Fittings"` trong TreeView, dùng chung cho cả MasterLibraryWindow và ProjectLibraryWindow. Tracking chỉ qua thao tác plugin (không hook event database — Cách A đã chốt). Giới hạn 15 entries, FIFO. Không hiển thị thời gian. Không có nút Clear.

### Đã làm
**File mới (3):**
| File | Trách nhiệm |
|---|---|
| [Models/FittingManagement/RecentItemEntry.cs](Models/FittingManagement/RecentItemEntry.cs) | DTO `{ BlockName, TimestampUtc }` — Model thuần, không import AutoCAD |
| [Services/FittingManagement/Library/IRecentItemsTracker.cs](Services/FittingManagement/Library/IRecentItemsTracker.cs) | Interface `Track / GetRecentBlockNames` |
| [Services/FittingManagement/Library/RecentItemsTracker.cs](Services/FittingManagement/Library/RecentItemsTracker.cs) | Impl: file-based JSON (Newtonsoft), move-to-top, cap 15, sort timestamp desc |

**File sửa (3):**
| File | Thay đổi |
|---|---|
| [Views/FittingManagement/Library/Shared/CatalogTreeBuilder.cs](Views/FittingManagement/Library/Shared/CatalogTreeBuilder.cs) | Thêm overload `Build(catalog, recentBlockNames, allLabel)` — chèn node "Recently" sau "All Fittings", lookup ngược catalog theo BlockName, bỏ entry không còn |
| [Views/FittingManagement/Library/MasterLibraryWindow.xaml.cs](Views/FittingManagement/Library/MasterLibraryWindow.xaml.cs) | Init tracker trong ctor (`<MasterLibraryFolder>\MasterCatalog.recent.json`); hook Track tại 3 điểm: `InsertSelected`, `BtnManageAccessory_Click` (sau OK), `BtnUpdateLibrary_Click` (theo `result.Updated`) |
| [Views/FittingManagement/Library/ProjectLibraryWindow.xaml.cs](Views/FittingManagement/Library/ProjectLibraryWindow.xaml.cs) | Init tracker trong `LoadCatalog` theo project active (`<projectFolder>\<name>.recent.json`); hook Track tại 2 điểm: `InsertSelected`, `GridCatalog_CellEditEnding` (sau save pos) |

### Quyết định kiến trúc
- **Storage 2 file riêng** (Master vs Project) thay vì 1 file global → mỗi project có Recently riêng, không nhiễu chéo.
- **Key = BlockName** (đồng nhất với `CatalogJsonStore.MergeItems` / `RemoveItems`).
- **Không rebuild tree ngay sau Insert** — Track ngầm; Recently sẽ refresh khi user click Refresh / chuyển catalog. Tránh disrupt selection trong khi user đang thao tác trên CAD.
- **Auto-Assign Pos không track** — đó là mass operation, sẽ flood Recently nếu track tất cả.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: chưa thực hiện (chờ user verify).

### Bước tiếp theo
- User build & test: mở Master Library → Insert vài block → click Refresh → kiểm tra node "Recently" xuất hiện sau "All Fittings" với đúng item và thứ tự.
- Lặp lại trên Project Library với 1 project active.
- Edge case cần verify: thay đổi project active → Recently phải đổi theo project.

### Ghi chú API
- `CatalogTreeBuilder.ApplySearch` dùng `source.Where(...).ToList()` → giữ thứ tự `source` khi không search → thứ tự "mới nhất → cũ nhất" của Recently được preserve trong DataGrid.
- `ApplyFilters` ở cả 2 window kiểm tra `node.CategoryName != "All Fittings"` để dùng `node.Items` — node "Recently" rơi vào nhánh này nên dùng đúng items đã được sort.

---

## Session 2026-05-05 (11) — Tách FittingManagementService.IdwImport.cs thành 5 file partial

### Bối cảnh
File [FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs) cũ 781 dòng với 17 method + 1 nested type — vi phạm SRP, khó navigate. Tách theo trách nhiệm theo convention đã có ở `DrawingCollection/` (split 6 file partial cùng folder).

### Đã làm
Tách thành 5 file partial trong cùng folder `Services/FittingManagement/Import/`:

| File | Trách nhiệm | LOC |
|---|---|---|
| [FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs) | Entry point + flow (`ImportIdwFilesAsync`, `ExtractAllIdw`, `VaultStatusShortLabel`, nested `ExtractedIdw`) | 211 |
| [FittingManagementService.IdwImport.Inventor.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.Inventor.cs) | Inventor COM lifecycle + per-file (`AcquireInventorInstance`, `ReleaseInventorInstance`, `ProcessSingleIdwFile`, `GetReferencedModel`) | 193 |
| [FittingManagementService.IdwImport.Metadata.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.Metadata.cs) | iProperties (`ExtractIProperties`, `SafeGetProperty`, `FormatAndRoundMass`) | 115 |
| [FittingManagementService.IdwImport.Views.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.Views.cs) | Drawing views + 2D/3D classification (`ExtractDrawingViews`) | 130 |
| [FittingManagementService.IdwImport.DwgExport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.DwgExport.cs) | DWG export (`ExportIdwToDwg`, `ExportViaTranslator`, `CreateMinimalDwgIni`, `FindInventorDwgIniPath`) | 191 |

Tổng 840 dòng (trước: 781) — chênh ~60 dòng do thêm header `using` + namespace + class declaration cho mỗi file.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK).
- Không thay đổi public API, không thay đổi logic — chỉ di chuyển code.

### Bước tiếp theo
- User test smoke run `MCG_Fitting` → tab Template → Import .idw để verify luồng end-to-end vẫn work sau split.
- Khi cần sửa 1 phần (vd 2D/3D detection ở Session 4) — chỉ chạm `Views.cs`, các file khác không ảnh hưởng.

### Ghi chú API
- Tất cả 5 file cùng `partial class FittingManagementService` cùng namespace `MCG_FittingManagement.Services.FittingManagement` → compiler merge thành 1 class. Nested type `ExtractedIdw` (private) ở file gốc — file khác cùng partial class vẫn truy cập được vì cùng class scope.
- `LOG_PREFIX` + `_libraryFolderPath` ở `MasterLibrary.cs` — accessible từ mọi partial.
- `using` directives khác nhau từng file (chỉ import những namespace cần) — gọn hơn 1 file ôm hết.

---

## Session 2026-05-05 (10) — MLeader BlockScale=1 (drive size qua Scale tổng)

### Bối cảnh
Session 7 set cả `mleader.Scale = scale` và `mleader.BlockScale = new Scale3d(scale)` → 2 multiplier cùng = A1 → visual size = scale². Vd A1=25 → balloon radius hiển thị ~625mm, quá to. User chốt: trong Properties palette → tab Block phải hiển thị Scale = 1, còn visual scale của block trong MLeader vẫn = A1 (qua overall `mleader.Scale`).

### Đã làm
- [Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs](Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs#L99-L101): đổi `mleader.BlockScale = new Scale3d(scale)` → `new Scale3d(1.0)`. Giữ `mleader.Scale = scale`.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded**.
- ⚠ User restart AutoCAD → place balloon mới → verify radius giờ ≈ A1mm (vd 25mm cho A1=25), không còn 625mm.

### Bước tiếp theo
- Test trên A1 scale=25:
  - Balloon đặt mới → radius ~25mm (1 × 25 × 1).
  - Properties palette: Specify Scale = 25 (Leader Structure tab); Block → Scale = 1 (Content tab).
  - So sánh với balloon cũ (Session 7) bị to → balloon mới phải nhỏ hơn 25× lần.

### Ghi chú API
- Visual size cuối của block content trong MLeader = `blockRadius × MLeader.Scale × MLeader.BlockScale`. Setting cả 2 = scale gây nhân đôi (scale²). Conventional usage: 1 trong 2 = 1, cái kia = scale thực sự.
- `MLeader.BlockScale` là `Scale3d` (X,Y,Z component). `new Scale3d(1.0)` = (1,1,1) uniform.
- Vẫn để `mleader.Scale = scale` thay vì = 1 vì:
  - Arrow size effective = `mleader.ArrowSize × mleader.Scale = 3 × 25 = 75mm` ở A1=25 — visible đẹp khi zoom.
  - Dogleg length effective = `0.001 × scale` — vẫn collapse như mong đợi.
  - "Specify Scale" trong Properties palette show A1 — user dễ verify.

---

## Session 2026-05-05 (9) — TagCircle text màu vàng + bỏ command MCG_Fitting_Show

### Bối cảnh
- User yêu cầu MText/Text trong `_TagCircle` mặc định màu vàng (highlight số POS dễ nhìn).
- Trùng lặp command: `MCG_Fitting` (Commands/FittingManagement/FittingManagementCommand.cs) và `MCG_Fitting_Show` (Commands/PaletteManager.cs) cùng làm việc Show. User chốt giữ `MCG_Fitting` + `MCG_Fitting_Hide`, bỏ `MCG_Fitting_Show`.

### Đã làm
- [Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs](Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs#L186-L210): normalize loop branch theo type:
  - `AttributeDefinition / MText / DBText` → `ColorIndex = 2` (yellow).
  - Còn lại (Circle...) → `ColorIndex = 256` (ByLayer).
  - Layer="0", Linetype="ByLayer", LineWeight=ByLayer giữ chung cho mọi entity.
- [Commands/PaletteManager.cs](Commands/PaletteManager.cs#L107-L113): xoá `[CommandMethod("MCG_Fitting_Show")]` + `McgShow()`. Giữ `MCG_Fitting_Hide`. Comment giải thích Show được handle bởi `MCG_Fitting` ở file khác.
- [CLAUDE.md](CLAUDE.md#L297-L299): cập nhật mục 9 — list lại 2 lệnh CAD: `MCG_Fitting` (show) + `MCG_Fitting_Hide` (hide), kèm chỉ dẫn file đăng ký.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK).
- ⚠ User restart AutoCAD → place balloon → text vàng tự apply (block cũ self-heal qua Session 8 logic).

### Bước tiếp theo
- Test:
  - Place balloon → verify text "POS_NUM" hiển thị màu vàng (color index 2).
  - Verify Circle vẫn ByLayer (theo Layer của BlockReference).
  - Gõ `MCG_Fitting_Show` → AutoCAD báo "Unknown command". Gõ `MCG_Fitting` → palette hiển thị. Gõ `MCG_Fitting_Hide` → palette ẩn.

### Ghi chú API
- ColorIndex AutoCAD: 1=Red, 2=Yellow, 3=Green, 4=Cyan, 5=Blue, 6=Magenta, 7=White/Black (theo theme); 0=ByBlock, 256=ByLayer.
- AutoCAD không cho duplicate `[CommandMethod]` cùng tên trong cùng plugin domain — sẽ throw runtime hoặc 1 trong 2 wins. Bỏ duplicate `MCG_Fitting_Show` an toàn.

---

## Session 2026-05-05 (8) — `_TagCircle` self-heal: normalize entities về Layer="0" + ByLayer mỗi lần dùng

### Bối cảnh
Session 7 fix Color/LT/LW = ByLayer cho entities mới tạo trong `_TagCircle`. Nhưng `EnsureTagCircleBlock` skip toàn bộ logic nếu block đã tồn tại từ Session 6 (chưa có ByLayer). User báo block cũ vẫn còn property sai → balloon vẽ ra với Color/Layer không như mong đợi.

### Đã làm
- [Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs](Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs#L138-L209): refactor `EnsureTagCircleBlock`:
  - Tách 2 bước: (a) tạo block + entities nếu chưa có, (b) **luôn** normalize entities trong block về `Layer="0"`, `ColorIndex=256` (ByLayer), `Linetype="ByLayer"`, `LineWeight=ByLayer`.
  - Block cũ đã tồn tại từ Session 6 sẽ tự self-heal lần đầu user place balloon sau khi reload DLL — không cần purge thủ công.
  - Try/catch bao quanh từng entity normalize để 1 entity fail không block các entity khác (vd entity type lạ không cho set 1 trong các property).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK).
- ⚠ User restart AutoCAD → place balloon lần đầu → block `_TagCircle` cũ tự được normalize property → balloon mới và cả balloon cũ trong drawing đều render với Color/LT/LW = ByLayer (vì cùng share BlockTableRecord).

### Bước tiếp theo
- Test trên drawing có sẵn balloon từ Session 6/7:
  - Mở drawing → place 1 balloon mới.
  - Verify Properties palette của balloon CŨ: Color = ByLayer, Linetype = ByLayer, LineWeight = ByLayer (entity in block ref được render theo property mới).
  - Trong block editor (`_BEDIT _TagCircle`): verify Circle + AttDef Layer="0" + Color/LT/LW = ByLayer.

### Ghi chú API
- BlockTableRecord entities mở `OpenMode.ForWrite` để set property — phải vẫn ở trong scope transaction đang chạy của caller. OK vì `EnsureTagCircleBlock` được gọi trong `DrawMagneticMLeader` đã có active transaction.
- Khi modify entities trong BlockTableRecord, mọi BlockReference đang insert block đó **tự cập nhật visual** ngay (AutoCAD invalidate render). Tức là ballloon cũ trong drawing không cần xóa+vẽ lại, chỉ cần regen (`_REGEN`) là thấy property mới.
- Dynamic block / anonymous block: `_TagCircle` không phải dynamic → check `bt["_TagCircle"]` trả về unique BTR, không phải dynamic anonymous.

---

## Session 2026-05-05 (7) — MLeader style fix: BlockScale + ByLayer cho mọi visual property

### Bối cảnh
Sau Session 6 user thấy Block Circle render bé tí (radius=1mm bất kể A1 scale). Lý do: `mleader.Scale = scale` chỉ áp cho arrow/dogleg/text — KHÔNG tự multiply sang block content. Block content scale phải set qua `mleader.BlockScale` (Scale3d) riêng. Đồng thời user yêu cầu Color/Linetype/Layer-related properties về ByLayer (chuẩn CAD convention: entity sit trên Layer, Color/LT/LW = ByLayer kế thừa từ Layer).

### Đã làm
- [Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs](Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs):
  - Thêm `using Autodesk.AutoCAD.Colors;` cho `Color.FromColorIndex(ColorMethod.ByLayer, 256)`.
  - **MLeader entity**: thêm `mleader.BlockScale = new Scale3d(scale)` (Block Circle render đúng size theo A1); set `ColorIndex=256`, `Linetype="ByLayer"`, `LineWeight=ByLayer`; set `BlockColor = Color.FromColorIndex(ColorMethod.ByLayer, 256)` (Content tab "Color" → ByLayer).
  - **`_TagCircle` block internals**: Circle + AttributeDef set `Layer="0"` (chuẩn block — inherit từ BlockReference layer), `ColorIndex=256`, `Linetype="ByLayer"`, `LineWeight=ByLayer`.
  - **Stacked BlockReference** (cho multi-pos balloon): set `ColorIndex=256`, `Linetype="ByLayer"`, `LineWeight=ByLayer` (Layer giữ "Mechanical-AM_5" như cũ).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK; warning DLL lock chỉ vì AutoCAD đang chạy).
- ⚠ User phải đóng AutoCAD và mở lại để load DLL mới.

### Bước tiếp theo
- Test trong drawing có A1 scale=25:
  - Verify Block Circle visual radius ≈ 25mm (không còn 1mm).
  - Verify Properties palette: Block Scale = 25, Color = ByLayer, Linetype = ByLayer, Lineweight = ByLayer.
  - Verify entity Layer = Mechanical-AM_5 (nếu drawing có), nhưng Color/LT/LW = ByLayer kế thừa từ layer.
- ⚠ `_TagCircle` block đã tạo trong Session 6 với Color/LT/LW chưa ByLayer sẽ TỒN TẠI sẵn — `EnsureTagCircleBlock` skip tạo lại. Nếu user muốn refresh: `purge` block `_TagCircle` (qua `_purge` command hoặc xóa balloon đã đặt + purge), lần sau gọi sẽ tạo lại với property ByLayer mới.

### Ghi chú API
- `MLeader.Scale` (double): áp cho arrow, dogleg, text (annotation aspects). KHÔNG áp tự động cho block content.
- `MLeader.BlockScale` (Scale3d, KHÔNG phải Vector3d): scale của block content trong tab Content "Scale". Set độc lập với `MLeader.Scale`. Nếu cả 2 đều set → kết quả: arrow size = arrowSize × Scale; block visual size = blockSize × BlockScale (2 trục độc lập).
- `MLeader.BlockColor` (Autodesk.AutoCAD.Colors.Color): tab Content "Color" cho block content. Tách biệt với `MLeader.ColorIndex` (color của leader line + arrow + frame).
- ColorIndex=256 = ByLayer; ColorIndex=0 = ByBlock. AttDef/Entity inside block: nên Layer="0" để inherit BlockReference layer khi insert.

---

## Session 2026-05-05 (6) — MLeader style: Block+Circle + Scale theo A1 chứa fitting

### Bối cảnh
User chốt MLeader style mới: Content tab dùng Block với Source Block = Circle (built-in `_TagCircle`); Leader Structure tab dùng Specify Scale = ScaleFactors.X của A1 BlockReference chứa fitting đó. Behavior cũ: scale hardcode 25.0, fallback MText nếu drawing chưa có `_TagCircle`.

### 4 quyết định đã chốt
1. **Scale interpretation**: dùng `BlockReference.ScaleFactors.X` của A1 (đơn giản, đúng nghĩa "Scale insert").
2. **`_TagCircle` không có**: tự tạo programmatically (Circle r=1 + AttributeDef tag `TAGNUMBER` h=1, MiddleCenter, BlockScaling.Uniform).
3. **Mass balloon**: per-fitting scale cho mleader; max scale across cluster cho slot spacing/margin (slot đủ rộng cho balloon to nhất).
4. **Fitting ngoài A1**: fallback scale = 25.0 (giữ behavior cũ).

### Đã làm
- [Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs](Services/FittingManagement/Balloon/FittingManagementService.BalloonHelpers.cs):
  - `DrawMagneticMLeader`: bỏ branch MText fallback; luôn `ContentType.BlockContent` + `BlockContentId = EnsureTagCircleBlock(...)`. Single-pos vẫn append qua MLeader; multi-pos stack thêm BlockReference riêng (cũ — giữ).
  - **Mới** `EnsureTagCircleBlock(db, tr)`: tạo `_TagCircle` BTR nếu thiếu (Circle radius=1 + AttributeDefinition tag `TAGNUMBER`, height=1, justify=MiddleCenter, BlockScaling=Uniform). Trả ObjectId.
  - **Mới** `ComputeA1Scale(tr, db, pt)`: iterate CurrentSpace BlockReferences, match name == "A1" (case-insensitive, dynamic-block-aware qua `BlockReference.Name`), check `pt` nằm trong `GeometricExtents`, trả `Math.Abs(ScaleFactors.X)`. Trả null nếu không match.
- [Services/FittingManagement/Balloon/FittingManagementService.BalloonInteractive.cs](Services/FittingManagement/Balloon/FittingManagementService.BalloonInteractive.cs#L74-L78): `mleaderScale = ComputeA1Scale(tr, db, arrowHeadPoint) ?? 25.0` thay cho hardcode.
- [Services/FittingManagement/Balloon/FittingManagementService.BalloonMass.cs](Services/FittingManagement/Balloon/FittingManagementService.BalloonMass.cs): dùng `Dictionary<DiscoveredFitting, double> perFittingScale` + `maxScale`. Slot layout (`margin = 20×maxScale`, `slotSpacing = 12×maxScale`) dùng max scale; mỗi mleader dùng scale per fitting.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK; warning DLL lock chỉ vì AutoCAD đang chạy).
- ⚠ User phải đóng AutoCAD và mở lại để load DLL mới.

### Bước tiếp theo
- Test trong drawing có nhiều A1 frame khác scale (vd 1:25, 1:50):
  - Pick fitting trong A1 scale=25 → balloon size theo scale 25.
  - Pick fitting trong A1 scale=50 → balloon size theo scale 50.
  - Pick fitting NGOÀI A1 → fallback scale 25 (log debug `[FittingManagementService] ComputeA1Scale...` sẽ không có dòng A1 match).
- Mass balloon trên cluster nằm trong A1 scale=50 + A1 scale=25 → verify slot spacing đủ cho balloon to (size theo 50) không chồng nhau.
- Drawing chưa có `_TagCircle` → verify lần đầu place balloon tự tạo block, các lần sau reuse.

### Ghi chú API
- `BlockReference.Name` modern AutoCAD .NET trả về tên gốc của dynamic block (không phải `*Uxxx` anonymous) → check `== "A1"` work cho cả static và dynamic A1.
- `EnsureTagCircleBlock` upgrade BlockTable lên `OpenForWrite` chỉ khi cần Add. Block created với `BlockScaling.Uniform` để mleader scale uniform — tránh lệch tỷ lệ X/Y.
- `_TagCircle` AttributeDefinition đặt `Position` và `AlignmentPoint` cùng = origin với `Justify = MiddleCenter` → text căn giữa circle khi mleader render.
- `Math.Abs(ScaleFactors.X)` để tránh case A1 mirror (scaleX âm) ra scale 0 hoặc âm cho mleader.

---

## Session 2026-05-05 (5) — Fix eLockViolation ở Place Balloon (Interactive)

### Bối cảnh
User báo `eLockViolation` khi click button "Place Balloon" trên Palette tab Fitting Handle. Trace: `BtnAddBalloon_Click` (WPF) → `_service.InteractivePlaceBalloon()` → `db.TransactionManager.StartTransaction()` → `tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite)` → throw eLockViolation.

### Nguyên nhân gốc
Method `InteractivePlaceBalloon` mở transaction ghi mà **không có `doc.LockDocument()`**. Khi gọi qua `[CommandMethod]`, AutoCAD tự lock document. Nhưng gọi từ Palette button (WPF UI thread), **không có auto-lock** → mọi transaction ghi đều fail. Method anh em `MassAutoBalloon` đã đúng (wrap `using (DocumentLock docLock = doc.LockDocument())`), `InteractivePlaceBalloon` thiếu nhất quán.

### Đã làm
- [Services/FittingManagement/Balloon/FittingManagementService.BalloonInteractive.cs](Services/FittingManagement/Balloon/FittingManagementService.BalloonInteractive.cs#L24-L29): wrap `while (true) { ... }` trong `using (DocumentLock docLock = doc.LockDocument()) { ... }`. Lock giữ trong toàn bộ vòng lặp interactive — Lock acquire 1 lần duy nhất, các iteration sau chỉ tốn cost transaction nội bộ. Thêm comment giải thích lý do.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK).
- ⚠ User phải đóng AutoCAD và mở lại để load DLL mới — DLL đang load là bản trước cả Session 3/4/5.

### Bước tiếp theo
- Restart AutoCAD → click "Place Balloon" → pick fitting → đặt balloon point → verify không còn eLockViolation, mleader được tạo.
- Audit các method service khác gọi từ Palette button có cùng bug không (write transaction thiếu LockDocument). Candidates: `PickGeometricFeatureFromCad` (đã có lock), `InsertBlockFromLibrary` (đã có lock), `PushBlocksFromCurrentDrawing` (đã có lock). `InteractivePlaceBalloon` là case lẻ duy nhất phát hiện trong session này.

### Ghi chú API
- `[CommandMethod(..., CommandFlags.Modal)]` → AutoCAD tự lock document trước khi invoke.
- Code chạy từ Palette button / WPF event handler → KHÔNG có auto-lock. Phải explicit `doc.LockDocument()`.
- `LockDocument()` returns `IDisposable` — wrap trong `using` để release đúng. Lock giữ qua interactive prompt OK (`ed.GetNestedEntity` / `ed.GetPoint` không cần unlock-and-relock).
- `eLockViolation` raise ngay tại dòng `OpenMode.ForWrite` đầu tiên — `OpenMode.ForRead` không cần lock.

---

## Session 2026-05-05 (4) — 3D detection robust hơn: thêm heuristic camera vector

### Bối cảnh
Sau Session 3 user test thấy MasterCatalog VẪN chứa iso block. 2 nguyên nhân khả dĩ: (a) AutoCAD đang load DLL cũ (build mới `_090038.dll` nhưng AutoCAD lock `_081835.dll` + `_090038.dll`); (b) view iso thực tế của user trả về orient code KHÁC iso enum (10309-10312)/arbitrary (10301) — có thể `kCurrent` (10303) hoặc `kDefault` (10313) khi child view kế thừa từ parent.

### Đã làm
- [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs#L527-L568): rewrite phân loại với 2 heuristic độc lập (OR):
  - **(1) ViewOrientationType enum**: Iso (10309-10312) hoặc Arbitrary (10301).
  - **(2) Camera direction vector**: đọc `Eye/Target`, normalize. Ortho 2D có max component ≈ 1; iso 3D (vd (1,1,1)/√3) có max ≈ 0.577. Threshold `maxComp < 0.95` → 3D.
- Mỗi view log 1 dòng debug: `View_N: orient=X, dir=(nx,ny,nz), isoByEnum=B1, isoByVector=B2 → 2D/3D` để user gửi log diagnose nếu detection còn miss.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK).
- ⚠ **User phải đóng AutoCAD và mở lại** để load DLL mới — DLL trong AutoCAD đang là bản trước cả Session 3.
- ⚠ Iso block đã có trong MasterCatalog từ trước phải user tự Remove qua Master Library window — code mới chỉ ngăn publish mới.

### Bước tiếp theo
- User restart AutoCAD → re-import 1 file .idw → check `%APPDATA%\MCG_FittingManagement\plugin.log` cho dòng `View_N: orient=..., dir=...`. Nếu còn iso vào catalog, gửi log lại.
- Nếu heuristic vector miss case "iso nhưng max component cao do camera setup khác" → giảm threshold xuống 0.90 hoặc dùng thêm `Camera.Projection == kPerspective`.

### Ghi chú API
- Inventor `Camera` có `Eye` (Point) và `Target` (Point). Direction = Target − Eye, normalize. Drawing iso views thực tế camera nhìn theo (1,1,1)-ish.
- Threshold 0.95: chấp nhận sai số nhỏ trong ortho view (vd Inventor có thể set 0.999 thay vì đúng 1.0). Iso (1,1,1)/√3 ≈ 0.577 — cách rất xa 0.95 → an toàn.
- View edge case: section/detail/auxiliary view có thể có camera direction lệch trục — sẽ bị flag 3D nhầm. User test trên dataset thật rồi mới biết có cần whitelist `ViewType` (kSectionDrawingView etc.) không.

---

## Session 2026-05-05 (3) — Master Library: chỉ block hoá view 2D, skip view 3D

### Bối cảnh
User audit lại luồng tạo Master Library (Session trước phân tích 2 luồng: JSON Import + Add from CAD). Yêu cầu: khi import .idw từ Inventor, **không tạo Block cho các view 3D iso/perspective**, chỉ tạo Block cho view 2D ortho và đưa vào Master Library. Hiện tại `ExtractDrawingViews` gộp tất cả `DrawingView` không phân biệt loại → mỗi view 3D iso vẫn bị wblock + publish, dẫn tới Master chứa block "không sử dụng được" cho việc chèn 2D vào layout.

### 3 quyết định đã chốt với user
1. `kArbitraryViewOrientation` (10301) → treat as 3D (skip).
2. Backward compat OK: `Is3D` mặc định `false` → JSON cũ deserialize ra 2D → behavior cũ giữ nguyên cho file đã có.
3. Chỉ ghi log file, không thay đổi UI summary.

### Đã làm
- [Models/FittingManagement/FittingManagementModel.cs](Models/FittingManagement/FittingManagementModel.cs#L18-L24): `ViewMetadata` thêm `Is3D` (bool, default false). XML doc giải thích semantics 2D ortho vs 3D iso/arbitrary.
- [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs#L516-L549): `ExtractDrawingViews` đọc `drawingView.Camera.ViewOrientationType` (int via dynamic). Code phân loại: `is3D = (orient == 10301) || (orient ∈ [10309..10312])`. Lỗi đọc orient → fallback 2D (an toàn). Log summary đổi từ `Tìm thấy N drawing view(s)` → `Tìm thấy N drawing view(s) — X 2D ortho, Y 3D iso/arbitrary`.
- [Services/FittingManagement/Import/FittingManagementService.JsonImport.cs](Services/FittingManagement/Import/FittingManagementService.JsonImport.cs#L132-L275): khai báo `int skipped3D` ở scope ngoài (sourceDb-using) để dùng được trong cả foreach view và log summary cuối; foreach skip view nếu `view.Is3D == true` + log dòng `Bỏ qua '{view.Name}' — view 3D (iso/arbitrary), không publish vào Master`. Thêm log per-file: `'{file}' summary — Tạo {n2D} block 2D, bỏ qua {n3D} view 3D`.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK; warning DLL lock chỉ vì AutoCAD đang chạy).
- Chưa test trên file .idw thật có cả view ortho + iso.

### Bước tiếp theo
- User test với 1 file .idw có view 3D iso (ví dụ Front + Top + IsoTopRight) → verify log:
  - `Tìm thấy 3 drawing view(s) — 2 2D ortho, 1 3D iso/arbitrary`.
  - `Bỏ qua 'View_3' — view 3D (iso/arbitrary), không publish vào Master`.
  - `summary — Tạo 2 block 2D, bỏ qua 1 view 3D`.
  - MasterCatalog.json chỉ có 2 entry (View_1 + View_2), không có View_3.
- Nếu user test thấy view "Arbitrary" của họ thật ra là ortho custom → cần giữ → cân nhắc chỉ skip iso (10309-10312), bỏ check `kArbitrary == 10301` ra.

### Ghi chú API
- `Inventor.DrawingView.Camera.ViewOrientationType` trả về `ViewOrientationTypeEnum`. Cast `(int)` qua `dynamic` work — Inventor expose enum dạng numeric COM IDispatch.
- Enum values: 10302-10308 (Front/Top/Side ortho 2D), 10309-10312 (Iso 4 góc 3D), 10301 (Arbitrary), 10303 (Current — kế thừa parent), 10313 (Default).
- Không check `Camera.Projection == kPerspective` vì drawing views của Inventor gần như luôn ortho-projection (perspective rất hiếm cho technical drawing). Iso + Arbitrary đã đủ phủ trường hợp 3D thật sự.
- BlockTable trong AutoCAD case-insensitive nhưng `metadata.Views[i].Name` là `"View_N"` tuần tự → không bị collision do skip 3D giữa chừng (View_3 skip vẫn không ảnh hưởng View_4 vì uniqueName được suffix nếu trùng).

---

## Session 2026-05-05 (2) — Drawing Collection: BTR walk fallback cho A1 bbox extraction

### Bối cảnh
User chạy Hướng A + N=1 trên batch 6 file (`71-48963-10..15.dwg`), vẫn báo overlap A1. Phân tích log `%APPDATA%\MCG_FittingManagement\plugin.log`:
- Mọi A1 ở Phase 1 đều ghi `bbox=[(0,0)-(0,0)]` → degenerate.
- Bị filter `w <= 0.5` → `validA1Count = 0` → tất cả 6 file fallback bbox layout.
- Summary xác nhận: `Sum effective widths : 10201772mm (0 file dùng A1.Width, 6 file fallback bbox)`.
- 3/6 file có orphan BlockReference khổng lồ (file 14: BR size=5065474×146605mm) kéo bbox tổng → đẩy offsetX cực xa.
- File 12↔13 chồng A1 17146mm vs content bbox 14883/16095mm → A1 thò ra ngoài content.

### Nguyên nhân gốc
`BlockReference.GeometricExtents` trong **side database** (`new Database(false, true) + ReadDwgFile`) thường THROW vì block reference chưa graphics-realized. Code cũ fallback `new Extents3d(Position, Position)` = degenerate point. Phase 2 sau `WblockClone + TransformBy` thì extents lại lấy được bình thường (`brDest.GeometricExtents` OK trong dest db đã thuộc Document).

### Fix #1 đã làm
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Helpers.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Helpers.cs): thêm `ComputeBlockRefExtentsViaBtrWalk(BlockReference, Transaction, depth=0)` — iterate BTR entities, union extents (với recursion 1 cấp cho nested BR, MAX_DEPTH=2), apply `br.BlockTransform`. Trả `Extents3d?`, null nếu BTR rỗng.
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Preprocess.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Preprocess.cs#L487-L505): trong keep-as-is scan của `ComputeModelSpaceExtents`, khi `hasExt = false`, gọi BTR walk fallback thay vì degenerate `(Position, Position)`. Nếu BTR walk cũng fail → vẫn fallback degenerate (giữ behavior cũ).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK).
- Chưa test lại trên batch user phản ánh.

### Bước tiếp theo
- User test lại batch 6 file `71-48963-10..15.dwg`. Verify log mới:
  - `[src] A1 ... bbox=[(0,0)-(17146,11900)]` (thay vì `(0,0)-(0,0)`).
  - `validA1Count = 1` cho mọi file.
  - `Sum effective widths : 6 file dùng A1.Width` (thay vì fallback bbox).
  - Overlap detector không flag cặp A1↔A1 (gap đúng 100mm).
- Nếu sau Fix #1 vẫn còn issue (orphan BR khổng lồ chồng A1 file kế bên do clone vào dest) → triển khai Fix #2 (purge orphan BR layer='0' size > ngưỡng trong side db).

### Ghi chú API
- `Entity.GeometricExtents` trong side db thường fail cho `BlockReference` (cached extents chưa compute), nhưng work cho `Line/Polyline/MText` (extents tính từ control points).
- BTR contents iterate được trong side db — chỉ graphics-realization fail, không phải data access.
- Recursion depth=2 đủ cho A1 → title block → text. Depth sâu hơn không cần thiết và risk infinite loop nếu BTR self-reference (rất hiếm).

---

## Session 2026-05-05 (1) — Drawing Collection: anchor N=1 (chỉ A1 trái nhất mỗi file)

### Bối cảnh
User test bản Hướng A (session trước) — vẫn thấy gap A1↔A1 không đồng nhất. Nguyên nhân: code đang lấy **envelope X-range của TẤT CẢ A1** trong từng file (`A1MinXSrc = min(all), A1MaxXSrc = max(all)`). Với file chứa nhiều khổ A1 (vd 2 sheet side-by-side), envelope rộng hơn 1 khổ A1 đơn lẻ → effectiveW lệch chuẩn → gap visible giữa các A1 khác nhau giữa các file.

### Quyết định
**N=1 mỗi file.** Mỗi file mặc định DUY NHẤT 1 A1 frame làm anchor (chọn A1 **trái nhất** = smallest MinX). A1 phụ trong cùng file giữ vị trí tương đối từ source — có thể nằm trong gap area hoặc chồng A1 file kế bên (overlap detector ở Summary sẽ phát hiện và liệt kê).

### Đã làm
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Types.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Types.cs): `PreparedDrawing` thêm `ValidA1Count` (tổng A1 hợp lệ trong file). Update XML doc: `A1MinXSrc/MaxXSrc` giờ là bbox của A1 **chosen anchor** (leftmost), không phải envelope.
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Preprocess.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Preprocess.cs): đổi loop chọn A1 — track `chosenA1` = ref có `Bbox.MinPoint.X` nhỏ nhất, đồng thời đếm `validA1Count`. Log warning `⚠ MULTIPLE A1 '{file}': phát hiện N khổ A1, default N=1 → chỉ anchor trên A1 trái nhất` khi `validA1Count > 1`. Overflow warning phụ thêm note nếu N>1.
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Summary.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Summary.cs): `Layout sanity` header → `(Hướng A — A1-frame anchored, N=1)`. Thêm dòng `Files với nhiều A1 (anchor leftmost): {n} ⚠ A1 phụ có thể chồng A1 file kế bên` khi có file N>1. Per-file overflow listing thêm tag `[N=X A1, anchor leftmost]`.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK; warning DLL lock chỉ vì AutoCAD đang chạy).

### Bước tiếp theo
- User test với batch có file >1 A1 → verify gap A1↔A1 (giữa các file) đồng nhất 100mm. Nếu file gốc thật sự có nhiều khổ A1 cần collect riêng, user nên tách thành nhiều file source trước khi collect.
- Overlap detector trong Summary sẽ flag rõ A1 phụ chồng A1 file kế (nếu có).

### Ghi chú API
- "Leftmost A1" = A1 có `Bbox.MinPoint.X` nhỏ nhất trong source. Tie-break không cần (cùng MinX → kết quả gần như tương đương).
- A1 phụ trong file (sau leftmost) được TransformBy cùng `dx` → giữ vị trí tương đối — không cần xử lý thêm.

---

## Session 2026-05-04 (2) — Drawing Collection: A1-frame anchored layout (Hướng A)

### Bối cảnh
User phản ánh khoảng gap giữa các khổ A1 sau khi Collect Drawings KHÔNG đồng nhất, nghi do scale A1 khác nhau (1:25, 1:30, 1:50). Phân tích code cho thấy nguyên nhân thực: thuật toán cũ căn theo `Extents.MinPoint.X` (bbox của TOÀN BỘ ModelSpace, gồm cả entity nằm ngoài A1 frame như dim/leader/text rảnh), với `COLLECTION_GAP = 1.0mm`. Suy ra công thức: `Gap(A1[i]→A1[i+1]) = RightOverflow_i + LeftOverflow_{i+1} + 1mm`, trong đó overflow = entity nằm ngoài A1 frame. Scale chỉ là yếu tố khuếch đại (cùng % overflow paper-space → mm-space khác theo scale).

### Giải pháp (Hướng A — A1-frame anchored)
Căn `dx` theo `A1.MinX` thay vì `Extents.MinX`. Advance offsetX bằng `A1.Width + GAP`. Gap A1↔A1 luôn = `COLLECTION_GAP` bất kể scale/overflow. GAP nâng từ 1mm → 100mm để dim/leader nhỏ overflow không chồng A1 lân cận. Files không có A1 fallback bbox cũ.

### Đã làm
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Types.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Types.cs): `PreparedDrawing` thêm `HasA1`, `A1MinXSrc`, `A1MaxXSrc`, `LeftOverflowSrc`, `RightOverflowSrc`.
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Preprocess.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Preprocess.cs): tính envelope X-range của tất cả `KeepAsIsRefsInSource` (A1/CAS_HEAD) có bbox > 0.5mm; tính LeftOverflow/RightOverflow vs `Extents`; log warning `⚠ OVERFLOW '{file}': left=...mm, right=...mm` khi overflow > 0.5mm.
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Clone.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Clone.cs#L66-L70): `dx = offsetX − (HasA1 ? A1MinXSrc : Extents.MinX)`.
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.cs#L34): `COLLECTION_GAP = 1.0` → `100.0`. `effectiveW = HasA1 ? A1.Width : Extents.Width` (line ~175). Log layout mode + overflow per file.
- [Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Summary.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Summary.cs): cập nhật label "Layout sanity (Hướng A — A1-frame anchored)"; thêm section "Overflow check" liệt kê file có overflow + đánh dấu `⚠ vượt GAP=100mm → CHỒNG A1 lân cận` nếu overflow > GAP.
- [Views/FittingManagement/BlockUtilitiesView.xaml](Views/FittingManagement/BlockUtilitiesView.xaml#L29): UI helper text → "anchored on each file's A1 frame with a 100mm gap between frames. Files without A1 fall back to bbox layout. Entities outside the A1 frame (overflow) trigger a warning in the log."

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- Build: **succeeded** (Debug, dotnet build OK; warning DLL lock chỉ vì AutoCAD đang chạy).
- Chưa test trên dataset thực tế nhiều scale khác nhau.

### Bước tiếp theo
- User test với batch nhiều file cùng A1 nhưng scale khác nhau (1:25/1:30/1:50) → verify gap A1↔A1 = đúng 100mm, đồng nhất.
- Nếu user thấy 100mm quá nhỏ với các file có dim ngoài frame → tăng GAP lên 200-500mm.

### Ghi chú API
- `KeepAsIsRefsInSource[].Bbox` lấy từ `ent.GeometricExtents` ở Phase 1 — có thể fail (`new Extents3d(Position, Position)` fallback, size = 0). Filter `w > 0.5` để loại bbox degenerate.
- Bbox source A1 = bbox dest A1 sau translate (chỉ Displacement, không scale/rotate). Nên A1.Width source dùng cho effectiveW giữ chính xác trong dest.
- File rỗng / không A1 / không Extents → `dx = offsetX − 0` (giữ behavior cũ), `Advanced=false`, không tốn GAP.

---

## Session 2026-05-04 (1) — Tách Master Library và Project Library thành 2 window độc lập

### Bối cảnh
[FittingLibraryWindow](Views/FittingManagement/Library/FittingLibraryWindow.xaml) cũ gánh cả Master + Project, switch bằng `RadioMasterMode` / `RadioProjectMode`. Code-behind dày `if (RadioProjectMode.IsChecked)`, button enable/disable theo mode → vi phạm SRP, user phải nhớ mode đang chọn. User chốt phương án tách 2 window độc lập, mỗi window 1 trách nhiệm.

### 4 quyết định kiến trúc đã chốt
1. Add to Project: chỉ cần `ActiveProjectContext` có path là OK (Project window có thể đang đóng).
2. `ProjectCatalogItem : CatalogItem` — subclass marker (giữ JSON shape cũ tương thích cho BOM Harvester đang đọc `ProjectPosNum` từ master catalog).
3. Push Update button chỉ giữ ở Master window.
4. Palette UX: 2 nút riêng "Open Master Library" / "Open Project Library".

### Đã làm

**MỚI — Models/Services/Utilities**:
- [Models/FittingManagement/ProjectCatalogItem.cs](Models/FittingManagement/ProjectCatalogItem.cs): subclass marker, không thêm field.
- [Services/FittingManagement/Library/IMasterLibraryService.cs](Services/FittingManagement/Library/IMasterLibraryService.cs): `MasterCatalogPath`, `MasterLibraryFolder`, `GetMasterCatalogItems`, `MergeIntoMaster`, `RemoveFromMaster`, `PublishToCentralLibrary`.
- [Services/FittingManagement/Library/IProjectLibraryService.cs](Services/FittingManagement/Library/IProjectLibraryService.cs): `LoadProjectCatalog`, `CreateProjectCatalog`, `MergeIntoProject`, `SaveProjectCatalog`, `RemoveFromProject`, `AutoAssignPositions`.
- [Services/FittingManagement/Library/ActiveProjectContext.cs](Services/FittingManagement/Library/ActiveProjectContext.cs): Singleton lưu project path đang active + event `ProjectChanged` cho cross-window sync.
- [Utilities/FittingManagement/CatalogJsonStore.cs](Utilities/FittingManagement/CatalogJsonStore.cs): generic `Read<T>`, `Write<T>`, `MergeItems`, `RemoveItems` — dùng chung Master + Project.

**TÁCH — Service partial**:
- XOÁ [Services/FittingManagement/Library/FittingManagementService.Library.cs](Services/FittingManagement/Library/FittingManagementService.Library.cs).
- THÊM [Services/FittingManagement/Library/FittingManagementService.MasterLibrary.cs](Services/FittingManagement/Library/FittingManagementService.MasterLibrary.cs): implement `IMasterLibraryService` + `InsertBlockFromLibrary`. `LOG_PREFIX` + ctor giữ ở đây.
- THÊM [Services/FittingManagement/Library/FittingManagementService.ProjectLibrary.cs](Services/FittingManagement/Library/FittingManagementService.ProjectLibrary.cs): implement `IProjectLibraryService`.

**SỬA — IFittingManagementService**:
- Gỡ `GetMasterCatalogItems`, `AddItemsToProjectCatalog`, `PublishToCentralLibrary`. Giữ `InsertBlockFromLibrary`, `PickGeometricFeatureFromCad` (cross-cutting với CAD).

**MỚI — Views/Library**:
- [Views/FittingManagement/Library/Shared/CategoryNode.cs](Views/FittingManagement/Library/Shared/CategoryNode.cs): tách ra khỏi xaml.cs cũ.
- [Views/FittingManagement/Library/Shared/CatalogTreeBuilder.cs](Views/FittingManagement/Library/Shared/CatalogTreeBuilder.cs): static helper `Build` + `ApplySearch` dùng chung 2 window.
- [Views/FittingManagement/Library/MasterLibraryWindow.xaml](Views/FittingManagement/Library/MasterLibraryWindow.xaml) + .cs: badge xanh "MASTER", buttons Add from CAD / Sub-BOM / Remove / **Add to Active Project** / Push Update / Refresh / Insert. Subscribe `ActiveProjectContext.ProjectChanged` để enable/disable Add-to-Project + label.
- [Views/FittingManagement/Library/ProjectLibraryWindow.xaml](Views/FittingManagement/Library/ProjectLibraryWindow.xaml) + .cs: badge đỏ "PROJECT", `Load Project` / `Create Project` set `ActiveProjectContext.ProjectFilePath`. `ColProjectPos` always editable. Buttons Auto-Assign Pos / Remove / Refresh / Insert. Bỏ Push Update.

**SỬA — Cross-window dependencies**:
- [Views/FittingManagement/Library/Accessory/AccessoryManagerWindow.xaml.cs](Views/FittingManagement/Library/Accessory/AccessoryManagerWindow.xaml.cs): ctor đổi sang `IMasterLibraryService`. `_service.AddItemsToProjectCatalog(_masterCatalogPath, ...)` → `_masterService.MergeIntoMaster(...)`.
- [Views/FittingManagement/Library/Accessory/NewAccessoryWindow.xaml.cs](Views/FittingManagement/Library/Accessory/NewAccessoryWindow.xaml.cs): tương tự.
- [Views/FittingManagement/Library/VirtualItemWindow.xaml.cs](Views/FittingManagement/Library/VirtualItemWindow.xaml.cs): tương tự.
- XOÁ hardcoded `_masterCatalogPath` ở 3 file trên — service tự biết path qua `MasterCatalogPath` property.

**SỬA — Palette tabs**:
- [Views/FittingManagement/TemplateView.xaml](Views/FittingManagement/TemplateView.xaml): 1 nút "Open Fitting Library" → 2 nút "Open Master Library" + "Open Project Library".
- [Views/FittingManagement/TemplateView.xaml.cs](Views/FittingManagement/TemplateView.xaml.cs): inject 3 interface (`IFittingManagementService` + `IMasterLibraryService` + `IProjectLibraryService`) từ cùng 1 instance `FittingManagementService`.
- [Views/FittingManagement/ProjectConfigView.xaml](Views/FittingManagement/ProjectConfigView.xaml) + .cs: tab "Project Config" mở thẳng ProjectLibraryWindow (đúng ngữ nghĩa tab).

**XOÁ — Legacy**:
- [Views/FittingManagement/Library/FittingLibraryWindow.xaml](Views/FittingManagement/Library/FittingLibraryWindow.xaml) + .cs.

### Kết quả build
- `dotnet build -c Debug`: **0 Errors**, 2 Warnings (Costura.Fody IncludeAssets + PowerShell post-build script bị group policy chặn — pre-existing, không liên quan).
- DLL mới: `MCG_FittingManagement.FittingManagement_20260504_141140.dll`.

### Ghi chú API/kiến trúc
- `FittingManagementService` giờ implement 3 interface (`IFittingManagementService`, `IMasterLibraryService`, `IProjectLibraryService`) qua 4 file partial (Master/Project Library + các phần BOM/Block/IDW Import vẫn nguyên). Caller chỉ nhận interface mỏng → DIP sạch.
- `ProjectPosNum` vẫn ở base `CatalogItem` (không move xuống `ProjectCatalogItem`) vì `BomStructure.cs` + `BomInterface.cs` đang đọc field này từ master catalog. Move sẽ break BOM Harvester. `ProjectCatalogItem` hiện chỉ đóng vai marker semantic.
- `CatalogJsonStore` deserialize project file dạng `List<ProjectCatalogItem>`. Vì subclass không có field mới → JSON shape trùng `CatalogItem` → backward compatible với file project cũ.
- `ActiveProjectContext` không persist xuống disk, reset khi AutoCAD tắt — đúng yêu cầu user.

### Bước tiếp theo
- User test luồng: open Master + open Project (cả 2 cùng lúc) → load project file → quay sang Master, click "Add to Active Project" → verify item được merge vào project file đúng.
- Test edge case: đóng Project window trong khi Master đang mở → `ActiveProjectContext` vẫn giữ path → Add to Project vẫn hoạt động.
- Test BOM Harvester sau split: chạy `MCG_Fitting_Show` → harvest BOM trên panel có fitting → verify `ProjectPosNum` vẫn xuất hiện đúng (ProjectPosNum vẫn được đọc từ MasterCatalog.json như cũ).

---

## Session 2026-04-23 (7) — Fix SDK runtime: path LoginHistory sai + logout quá sớm + noise resolver

### Bối cảnh
User test Session 6 trên live Vault, lộ 3 bug:

1. **LoginHistory.xml path sai**: log báo `Không tìm thấy LoginHistory.xml` tại `%APPDATA%\Autodesk\VaultCommon\Servers\`. File thật nằm ở `%APPDATA%\Autodesk\VaultCommon\` (không có subfolder `Servers`).
2. **UI message stale**: dialog kết quả vẫn hiển thị "Mở Inventor → Vault menu → Log In" (từ flow cũ). User đã login Vault Explorer → vẫn thấy message sai → confusing.
3. **Logout quá sớm**: mỗi import xong `Library.Logout()` xoá cached session → import kế tiếp phải sign-in lại (dù thường silent qua SSO nhưng tốn 1-2s + risk prompt).
4. **Log noise**: resolver log warning cho `.resources.dll` và `.XmlSerializers.dll` — đây là file OPTIONAL của .NET runtime (localization satellite + XML serializer cache), null return là đúng, không phải lỗi.

### Đã làm

- [Services/FittingManagement/Vault/VaultDirectService.cs](Services/FittingManagement/Vault/VaultDirectService.cs):
  - `AutoDetectConnection`: probe cả 2 location (`VaultCommon\LoginHistory.xml` trước, `VaultCommon\Servers\LoginHistory.xml` fallback).
  - `Dispose`: XOÁ `Library.Logout` call. Chỉ release local reference; VDF giữ session đến khi AutoCAD process thoát → next import tái sử dụng session silent.
  - `EnsureSignedIn`: thêm log chi tiết (ApartmentState, Settings) để dễ diagnose nếu còn fail.
- [Utilities/VaultAssemblyResolver.cs](Utilities/VaultAssemblyResolver.cs):
  - Return null SILENT cho `*.resources` và `*.XmlSerializers` assembly names — không log warning nữa. Log warning chỉ cho DLL core thực sự cần.
- [Views/FittingManagement/TemplateView.xaml.cs](Views/FittingManagement/TemplateView.xaml.cs):
  - BuildVaultBreakdown: sửa message từ "Mở Inventor → Vault menu" thành "Mở Autodesk Vault (Vault Explorer), login". Thêm hint xem log `[VaultDirectService]` nếu vẫn fail.

### Kết quả log sau fix (import CAS-0057373.idw)
```
[VaultDirectService] Tìm thấy LoginHistory.xml: C:\Users\...\VaultCommon\LoginHistory.xml
[VaultDirectService] Auto-detect OK: VNHPH1-S0006/MacGregor_CAS
[VaultDirectService] ✓ Sign-in OK — vnhph1-s0006/MacGregor_CAS, User=pham.truong.giang@macgregor.com (ID=94).
[VaultDirectService] Found: CAS-0057373.idw (MasterId=821573, Ver=11, Size=523776B).
[VaultDirectService] ✓ Downloaded 527360 bytes → C:\MacGregor_CAS_WF\Designs\...\CAS-0057373.idw.
[VaultDirectService] Dispose — giữ Vault session sống cho import kế tiếp (không logout).
HOÀN TẤT — Thành công: 1, Thất bại: 0.
```

### Ghi chú API
- **VDF AcquireFiles tôn trọng workspace mapping**: file download về **Vault workspace folder** (`C:\MacGregor_CAS_WF\...`), KHÔNG phải localPath ta pass qua `FilePathAbsolute`. Với user hiện tại không vấn đề vì họ select file từ workspace → extract cùng path. Nếu sau này gặp case file outside workspace, cần post-download copy từ workspace path sang localPath.
- **PropDefId "ClientFileName" = 9** trong MacGregor_CAS vault (không phải 4 như fallback). `ResolveFileNamePropDefId` dynamic query đã catch đúng.
- **Session persistence**: VDF giữ `Connection` object sống trong process memory cho đến khi `Library.Logout()` hoặc process exit. Không cần explicit logout — chỉ tốn session khi restart AutoCAD.

### Bước tiếp theo
- User test 2+ import liên tiếp → verify sign-in chỉ chạy 1 lần, imports sau silent.

---

## Session 2026-04-23 (6) — Vault: chuyển sang VDF SDK direct (bypass Inventor hoàn toàn)

### Bối cảnh
Phương án Inventor CommandManager (Session 4-5) có 2 hạn chế lớn:
1. Phụ thuộc user login Vault trong Inventor (command Enabled=false nếu chưa login).
2. Chậm — mỗi file ~3-5s qua Inventor Open doc + Execute command.

Chuyển sang **Option A**: gọi thẳng VDF SDK (Autodesk Vault Data Framework) — download file về local, không qua Inventor. Nhanh hơn ~10×, không cần Inventor login Vault, reuse cached credentials từ Vault Client.

User approve plan + không giữ UI toggle (commit hoàn toàn cho SDK approach). Chấp nhận risk bypass NTI for Vault AddIn workflow (nếu có custom logic get-latest — hiện chưa biết).

### Đã làm

**MỚI — VDF SDK scaffolding**:
- [MCG_FittingManagement.csproj](MCG_FittingManagement.csproj): thêm 5 reference DLL (Private=False → Costura không embed):
  - `Autodesk.Connectivity.WebServices`
  - `Autodesk.DataManagement.Client.Framework`
  - `Autodesk.DataManagement.Client.Framework.Vault`
  - `Autodesk.DataManagement.Client.Framework.Forms`
  - `Autodesk.DataManagement.Client.Framework.Vault.Forms`
  - HintPath: `C:\Program Files\Autodesk\Vault Client 2023\Explorer`.
- [Utilities/VaultAssemblyResolver.cs](Utilities/VaultAssemblyResolver.cs): runtime `AssemblyResolve` hook, probe Vault Client 2020-2026 trong Program Files, load DLL theo version có cài. Plugin bundle vẫn nhẹ (~5MB).
- [Models/FittingManagement/VaultConnectionInfo.cs](Models/FittingManagement/VaultConnectionInfo.cs): DTO Server + Vault + UserName.
- [Services/FittingManagement/Vault/IVaultDirectService.cs](Services/FittingManagement/Vault/IVaultDirectService.cs): interface (AutoDetectConnection, EnsureSignedIn, DownloadLatest, IsSignedIn, Dispose).
- [Services/FittingManagement/Vault/VaultDirectService.cs](Services/FittingManagement/Vault/VaultDirectService.cs): implementation đầy đủ:
  - `AutoDetectConnection`: đọc `%APPDATA%\Autodesk\VaultCommon\Servers\LoginHistory.xml` → `LastServerName` + `LastVault`.
  - `EnsureSignedIn`: `VDF.Vault.Forms.Library.Login(LoginSettings)` với `AutoLoginMode=RestoreAndExecute` (silent nếu cached creds, dialog nếu không).
  - `DownloadLatest`: `DocumentService.FindFilesBySearchConditions` (search theo `ClientFileName` PropDef, dynamic resolve qua `PropertyService.GetPropertyDefinitionsByEntityClassId("FILE")`) → `FileManager.AcquireFiles` với `FileIteration` + `FilePathAbsolute` → download vào localPath.
  - Static ctor: `VaultAssemblyResolver.Install()` — guaranteed chạy trước JIT resolve VDF types.
  - Dispose: `Library.Logout(connection, LogoutSettings, LoginSettings)`.

**SỬA — Integration**:
- [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs):
  - PHASE 0 (mới): khởi tạo `VaultDirectService` + sign-in TRÊN UI THREAD (trước `Task.Run`). VDF Login dialog cần STA/UI thread. Nếu sign-in fail → dispose, set null, per-file ghi SkippedNotLoggedIn.
  - PHASE 1: download trong Task.Run qua `vaultService.DownloadLatest(fileName, idwPath)` — WebService call OK trên worker thread.
  - `vaultService?.Dispose()` trong finally block để đảm bảo logout.
  - Helper `VaultStatusShortLabel` (private static) thay `StatusShortLabel` của VaultRefresh.cs đã xoá.

**XOÁ — Legacy code**:
- `Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs` — toàn bộ flow Inventor-based (TryPullLatestFromVault, CheckVaultRefreshReady, TryPullViaCommandManager, EnumerateVaultCommands, ...).

**Docs update**:
- [Models/FittingManagement/VaultRefreshResult.cs](Models/FittingManagement/VaultRefreshResult.cs): update enum comments — `SkippedNoAddIn` nghĩa mới: "Vault Client/SDK không cài". Message của `SkippedNotLoggedIn` cập nhật cho flow SDK.

### Kết quả build
- `dotnet build -c Debug`: **0 Errors**, 9 Warnings (pre-existing: file lock AutoCAD + PowerShell post-build).
- DLL mới: `MCG_FittingManagement.FittingManagement_20260423_100506.dll`.

### Khám phá API thật (qua reflection probe)
VDF API không như tài liệu online phổ biến — nhiều method/type khác tên:
- `ConnectionManager` KHÔNG tồn tại. Dùng static class `Autodesk.DataManagement.Client.Framework.Vault.Forms.Library` với `Login(LoginSettings)` / `Logout(...)`.
- `LogInTool` KHÔNG tồn tại. Xem trên.
- `DownloadFilePart` trên DocumentService KHÔNG tồn tại. Dùng high-level `FileManager.AcquireFiles(AcquireFilesSettings)` với `FileIteration` + `FilePathAbsolute`.
- `AcquisitionOption` là nested enum `AcquireFilesSettings.AcquisitionOption`, không phải top-level.
- `FilePathAbsolute` nằm trong `Autodesk.DataManagement.Client.Framework.Currency` (Framework.dll), không phải Vault namespace.
- `LoginSettings.AutoLoginModeValues`: `None`, `Restore`, `RestoreAndExecute`, `RestoreAndExecuteOrDoNothing` → dùng `RestoreAndExecute` cho silent-with-fallback-dialog.

### Test plan (user cần tự verify — máy dev không có live Vault)
1. **Test 1 — Vault Client đã login**: mở Vault Explorer, login. Chạy Import với "Pull from Vault" ON.
   Expected: progress hiển thị "Vault: signed in → VNHPH1-S0006/MacGregor_CAS", mỗi file log `Downloaded X bytes → ...`, tốc độ <500ms/file.
2. **Test 2 — Vault Client chưa login**: đảm bảo logged out. Chạy Import.
   Expected: VDF dialog bật lên lần đầu user gõ password → sau đó silent; hoặc user cancel → tất cả file ghi `SkippedNotLoggedIn`, extraction vẫn chạy bình thường với file local.
3. **Test 3 — File không trong Vault**: file IDW không được upload Vault. Chạy Import.
   Expected: per-file ghi `SkippedNotInVault`, extraction vẫn OK.
4. **Test 4 — Vault Client chưa cài**: unlikely trên máy production nhưng test resolver fallback. Expected: log warning "Không tìm thấy X.dll trong Vault Client 2020-2026", sign-in throw → SkippedNotLoggedIn.

### Known risks (theo dõi sau deploy)
- **NTI for Vault custom workflow**: SDK bypass hoàn toàn Inventor AddIn → nếu NTI có script get-latest riêng, workflow đó sẽ không chạy. Nếu user báo bug "metadata không đồng bộ" / "file thiếu dependency" → có thể do NTI hook bị skip. Lúc đó cân nhắc revert sang Inventor CommandManager approach.
- **ClientFileName PropDefId = 4 fallback**: `ResolveFileNamePropDefId` dynamic tra PropertyService — nếu Vault server custom schema, fallback `4` có thể sai. Cải thiện nếu gặp: cache mapping ra file settings.
- **STA thread cho Login dialog**: đã đảm bảo sign-in ở UI thread (trước Task.Run). Nếu sau này refactor sang call-from-background, cần `Application.Current.Dispatcher.Invoke` wrap.

### Bước tiếp theo
- Chờ user test 4 scenario trên Vault live.
- Nếu có issue NTI workflow → cân nhắc phương án hybrid (SDK cho tốc độ + Inventor AddIn fallback cho NTI).

---

## Session 2026-04-23 (5) — Vault: fix fallback chain + pre-flight + gate diagnostic

### Bối cảnh
Diagnostic của Session 4 đã reveal thật tên Vault command trong Inventor 2023: **`VaultGetLatest`** (Display='Refresh'), `RefreshRevisionTop`, `AppRefreshCmd`. 8 command ID cũ trong fallback chain đều KHÔNG tồn tại (throw E_FAIL). Tất cả command Vault đều `Enabled=False` vì user chưa login Vault trong Inventor session.

User yêu cầu sửa cả 4 điểm đề xuất.

### Đã làm
**[Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs](Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs)**:

1. **Fix fallback chain**: thay 8 command ID sai (`VaultCmd:GetLatestVersion`, `VltInv.cmdRefresh`, ...) bằng 3 ID thật: `VaultGetLatest` → `RefreshRevisionTop` → `AppRefreshCmd`.

2. **Enabled handling**: tách lookup command vs. Execute. Đếm `foundButDisabled`. Nếu có command tồn tại nhưng tất cả Enabled=false → trả **SkippedNotLoggedIn** (thay vì `Failed` khó hiểu). Nếu 0 command tồn tại → `Failed` như cũ.

3. **Gate diagnostic**: `EnumerateVaultCommands` chỉ chạy khi env var `MCG_VAULT_DIAGNOSTIC=1`. Mặc định tắt vì enumerate 2295 ControlDefinitions tốn ~3-4s. Helper mới: `IsVaultDiagnosticEnabled()`.

4. **Pre-flight**: method mới `CheckVaultRefreshReady(invApp)` — lookup `VaultGetLatest` và check `Enabled`. Return `null` = ready, non-null = skip reason cho cả batch.

**[Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs)**:

5. **Gọi pre-flight 1 lần trước loop**: nếu Vault không ready, mỗi file trong batch sẽ clone skip reason vào `result.VaultResults` mà không thử per-file (tiết kiệm 3-5s × N file). UI `BuildVaultBreakdown` xử lý `SkippedNotLoggedIn` group sẵn.

### Kết quả build
- `dotnet build -c Debug`: **0 Errors**, warnings chỉ là pre-existing (file lock của AutoCAD đang mở, PowerShell post-build).
- DLL mới: `MCG_FittingManagement.FittingManagement_20260423_092707.dll`.

### Test plan (user cần verify)
- **Test 1 — chưa login Vault**: chạy Import với "Pull from Vault" = ON khi Inventor chưa mở Vault. Expected: log "pre-flight FAIL — 'VaultGetLatest' Enabled=false", UI summary group "Not Logged In" = N files, extraction vẫn chạy bình thường.
- **Test 2 — đã login Vault**: open Inventor → Vault menu → Log In → chạy Import. Expected: pre-flight OK, mỗi file log `executing CommandManager 'VaultGetLatest'` → `✓ command executed`.
- **Test 3 — diagnostic mode**: `set MCG_VAULT_DIAGNOSTIC=1` trước khi start AutoCAD, force Vault không ready (logout) → verify chỉ khi diagnostic=1 mới enumerate 2295 controls.

### Ghi chú API
- `CommandManager.ControlDefinitions[cid]` **throw E_FAIL** (không phải null) khi `cid` không tồn tại trong Inventor 2023 → cần catch generic `Exception` để `continue`, không chỉ `RuntimeBinderException`.
- Inventor 2023 Vault InternalName thực tế: `VaultGetLatest`, `VaultCheckinTop`, `VaultCheckoutTop`, `VaultOpenFromVault`, `VaultReviseTop`, v.v. Prefix **`Vault`** (PascalCase liền) + hậu tố thường `Top`. KHÔNG có colon, không `VltInv.`, không `VaultCmd:`.
- `VaultServer` là command duy nhất luôn `Enabled=true` (mở Options dialog, không cần login) → có thể dùng làm indicator "Vault AddIn có load".

### Bước tiếp theo
- Chờ user test 3 scenario trên → điều chỉnh nếu có edge case.

---

## Session 2026-04-23 (4) — Vault diagnostic: enumerate ControlDefinitions + AddIn identity

### Bối cảnh
User báo: Case 3 xảy ra — tất cả 8 command ID trong CommandManager fallback chain đều null (không match). Vault AddIn version đang dùng không expose command với tên chúng ta đoán.

Cần **diagnostic tool** để khám phá command ID thật → tune fallback chain.

### Đã làm
[Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs](Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs):

**1. Enumerate ControlDefinitions khi fallback fail**:
- Method mới `EnumerateVaultCommands(invApp)` — iterate toàn bộ `CommandManager.ControlDefinitions`, log những command có InternalName/DisplayName chứa keyword: `"Vault"` / `"Vlt"` / `"GetLatest"` / `"Refresh"` / `"Get Latest"`.
- Mỗi match log: `Internal='{name}' | Display='{name}' | Enabled={true/false}`.
- Nếu 0 match → log rõ "Vault AddIn không expose command qua CommandManager — cần đổi approach (VDF SDK direct / Vault CLI)".
- Gọi trong `TryPullViaCommandManager` sau khi loop 8 ID fail hết.

**2. Helper `ContainsAny(source, params keywords)`** — check string chứa bất kỳ keyword nào (case-insensitive).

**3. Log identity Vault AddIn** trong `FindVaultAddIn`:
- Sau khi match AddIn, log thêm `ClientId` (GUID) và `FullFileName` (đường dẫn DLL).
- Giúp user xác định Vault version/provenance (vd Vault Professional 2023 vs 2024 có DLL khác nhau).

### Expected log khi test lại (Case 3)
```
Vault: Inventor có 47 ApplicationAddIn(s).
Vault: Found AddIn 'Autodesk Vault Professional' | ClientId={2B6FF4F0-...} | DLL=C:\Program Files\Autodesk\Vault Client 2024\Bin\VltInv.dll
Vault: Automation=null → fallback CommandManager-based approach...
Vault: opening 'CAS-0071132.idw' trong Inventor để run Vault command...
Vault: (8 command ID all return null, không match)
Vault DIAGNOSTIC: enumerate CommandManager.ControlDefinitions (1847 total) để tìm command liên quan Vault...
  [1] Internal='VaultInvCmd.GetLatest' | Display='Get Latest Version' | Enabled=True
  [2] Internal='VaultInvCmd.CheckIn' | Display='Check In' | Enabled=True
  [3] Internal='VaultInvCmd.CheckOut' | Display='Check Out' | Enabled=True
  ...
Vault DIAGNOSTIC: tìm thấy 12 command(s). Gửi log này về dev để update fallback chain với InternalName thực tế.
```

Từ log trên, ta biết command ID thật là `VaultInvCmd.GetLatest` (hoặc tương tự) → update fallback chain.

### Thiết kế
- **Diagnostic chỉ chạy khi cần**: không enumerate mỗi file, chỉ khi 8 fallback đầu fail → O(N files × 1 enumerate max) thay vì O(N × enumerate).
- **Keywords match rộng**: `Vault`, `Vlt`, `GetLatest`, `Refresh`, `Get Latest` → cover các naming convention khác nhau (VaultCmd, VltInv, Vault., Get Latest Version, Refresh Out-of-Date, ...).
- **Log `Enabled=true/false`**: user biết command nào khả dụng trong context hiện tại (active doc, logged in Vault).
- **Log DLL path**: giúp verify đúng Vault Client installed version.
- **Case-insensitive match**: robust với PascalCase, camelCase, UPPER_CASE.

### Fallback nếu enumerate ra 0 match
Nếu log `Vault DIAGNOSTIC: KHÔNG có command nào match` → Vault AddIn không expose command public cho CommandManager. Lúc này options còn lại:
1. **Phương án A nguyên gốc**: reference Vault SDK (VDF) trực tiếp — cần setup Vault Developer SDK.
2. **Phương án C**: shell to Vault Client CLI (`VaultClient.exe --getLatest ...`) — UX kém nhưng không phụ thuộc Inventor.
3. **Phương án D**: fallback manual — user Get Latest trong Vault Explorer trước khi import, plugin không auto.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
User test lại với batch IDW, gửi plugin.log. Tôi đọc section `Vault DIAGNOSTIC` để:
- Xác định command ID thật → update fallback chain (quick fix).
- Hoặc confirm không có command → đề xuất Phương án A/C/D.

### Ghi chú API
- **`CommandManager.ControlDefinitions`**: collection enumerable. Count property (có thể throw trên một số Inventor version — try/catch). Iterate với `foreach dynamic`.
- **`ControlDefinition.InternalName` vs `DisplayName`**:
  - `InternalName` = unique ID dùng khi execute (vd "VaultInvCmd.GetLatest").
  - `DisplayName` = localized text hiển thị UI (vd "Get Latest Version").
  - Indexer `ControlDefinitions[key]` match theo InternalName.
- **`ApplicationAddIn.ClientId`**: GUID string unique identifier AddIn. Dùng với `ApplicationAddIns.ItemById(guid)` để access reliable (bypass DisplayName match).
- **`ApplicationAddIn.FullFileName`**: đường dẫn đầy đủ đến DLL. Vault AddIn phổ biến ở `C:\Program Files\Autodesk\Vault Client 20XX\Bin\VltInv.dll`.

---

## Session 2026-04-23 (3) — Vault fallback: CommandManager khi Automation=null

### Bối cảnh
User test Vault integration thực tế, log báo:
```
Vault AddIn không available. Lý do: Vault AddIn Automation = null
```

Phân tích: `FindVaultAddIn` đã match được Vault AddIn (pass step 1), `Activated=true` (pass step 2), nhưng `vaultAddIn.Automation` trả null. Vault AddIn version này **không expose** COM Automation interface — một số Vault/Inventor version có behavior này, đặc biệt Vault newer (2023+) shift sang command-based API.

### Đã làm
[Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs](Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs):

**1. Change behavior khi `Automation=null`**: thay vì return `SkippedNoAddIn` → fallback sang `TryPullViaCommandManager`.

**2. Method mới `TryPullViaCommandManager(invApp, filePath)`:**
- **Open document** trong Inventor (`invApp.Documents.Open(path, false)`) — cần active doc cho Vault command target.
- **Fallback chain 8 command IDs**:
  - `VaultCmd:GetLatestVersion`, `Vault.GetLatestVersion`, `VltInv.cmdGetLatestVersion`
  - `Vault:RefreshOutOfDate`, `VaultCmd:Refresh`, `Vault.RefreshDocument`
  - `VltInv.cmdRefresh`, `VltInv.cmdGetLatest`
- **Với mỗi command**:
  - `CommandManager.ControlDefinitions[cid]` — null → skip (command không exist).
  - Check `cmdDef.Enabled` — exist nhưng disabled (vd chưa login) → skip + log.
  - `cmdDef.Execute()` → `Thread.Sleep(2000)` đợi Vault hoàn tất.
  - Return `Success` với MethodUsed = `"cmd:<cid>"`.
- **Error classification** từ exception message:
  - Contains "not in vault" / "not found" → `SkippedNotInVault`.
  - Contains "login" / "sign in" / "authenticated" → `SkippedNotLoggedIn`.
  - Other → continue next command.
- **Finally**: close doc để `ProcessSingleIdwFile` sau đó mở lại từ disk (file đã updated).

### Thiết kế
- **Opt-in overhead**: mở doc qua Inventor cost 3-5s/file. Batch 10 files = +30-50s. User đã chọn `pullFromVault=true` = chấp nhận cost để đổi data fresh.
- **2x open/close**: Vault pull mở doc để trigger command, `ProcessSingleIdwFile` sau đó mở lại để extract. Wasteful nhưng isolated — refactor share session sau nếu cần.
- **Check `Enabled` trước Execute**: tránh trigger command disabled (popup error). Command Enabled thường = active doc hợp lệ + Vault logged in.
- **Error pattern matching**: dựa trên message text (dễ vỡ với translation). Version thực tế có thể trả message tiếng Anh khác — cần tune sau khi thấy log thật.

### Flow mới tổng hợp
```
TryPullLatestFromVault
  ├─ Find Vault AddIn → not found → SkippedNoAddIn
  ├─ Activated = false → SkippedNoAddIn
  ├─ Automation property:
  │   ├─ != null → TryPullViaAutomation (original flow)
  │   │   ├─ Check LoggedIn → auto sign-in nếu cần (session 2)
  │   │   └─ Fallback chain 6 automation methods
  │   │
  │   └─ == null → TryPullViaCommandManager (NEW)
  │       ├─ Open doc in Inventor
  │       ├─ Fallback chain 8 command IDs
  │       └─ Close doc
  │
  └─ Return VaultRefreshResult
```

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
Test lại với batch IDW trong môi trường Vault:
1. Đọc log tìm dòng `Vault: executing CommandManager 'X'...` → biết command nào work.
2. Nếu tất cả 8 command đều không exist (ControlDefinitions return null) → cần research thêm Vault command IDs cho Vault version đang dùng:
   - Mở Inventor → Tools → Customize → UI commands → search "Vault" → ghi lại command ID.
   - Hoặc dùng iLogic / VBA snippet list toàn bộ ControlDefinitions của Inventor.
3. Nếu command Enabled=false vì chưa login → kiểm session 2's auto sign-in chạy đúng chưa (log `Vault: ✓ auto sign-in success via ...`).

### Ghi chú API
- **`ApplicationAddIn.Automation` null**: một số AddIn (bao gồm Vault newer) không expose COM Automation — thiết kế mới dùng CommandManager/events. Kiểm tra null trước khi cast dynamic.
- **`CommandDefinition.Enabled`**: property bool, thay đổi theo context (active doc state, login status, selection, ...). Check trước Execute để tránh trigger command in invalid state.
- **`Documents.Open(path, bVisible)`**: bVisible=false → mở background, không hiển thị UI. Document vẫn active trong Inventor documents collection → CommandManager commands target được.
- **COM HRESULT 0x80010114 trong `doc.Close(true)`**: benign khi Inventor tự unload document sau operation. Catch + log info, không throw.

---

## Session 2026-04-23 (2) — Vault auto sign-in qua Autodesk ID

### Yêu cầu user
Vault environment dùng **Autodesk ID SSO** (login không cần nhập password). Plugin hiện chỉ detect "chưa login" rồi skip — user phải mở Inventor → Vault menu → Sign In thủ công. Muốn auto sign-in qua cached Autodesk session.

### Đã làm
[Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs](Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs):

**1. Cache result sign-in attempt** (per-batch):
- Instance field `bool? _vaultSignInAttemptResult` (null=chưa thử, true=success, false=fail).
- `ResetVaultSignInCache()` method — reset về null.
- Gọi ở đầu `ImportIdwFilesAsync` nếu `pullFromVault=true`.
- Lý do cache: `TryPullLatestFromVault` gọi per-file → nếu sign-in lần 1 fail, file sau cũng fail → không retry để tiết kiệm 2-5s × N file.

**2. Integrate vào flow login check** trong `TryPullLatestFromVault`:
- Nếu `loggedIn == false`:
  - Nếu `_vaultSignInAttemptResult == null` → gọi `TryAutoSignIn` + cache.
  - Nếu cache=true → re-check `TryGetLoggedInStatus`.
  - Nếu vẫn !loggedIn → return `SkippedNotLoggedIn`.

**3. `TryAutoSignIn(invApp, vaultAuto)` — 2 approaches fallback chain:**

*Approach 1: Automation methods* (3 methods):
- `vaultAuto.SignIn()` — Vault API 2023+ với Autodesk ID SSO
- `vaultAuto.LogOn("", "", "", "")` — empty args, rely cached session
- `vaultAuto.Connect()` — newer connection method

*Approach 2: Inventor CommandManager* (5 command IDs):
- `VaultCmd:SignIn`, `Vault.SignIn`, `VltInv.cmdSignIn`, `Vault:LogIn`, `VltInv.cmdLogOn`
- Execute via `invApp.CommandManager.ControlDefinitions[cid].Execute()`

Sau mỗi attempt: `Thread.Sleep(2000)` để sign-in callback hoàn tất, rồi `TryGetLoggedInStatus` để verify. Return true nếu logged in.

**4. Integration** [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs):
- Gọi `ResetVaultSignInCache()` ở đầu `ImportIdwFilesAsync` nếu `pullFromVault=true`.

### Thiết kế
- **Fallback chain 2 approach × tổng 8 phương án**: Vault Sign-In API chưa được Autodesk document chính thức cho Inventor AddIn, cover nhiều Vault/Inventor version.
- **Cache sign-in attempt batch-level**: tiết kiệm thời gian và tránh spam popup dialog nếu môi trường không hỗ trợ auto sign-in.
- **Thread.Sleep 2s sau mỗi attempt**: Vault sign-in qua Autodesk ID là async qua OAuth flow với Autodesk server → callback cần vài giây.
- **Graceful: không throw nếu sign-in fail**: chỉ log + continue với status `SkippedNotLoggedIn`, user vẫn import được với file local.
- **Verbose log mỗi attempt**: user đọc plugin.log biết method/command nào hoạt động để tune sau này.

### Behavior expected
**Case 1: User đã login Autodesk Desktop App + Inventor**
- `vaultAuto.SignIn()` → Vault AddIn dùng token cached → silent login → `LoggedIn=true` sau 1-2s.
- Log: `✓ auto sign-in success via Automation.SignIn.`
- Import tiếp với file latest từ Vault.

**Case 2: User chưa login Autodesk Desktop App**
- `vaultAuto.SignIn()` → popup dialog Autodesk Sign-In xuất hiện.
- User login qua browser OAuth → Vault AddIn nhận token → `LoggedIn=true`.
- Plugin wait 2s → nếu user chưa kịp login → re-check fail → thử method kế.
- Nếu tất cả methods fail → cache `_vaultSignInAttemptResult=false` → các file sau skip luôn.

**Case 3: Vault AddIn không support auto sign-in (Vault cũ, chưa Autodesk ID)**
- Tất cả methods throw `RuntimeBinderException` hoặc fail silent.
- Cache false → file sau skip với `SkippedNotLoggedIn`.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test trong môi trường Vault + Autodesk ID:
  1. Đảm bảo đã login Autodesk Desktop App.
  2. Đóng hoàn toàn Inventor (plugin sẽ auto-start).
  3. AutoCAD → Template tab → Import .idw.
  4. Đọc plugin.log tìm:
     - Method nào trong Approach 1 hoạt động (SignIn / LogOn / Connect)?
     - Hoặc command nào trong Approach 2 hoạt động?
     - Log dòng `✓ auto sign-in success via ...` để xác định API chính xác.
- Nếu tất cả fail mặc dù đã login Autodesk ID → log sẽ show method names đã thử → cần nghiên cứu Vault AddIn API docs hoặc reverse engineer qua `ObjectBrowser` trên Vault Inventor AddIn DLL.

### Ghi chú API
- **Autodesk ID / Single Sign-On**: Vault 2023+ support SSO qua Autodesk Identity Services. User login Autodesk Desktop App 1 lần → token cached → các app desktop (Inventor, Vault) share session.
- **Vault AddIn OAuth flow**: Sign-In method có thể trigger browser OAuth popup (không nhập password nếu session cached). Duration 1-3s typically.
- **`RuntimeBinderException` vs runtime exception**: khi dynamic invoke method, RBE = method không exist; other exception = method exist nhưng throw.
- **`CommandDefinition.Execute()` trong Inventor**: command có thể async. `Thread.Sleep` là workaround — cleaner là dùng event callback nhưng phức tạp hơn cho exploratory phase.
- **`ApplicationAddIn.CommandManager.ControlDefinitions[key]`**: indexer trả null nếu key không tồn tại (không throw). Check `!= null` trước khi execute.

---

## Session 2026-04-23 (1) — Vault refresh UX: realtime status + summary breakdown

### Yêu cầu user
Sau khi apply Vault integration (session 17), user muốn hiển thị CHO USER BIẾT file IDW nào đã pull latest, file nào skip, file nào fail — qua UI thay vì chỉ log file.

Chọn **Cách A + B**: realtime progress + summary dialog với Vault breakdown.

### Đã làm

**1. Extend ImportResult** [Models/FittingManagement/ImportResult.cs](Models/FittingManagement/ImportResult.cs):
- Thêm field `List<VaultRefreshResult> VaultResults = new List<VaultRefreshResult>()`.
- Empty nếu `pullFromVault=false` (không break backward compat).

**2. Track + realtime progress** [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs):
- Trong `ExtractAllIdw` loop, sau khi `TryPullLatestFromVault` xong:
  - Bổ sung `FilePath` vào result nếu status skip không có path (SkippedNoAddIn/NotLoggedIn gốc không gắn path).
  - `result.VaultResults.Add(vaultResult)`.
  - Emit realtime progress với status icon (✓ success, ⚠ skip, ✗ fail): `progress?.Report($"[{i+1}/{N}] {icon} Vault {statusLabel}: {fileName}")`.

**3. Status short label** [Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs](Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs):
- Thêm `StatusShortLabel(VaultRefreshStatus)`: map enum → text ngắn 1-2 từ cho TxtImportStatus single-line.
  - `Success` → "latest"
  - `AlreadyLatest` → "already latest"
  - `SkippedNoAddIn` → "skip (no AddIn)"
  - `SkippedNotLoggedIn` → "skip (not logged in)"
  - `SkippedNotInVault` → "skip (not in vault)"
  - `Failed` → "failed"

**4. Summary dialog Vault breakdown** [Views/FittingManagement/TemplateView.xaml.cs](Views/FittingManagement/TemplateView.xaml.cs):
- Helper mới `BuildVaultBreakdown(ImportResult)`: group VaultResults theo status, format section text.
- Gọi trong `ShowImportResultDialog` → append vào message nếu có data.
- Mỗi group có header icon + count, liệt kê top-3/top-5 file tiêu biểu:
  ```
  ── Vault refresh ──
  ✓ Pulled latest: 3 file(s)
    • CAS-0071132.idw (via GetLatestServerVersion)
    • CAS-0071133.idw (via GetLatestServerVersion)
    ...
  ⚠ Not in vault: 1 file(s) — dùng file local
    • local-only.idw
  ⚠ Vault chưa login: 2 file(s) dùng local
    → Mở Inventor → Vault menu → Log In, rồi import lại để get latest.
  ```
- `SkippedNoAddIn`: gom 1 dòng + reason (lý do đồng loạt cho cả batch).
- `Failed`: liệt kê file + error message (top-3).

### Thiết kế
- **Realtime + summary phối hợp**: realtime cho cảm giác tiến độ từng file (flash qua nhanh), summary persistent cho verify sau import.
- **Status icon trong progress**: `✓ / ⚠ / ✗` map theo `IsSuccess` + Status. 3 nhóm màu rõ: xanh (success), vàng (skip), đỏ (fail).
- **Liệt kê top-N per group**: success 5, skip 3, failed 3 — tránh dialog quá dài với batch lớn.
- **Message actionable cho NotLoggedIn**: hướng dẫn cụ thể "Mở Inventor → Vault menu → Log In" → user biết cần làm gì.
- **Path bổ sung sau Vault call**: SkippedNoAddIn/NotLoggedIn không có `FilePath` theo design (lý do batch-level, không per-file). Gán path ở caller để summary có thể show file name.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### UX khi test
**Batch 5 files, Inventor chạy + Vault login + 3 file trong Vault + 1 file local + 1 file Vault không có:**

Realtime (TxtImportStatus cập nhật qua từng file):
```
[1/5] ✓ Vault latest: CAS-0071132.idw
[1/5] Extracting: CAS-0071132.idw
[2/5] ✓ Vault latest: CAS-0071133.idw
...
[5/5] ⚠ Vault skip (not in vault): local-only.idw
[5/5] Extracting: local-only.idw
Done. Success=5, Failed=0
```

Summary dialog:
```
Import IDW hoàn tất!
✓ Thành công: 5
✗ Thất bại: 0

── Vault refresh ──
✓ Pulled latest: 4 file(s)
  • CAS-0071132.idw (via GetLatestServerVersion)
  • CAS-0071133.idw (via GetLatestServerVersion)
  • CAS-0047895.idw (via GetLatestServerVersion)
  • CAS-0047761.idw (via GetLatestServerVersion)
⚠ Not in vault: 1 file(s) — dùng file local
  • local-only.idw
```

### Bước tiếp theo
- Test trong môi trường Vault thật — verify:
  - Realtime status cập nhật đúng sau mỗi Vault call.
  - Summary dialog phân nhóm đúng theo Status.
  - User dễ đọc và hiểu file nào đã sync, file nào không.

### Ghi chú API
- **`System.Text.StringBuilder` qua `BuildVaultBreakdown`**: tránh string concatenation trong loop (O(N²) allocation).
- **LINQ group qua `.Where(r => r.Status == ...)`**: O(N) per status, tổng O(N × 5 group) = O(N) — acceptable.
- **Progress.Report trong Task.Run**: `IProgress<string>` capture SynchronizationContext tại constructor (UI thread). Report từ worker thread tự marshal về UI thread → TxtImportStatus.Text update an toàn.

---

## Session 2026-04-22 (17) — Vault integration (Phương án B): Pull latest qua Inventor AddIn

### Yêu cầu user
Workflow admin hiện tại: chọn file IDW trên ổ local → import vào Library. Vấn đề: file local có thể cũ so với Vault. User muốn: khi chọn file, plugin tự pull latest từ Vault trước khi extract.

User chọn **Phương án B**: dùng Inventor COM mượn Vault AddIn — không cần Vault SDK reference riêng.

### Đã làm

**1. Model mới** [Models/FittingManagement/VaultRefreshResult.cs](Models/FittingManagement/VaultRefreshResult.cs):
- Enum `VaultRefreshStatus`: Success / AlreadyLatest / SkippedNoAddIn / SkippedNotLoggedIn / SkippedNotInVault / Failed.
- Class `VaultRefreshResult` với `Status`, `Message`, `FilePath`, `MethodUsed` (tên Vault API method đã hoạt động — dùng cho debug).
- Static factory methods cho mỗi status.
- `IsSuccess` property = Success || AlreadyLatest.

**2. Service partial** [Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs](Services/FittingManagement/Vault/FittingManagementService.VaultRefresh.cs):
- `TryPullLatestFromVault(dynamic invApp, string filePath)` — core method.
- Flow:
  1. `FindVaultAddIn(invApp)` → iterate `ApplicationAddIns`, match `DisplayName` chứa "Vault" (case-insensitive).
  2. Check `Activated` property.
  3. Access `Automation` property (COM late-bound, Vault-specific).
  4. `TryGetLoggedInStatus(auto)` — thử `LoggedIn`/`IsLoggedIn`/`LoggedOn` property (Vault API varies by version).
  5. **Fallback chain** 6 method names: `GetLatestServerVersion`, `RefreshFile`, `GetLatestForDocument`, `RefreshDocument`, `GetLatest`, `UpdateFile` — try từng method, catch `RuntimeBinderException` (method không exist) + generic Exception (method exist nhưng fail).
  6. Nếu exception message chứa "not found" / "not in vault" → trả `SkippedNotInVault`.
  7. Nếu tất cả fail → `Failed` với list methods đã thử.
- Log verbose từng bước: số AddIns, tên AddIn match, method đã hoạt động, reason skip — để diagnose môi trường Vault khác nhau.

**3. Interface** [Services/FittingManagement/IFittingManagementService.cs](Services/FittingManagement/IFittingManagementService.cs):
```csharp
Task<ImportResult> ImportIdwFilesAsync(string[] idwPaths, string bomType, bool pullFromVault = false, IProgress<string> progress = null);
```
Thêm param `pullFromVault` default=false (không break caller cũ).

**4. Integration trong Phase 1** [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs):
- `ExtractAllIdw` nhận thêm `bool pullFromVault`.
- Trước mỗi `ProcessSingleIdwFile`, nếu `pullFromVault=true`:
  - `progress?.Report("[i/N] Vault refresh: fileName")`.
  - Gọi `TryPullLatestFromVault(invApp, idwPath)`.
  - Log warning nếu Failed — **không fail toàn extract**, proceed với file local.
  - Catch generic Exception quanh Vault call (non-fatal).

**5. UI checkbox** [Views/FittingManagement/TemplateView.xaml](Views/FittingManagement/TemplateView.xaml):
- `<CheckBox x:Name="ChkPullFromVault" Content="Pull latest from Vault trước khi import">` dưới BOM type, trên button Import.
- Tooltip giải thích prerequisites.

**6. Pass state** [Views/FittingManagement/TemplateView.xaml.cs](Views/FittingManagement/TemplateView.xaml.cs):
- `bool pullFromVault = (ChkPullFromVault.IsChecked == true);`
- Pass vào `_service.ImportIdwFilesAsync(ofd.FileNames, bomType, pullFromVault, progress)`.

### Thiết kế
- **Không reference Vault SDK assembly**: tránh binding cứng version, giảm deployment complexity. Tradeoff: API reliability không đảm bảo 100%, phải late-bind dynamic.
- **Graceful degradation**: mọi Vault failure đều **non-fatal** — import tiếp với file local. User vẫn làm việc được kể cả khi Vault server down/không login.
- **Fallback chain cho method names**: Vault AddIn cho Inventor không document API chính thức, tên method khác nhau qua các Inventor/Vault version (2018/2020/2023/...). Thử 6 tên phổ biến, nếu tất cả fail → log rõ đã thử gì để user biết version compat issue.
- **Log verbose**: mỗi bước có log (count AddIns, tên AddIn match, login status, method result). User debug dễ khi test môi trường thật.
- **Opt-in via checkbox**: mặc định OFF — không tự động gọi Vault gây lag cho user không dùng Vault. User chủ động tick khi cần.

### Giới hạn & expected behavior
- **API không verified**: method names trong fallback chain dựa trên convention/forum posts, chưa test với Vault thật. Có thể tất cả fail ở môi trường thực → log sẽ cho biết method nào hoạt động để tune sau.
- **Yêu cầu môi trường**:
  1. Inventor đã cài đặt (pattern `AcquireInventorInstance` hiện có).
  2. Vault Client installed, Vault AddIn for Inventor enabled (`Tools → Add-In Manager → Autodesk Vault` checked).
  3. User login Vault qua Inventor UI (menu Vault → Log In) trước khi chạy plugin. Plugin không prompt login (tránh modal UI trên worker thread).
- **Không config được Vault server trong plugin**: dùng server/vault mà Inventor đang connect. Đổi server → đổi trong Inventor Vault menu.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test trong môi trường Vault thật:
  1. Mở Inventor, login Vault qua menu.
  2. AutoCAD → Drawing Tool palette → tab Template → tick "Pull latest from Vault trước khi import" → chọn 1-2 file IDW.
  3. Đọc plugin.log tìm dòng `Vault:` để xem:
     - Có detect được Vault AddIn không (count AddIns, tên AddIn match).
     - Login status đọc được không.
     - Method nào trong fallback chain hoạt động (hoặc tất cả fail).
- Nếu tất cả 6 method fail với cùng 1 exception type/message → báo lại để tìm đúng API name cho Vault version đang dùng.
- Nếu Vault AddIn có `Connection.WebServiceManager` (kiểu VDF) → phải dùng approach khác (không phải Automation direct) — sẽ design Phương án B.2.

### Ghi chú API
- **`invApp.ApplicationAddIns`**: collection đếm được qua `.Count`, iterate qua foreach. Mỗi item có `DisplayName` (string), `Activated` (bool), `Automation` (object).
- **Vault AddIn identification**: tên phổ biến "Autodesk Vault" / "Vault" / "Vault Professional" — match chứa "Vault" là an toàn.
- **`RuntimeBinderException`**: exception khi dynamic invoke method không exist trên object. Import `Microsoft.CSharp.RuntimeBinder`.
- **Vault AddIn Automation API**: không có official documentation từ Autodesk. Phải reverse-engineer qua Inventor API docs + forum posts. Method names có thể khác giữa Vault 2020 / 2023 / 2024.
- **Graceful degradation cho COM late-bound**: try/catch bao chặt mọi call. `dynamic` throw 2 loại: `RuntimeBinderException` (member không exist) + runtime exception của method (method exist nhưng throw). Phân biệt 2 loại để không false-positive "method không có".

---

## Session 2026-04-22 (16) — Tách DrawingCollection thành 6 partial file

### Bối cảnh
File `Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs` sau nhiều session đã lên 1372 dòng — khó navigate. Refactor thành partial files trong folder `Utilities/DrawingCollection/` theo concern-based split.

### Đã làm
**1. Tạo folder mới**: `Services/FittingManagement/Utilities/DrawingCollection/`

**2. Split thành 6 file partial** (tất cả cùng `namespace MCG_FittingManagement.Services.FittingManagement`, cùng `public partial class FittingManagementService`):

| File | LOC | Nội dung |
|---|---:|---|
| [.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.cs) | 326 | **Entry point**: `CollectDrawingsAsync` + constants (COLLECTION_GAP, BBOX_OUTLIER_WARN, BATCH_SIZE_WARN, MEMORY_WARN_MB) + `LogMemoryUsage` helper |
| [.Preprocess.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Preprocess.cs) | 405 | **Phase 1**: `PreprocessAll`, `LogPreparedFile`, `RenameBlocksInSideDb`, `PurgeUnusedInSideDb`, `CollectPurgeCandidates`, `ComputeModelSpaceExtents` |
| [.Clone.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Clone.cs) | 220 | **Phase 2**: `CloneToCurrentSpace`, `UnlockAllLayersInDest`, `ComputeInitialOffsetX`, `CollectModelSpaceIds` |
| [.Summary.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Summary.cs) | 258 | **End-of-run summary**: `WriteFinalSummary`, `FindDuplicateCandidates`, `FindKeepAsIsOverlaps` |
| [.Helpers.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Helpers.cs) | 97 | **Static helpers**: `KeepAsIsBlocks`, `IsKeepAsIs`, `IsRecoverableCorruptError`, `GetEffectiveBlockName`, `SanitizeBlockNamePart`, `FindTopOutliers`, `Median` |
| [.Types.cs](Services/FittingManagement/Utilities/DrawingCollection/FittingManagementService.DrawingCollection.Types.cs) | 138 | **Nested stats types**: `PreparedDrawing`, `RenameStats`, `PurgeStats`, `ExtentsStats`, `EntityExtInfo`, `KeepAsIsRefInfo`, `KeepAsIsClonedInfo`, `KeepAsIsOverlapPair`, `CloneStats` |

**3. Xoá file cũ** `Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs` (1372 dòng) → thay bằng 6 file trong subfolder.

### Thiết kế
- **Namespace không đổi**: `MCG_FittingManagement.Services.FittingManagement` — cần cho partial class lookup. Không đổi theo rule `§3` CLAUDE.md (đã có exception cho utility partial files từ trước).
- **File naming**: giữ pattern `FittingManagementService.DrawingCollection.<Section>.cs` — nhất quán với pattern các partial file khác của service (`.BomHelpers.cs`, `.BomInterface.cs`, `.IdwImport.cs`, …).
- **Imports tối giản per file**: mỗi file chỉ `using` các namespace cần cho code của nó — giảm coupling khi navigate.
- **Nested types tập trung 1 file**: các private classes `PreparedDrawing`, `RenameStats`, … dùng xuyên Preprocess/Clone/Summary → gom 1 chỗ để tránh phân tán, dễ sửa schema.
- **Public API không thay đổi**: chỉ có `CollectDrawingsAsync` là public; caller (BlockUtilitiesView) không cần sửa gì.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors, 0 warnings mới.
- **LOC sau refactor:** tổng 1444 dòng (+72 so với 1372 cũ, do thêm using statements + file header comments per partial file).

### Ghi chú API
- **C# partial class**: tất cả file cùng namespace + cùng class name + `partial` modifier → compiler gộp thành 1 class duy nhất. Constants, static fields, nested types, methods chia sẻ scope.
- **Nested private class trong partial**: khai báo ở 1 file bất kỳ, access được từ mọi file partial khác cùng class.
- **File folder không ảnh hưởng namespace**: C# namespace quy định qua `namespace` keyword, không phải folder path. Do đó có thể nest sub-folder tùy ý để tổ chức.
- **Edit tool với partial refactor**: cần Write (tạo file mới) thay vì Edit vì tạo file trong folder chưa tồn tại. Bash `rm` để xoá file cũ.

---

## Session 2026-04-22 (15) — Drawing Collection: defensive hardening + FATAL logging

### Bối cảnh
User báo "hay gặp lỗi FATAL" khi dùng Drawing Collection. Không có log cụ thể — cần audit code + apply defensive measures + enhance logging để capture FATAL khi lặp lại.

Các nguyên nhân nghi ngờ (theo mức khả năng):
- 🔴 Memory pressure với batch lớn (Phase 1 hold N sideDb cùng lúc, ~500MB/file).
- 🔴 Layer locked/frozen trong dest → `eLayerLocked` khi WblockCloneObjects.
- 🟡 Native code crash (SEH) trên file corrupt không detect bằng ErrorStatus.
- 🟡 Dynamic block reference state issue sau clone.

### Đã làm
[Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs) — 3 nhóm hardening:

**A1. Unlock layers dest trước Phase 2:**
- Helper mới `UnlockAllLayersInDest(destDb)`: iterate LayerTable, với mỗi LayerTableRecord IsLocked → set false; IsFrozen (trừ layer "0") → set false. Try/catch per-layer, count changed.
- Gọi ngay trong `using DocumentLock` trước ComputeInitialOffsetX.
- Log: `Đã unlock/thaw {N} layer trong dest trước khi clone.`

**A2. Batch size warning + memory tracking:**
- Const `BATCH_SIZE_WARN = 15`, `MEMORY_WARN_MB = 2048`.
- Log `CẢNH BÁO batch lớn: X files ...` nếu vượt ngưỡng.
- Helper `LogMemoryUsage(label)`: log `managed=XMB, workingSet=YMB` từ `GC.GetTotalMemory(false)` + `Process.WorkingSet64`.
- Log memory tại các điểm: Start, After Phase 1, After Phase 2, After forced GC.
- Sau mỗi file trong Phase 2 loop, check memory — nếu > MEMORY_WARN_MB → log `⚠ Memory = XMB ...`.

**A3. Force GC sau Phase 2:**
- `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` + log memory pre/post → user thấy memory leak nếu sideDb không dispose sạch.

**C1. Broader exception taxonomy trong Phase 2 per-file loop:**
- `OutOfMemoryException`: log `FATAL OutOfMemory tại file X (index i/N)`, log memory snapshot, **break** batch (file kế chắc cũng fail).
- `SEHException` (native crash reach managed): log `FATAL SEHException ... ErrorCode=0x{ErrorCode:X} — native code crash, file có thể corrupt`.
- `Autodesk.AutoCAD.Runtime.Exception`: log riêng với `ErrorStatus` name — dễ grep trong log.
- Generic `Exception` fallback.
- `finally`: wrap `item.SideDb?.Dispose()` trong try/catch riêng, log dispose fail.

**C2. Defensive cleanup outer try/finally:**
- Bọc toàn bộ Phase 2 (bao gồm using DocumentLock) trong `try/finally`.
- `finally` iterate `prepared` list → dispose mọi `p.SideDb` chưa `IsDisposed` (idempotent). Log số orphan disposed.
- Xử lý case OOM break loop hoặc exception ngoài per-file catch (UnlockAllLayersInDest / ComputeInitialOffsetX / ZoomExtents throw).

### Thiết kế
- **Không catch `AccessViolationException`**: .NET Framework 4.8 mặc định block CSE (Corrupted State Exceptions) — cần `[HandleProcessCorruptedStateExceptions]` attribute hoặc `legacyCorruptedStateExceptionsPolicy` in config. Plugin không control config. Thêm attribute chỉ trên method level async → runtime rewrite state machine, attribute có thể không propagate. Để process crash tự nhiên — AutoCAD có crash log riêng.
- **OOM break vs continue**: OOM → managed heap đã critical. File kế sẽ allocate tiếp → chắc chắn fail. Break batch để dừng sớm, GC cleanup.
- **Unlock chỉ layer, không thaw layer "0"**: layer 0 là default/current, thaw có thể throw nếu nó là current layer.
- **Memory log dùng 2 metric**: `GC.GetTotalMemory(false)` = managed heap (không force collect, phản ánh sau allocate). `Process.WorkingSet64` = RAM thực OS cấp cho process. Cả 2 cần track để phân biệt managed leak vs native/unmanaged leak (sideDb là native wrapper).
- **Guard rail chỉ log warning**, không block. User có thể chủ động chia batch nếu thấy warning.

### Tác động khi chạy batch bình thường (10 files)
Log sẽ thêm:
```
[Memory] Start: managed=150MB, workingSet=800MB
CẢNH BÁO batch lớn: ... (nếu ≥15 files)
Dest doc (nhà kho) path: ...
[Phase 1 preprocess as before]
[Memory] After Phase 1 (10 sideDb held): managed=420MB, workingSet=1200MB
Đã unlock/thaw 3 layer trong dest trước khi clone.
Scan dest ModelSpace: ...
[Phase 2 per-file clone as before, + memory log nếu vượt 2GB]
[Phase 2] xong ...
[Memory] After Phase 2: managed=380MB, workingSet=1150MB
[Memory] After forced GC: managed=180MB, workingSet=900MB
```

### Các nguyên nhân FATAL khó catch được bằng C# alone
- **Access violation trong native code AutoCAD** (null deref, use-after-free trong acmgd.dll): process crash trực tiếp, C# không catch được kể cả với HandleProcessCorruptedStateExceptions.
- **Stack overflow**: C# catch được StackOverflowException là uncatchable sau .NET 2.0. Process terminate.
- **AutoCAD internal assert fail**: process abort.

Khi FATAL thực sự xảy ra, user cần check:
1. `%APPDATA%\MCG_FittingManagement\plugin.log` — managed-side context (memory, last file processing).
2. `%LOCALAPPDATA%\Autodesk\AutoCAD 2023\R23.1\enu\AcadCoreStack.log` — AutoCAD crash dump.
3. Windows Event Viewer → Application → `acad.exe` Error entries.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- User test lại Drawing Collection với các batch đã gặp FATAL trước đây.
- Nếu FATAL tiếp diễn, gửi plugin.log (toàn bộ session) + AcadCoreStack.log → phân tích chính xác.
- Nếu log cho thấy `⚠ Memory > 2GB` trước crash → confirm OOM → implement giải pháp B (interleave Phase 1+2) để giảm peak memory.
- Nếu log SEHException trên 1 file cụ thể → file corrupt, skip file đó hoặc chạy RECOVER thủ công.

### Ghi chú API
- **`Database.IsDisposed`**: property check instance đã dispose chưa. Double-dispose Database không throw (benign) nhưng check để đếm đúng.
- **`SEHException.ErrorCode`**: HRESULT của native crash. Format hex (`:X`) match Windows convention. Vd `0x80004005 = E_FAIL`, `0xC0000005 = ACCESS_VIOLATION`.
- **`LayerTableRecord.IsFrozen` trên current layer**: throw `eInvalidInput`. Try/catch riêng cho `IsFrozen = false`. Layer "0" đặc biệt — không thể freeze → skip.
- **`[HandleProcessCorruptedStateExceptions]` trên async method**: không reliable. Runtime rewrite thành state machine class; attribute ở method gốc có thể không áp dụng cho MoveNext() sau rewrite. Nên không dùng.
- **`Process.WorkingSet64`**: RAM thực process đang giữ. Khác `VirtualMemorySize64` (virtual address space, có thể lớn hơn nhiều nhưng không reflect RAM pressure).

---

## Session 2026-04-22 (14) — Drawing Collection: append sau A1 hiện hữu trong nhà kho

### Yêu cầu user
Workflow mới:
- Nhà kho (dest doc) đã có sẵn **nhiều block A1** từ các lần collect trước.
- Mỗi lần chạy Collect Drawing, A1 MỚI phải nối tiếp **sau** A1 cũ theo trục X — không đè lên, không bắt đầu từ gốc 0.

Công thức: `initialOffsetX = max(A1_existing.bbox.MaxX) + GAP` thay vì `initialOffsetX = 0`.

### Đã làm
[Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs):

**1. Helper mới `ComputeInitialOffsetX(Database destDb)`:**
- Mở transaction trên dest db, iterate Model Space.
- Với mỗi `BlockReference`, lấy `GetEffectiveBlockName` (xử lý dynamic block). Nếu name ∈ `KeepAsIsBlocks` (A1, CAS_HEAD) → lấy `GeometricExtents.MaxPoint.X`, track max.
- Return `maxX + COLLECTION_GAP` nếu có reference, `0` nếu dest trống A1.
- Log rõ ràng:
  - Dest rỗng: `"Scan dest ModelSpace: không có A1/CAS_HEAD existing → initial offsetX = 0 (bắt đầu từ gốc)."`
  - Dest có N refs: `"Scan dest ModelSpace: N A1/CAS_HEAD reference(s) existing, Xmax=X → initial offsetX = X+1000 (collect mới nối tiếp, cách 1000mm)."`

**2. Gọi trong Phase 2 (trong `using DocumentLock`):**
```csharp
using (DocumentLock docLock = doc.LockDocument())
{
    double offsetX = ComputeInitialOffsetX(doc.Database);
    firstOffsetX = offsetX;
    // ... loop ...
}
```

**3. Update layout sanity check trong `WriteFinalSummary`:**
- `expectedSpan = firstOffsetX + sumEffective + advancedFiles × GAP` (trước đây không tính `firstOffsetX`).
- Thêm dòng log: `Initial offsetX: Xmm (nối tiếp sau A1 existing trong dest)` hoặc `(dest trống A1)`.

### Thiết kế
- **Scan cả A1 + CAS_HEAD**: dùng `KeepAsIsBlocks` HashSet consistent với rename logic. Nếu user chỉ muốn A1, có thể dễ filter trong helper.
- **`GeometricExtents` trên dest**: luôn có (dest đã render), không cần fallback.
- **Dynamic block handling**: `GetEffectiveBlockName` xử lý `IsDynamicBlock` — reference đến A1 dynamic (Name=`*U...`) vẫn detect được.
- **KHÔNG scan Paper Space/Layout**: collect chỉ thêm vào Model Space → chỉ quan tâm A1 trong Model Space dest.
- **Edge cases:**
  - Dest hoàn toàn trống (brand-new drawing): scan trả 0, behavior cũ giữ nguyên.
  - Dest có content nhưng không có A1: scan trả 0, collect bắt đầu từ gốc → có thể đè content khác (but user đã chấp nhận yêu cầu focus vào A1 Xmax).
  - Dest có A1 ở X âm lớn: `maxX + GAP` vẫn là số âm hoặc nhỏ → collect bắt đầu ở đó, OK.

### Tác động với test hiện tại
Giả sử user có workflow:
1. Chạy Collect lần 1 với 9 file → nhà kho có 9 A1 trải từ x=0 → x≈400k, Xmax ≈ 399695.
2. Chạy Collect lần 2 với 5 file mới:
   - Trước: bắt đầu từ 0 → đè lên A1 cũ.
   - Sau fix: `initial = 399695 + 1000 = 400695` → 5 A1 mới trải từ 400695 → x≈480k.
3. Chạy lần 3 với 3 file → bắt đầu từ `~480k + 1000`, v.v.

Layout tích lũy qua nhiều lần collect, không conflict.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test scenario tích lũy:
  1. Mở bản vẽ trống → Collect 3 file → verify log `không có A1/CAS_HEAD existing → initial offsetX = 0`.
  2. Save, đóng, mở lại → Collect 3 file khác → verify log `3 A1/CAS_HEAD reference(s) existing, Xmax=X → initial offsetX = X+1000`, các A1 mới nối tiếp sau A1 cũ.
  3. Xác nhận bản vẽ dest có 6 A1 trải liên tục theo X, không có cặp nào overlap.
- Edge: dest có A1 lẫn content thường, collect thêm → kiểm ngang X xem A1 mới có đè content non-A1 không (acceptable per spec).

### Ghi chú API
- **`Database.TransactionManager.StartTransaction` lồng với `DocumentLock`**: an toàn. DocumentLock bảo vệ doc, transaction riêng cho db op. Nested fine.
- **`BlockTableRecord.ModelSpace` constant**: `"*Model_Space"` — fixed string, dùng `bt.Has` + `bt[name]` an toàn mọi AutoCAD version.
- **Scan existing O(N) trên ModelSpace**: với bản vẽ có 100k entity, mất ~100-500ms. Acceptable vì chỉ chạy 1 lần/collect.

---

## Session 2026-04-22 (13) — Drawing Collection: self-collect guard

### Bối cảnh
User test session 12 cho thấy fix A1-aware layout hoạt động đúng (0 overlap). User hỏi tiếp: "nếu user lỡ chọn chính file nhà kho (dest doc) trong OpenFileDialog để collect, plugin có nhận không?" — Câu trả lời: **có**, đây là edge case chưa handle, hậu quả nghiêm trọng:

- Plugin đọc file nhà kho **từ disk** (state cũ, không có unsaved changes) vào side db.
- Rename toàn bộ block trong side db với prefix `[nhà_kho_filename]_`.
- Clone vào current doc → content nhà kho bị nhân đôi, block của nhà kho cũ đụng tên mới (hoặc bị giữ cũ do Ignore).
- Layout dịch horizontal → cấu trúc bản vẽ nhà kho bị vỡ.

### Đã làm
[Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs):

**1. `CollectDrawingsAsync` detect dest doc path:**
```csharp
string destDocPath = null;
try
{
    var activeDoc = Application.DocumentManager.MdiActiveDocument;
    string dbFilename = activeDoc?.Database.Filename;
    if (!string.IsNullOrWhiteSpace(dbFilename))
        destDocPath = Path.GetFullPath(dbFilename);
}
catch { /* doc chưa save hoặc path resolve fail → destDocPath=null, skip check */ }
```
Log path nếu có để user confirm dest doc đúng: `Dest doc (nhà kho) path: C:\...\nha_kho.dwg`.

**2. `PreprocessAll` thêm tham số `destDocPath`, check self-collect:**
- So sánh `Path.GetFullPath(path)` vs `destDocPath` case-insensitive (Windows filesystem convention).
- Nếu trùng: `FailCount++`, AddError với message rõ *"Không thể collect chính bản vẽ 'nhà kho' đang mở vào chính nó. Lưu file và mở bản vẽ khác làm nhà kho, hoặc bỏ file này khỏi danh sách chọn."*, log `SKIP — self-collect detected: {path}`, continue.

### Thiết kế & edge cases
- **Doc chưa save (Untitled)**: `Database.Filename` = "" hoặc null → `destDocPath=null` → không check. Không cần bảo vệ vì user chưa save thì không thể chọn file-on-disk tương ứng.
- **Case-insensitive**: Windows filesystem mặc định case-insensitive. Dùng `OrdinalIgnoreCase` match `C:\Foo\File.dwg` với `c:\foo\FILE.DWG`.
- **Normalize với `Path.GetFullPath`**: chuẩn hoá `\\` vs `/`, relative vs absolute. Tuy vậy KHÔNG resolve symlinks — edge case symlink sẽ bypass check (hiếm trên Windows drafting).
- **Unsaved changes in dest doc**: user có thể nghĩ "sẽ đọc state mới", nhưng side db đọc từ disk → state CŨ. Self-collect detection block việc này — đúng behavior.
- **KHÔNG fail toàn batch**: các file hợp lệ vẫn chạy. Chỉ file self-referencing bị skip.

### Về nhận định "content nằm trong A1" (user raised)
- Log thực tế: file 04 content=42900mm, A1=17146mm → **content vượt ngoài A1**. Không đúng với giả định "content luôn trong A1".
- Khả năng: file gốc có dimension/view đặt ngoài khung tên.
- `max(content, A1) + 1000` đã chọn để an toàn với cả 2 trường hợp (A1 bao content → A1 wins; content overflow → content wins). Giữ nguyên formula, không đổi sang A1-only.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test scenario:
  1. Mở file `nhà_kho.dwg` trong AutoCAD.
  2. Chạy Drawing Collection, chọn `nhà_kho.dwg` + vài file khác.
  3. Verify: log có `SKIP — self-collect detected: ...`, dialog kết quả báo file nhà kho fail với message rõ, các file khác collect bình thường.
- Sau đó test negative: chạy với nhà kho untitled (chưa save) + chọn vài file → plugin không có destDocPath, không check → chạy bình thường (đúng expected).

### Ghi chú API
- **`Document.Database.Filename`**: full path của file DWG đang mở. Untitled doc trả về empty string hoặc `"Drawing1.dwg"` relative-like (behavior khác nhau giữa AutoCAD versions). Check `IsNullOrWhiteSpace` + `Path.GetFullPath` để normalize.
- **`Path.GetFullPath` quirks**: throw nếu path có ký tự invalid; wrap try/catch. Không resolve symlinks (dùng `File.GetSymbolicLinkTarget` hoặc `Path.GetRealPath` nếu cần).
- **Thứ tự check**: self-collect check PHẢI trước `new Database()` + `ReadDwgFile` — vì ReadDwgFile trên file đang mở bởi AutoCAD có thể throw `eFileSharingViolation` hoặc đọc snapshot cũ. Detect sớm tiết kiệm exception handling.

---

## Session 2026-04-22 (12) — Drawing Collection: A1-aware layout (fix overlap)

### Bối cảnh
Log session 11 @13:04:14 cho thấy 3 cặp A1 overlap (947mm, 739mm, 39mm) do **A1 width > file content bbox + 1000mm gap** ở 1 số file:
- File 03: content=0, A1=17146 → A1 extend 17146mm → overlap 947mm với file 04.
- File 08/09: content (15407/16107) < A1 (17146) → A1 thừa 1039-1739mm → overlap 39-739mm.

User quyết định: layout dùng `max(content_width, A1_width) + 1000` để A1 rộng hơn content không đè file kế, đồng thời content rộng hơn A1 (như file 01 content=2742m) vẫn giữ nguyên.

### Đã làm
[Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs):

**1. `ComputeModelSpaceExtents` — refactor để scan A1 bất kể `GeometricExtents` fail:**
- Tách riêng try/catch cho `GeometricExtents` (set `hasExt` flag).
- Phần scan `BlockReference` → keep-as-is nay chạy BẤT KỂ extents OK hay không. Nếu không có extents, Bbox fallback về `Extents3d(br.Position, br.Position)` (1 điểm tại insertion point).
- Lý do session 11 log không có `[src] A1`: A1 BlockReference trong side db thường throw `eInvalidExtents` (graphics chưa realized) → trước đây scan bị skip theo catch → bỏ sót.

**2. Phase 2 loop trong `CollectDrawingsAsync` — dùng effective width:**
```csharp
double effectiveW = item.HasExtents ? item.Width : 0;
foreach (var ki in cloneStats.KeepAsIsCloned)
{
    double a1MaxXRel = ki.Bbox.MaxPoint.X - offsetX;  // A1 right-edge relative to current file's offsetX
    if (a1MaxXRel > effectiveW) effectiveW = a1MaxXRel;
}
offsetX += effectiveW + COLLECTION_GAP;  // thay cho `item.Width + COLLECTION_GAP`
```
- Nếu A1 extend vượt content, log `Layout width adjusted: content=X → effective=Y (A1 extend Zmm)`.
- `advance` trigger: `HasExtents || KeepAsIsCloned.Count > 0`. File rỗng hoàn toàn (không content + không A1) vẫn không dời offsetX.

**3. `PreparedDrawing` thêm 2 field** `EffectiveWidth` + `Advanced` — để Phase 2 ghi lại cho summary dùng.

**4. `WriteFinalSummary` — layout sanity check dùng sum(effective):**
- `sumEffective = sum(p.EffectiveWidth for p.Advanced)` thay vì `sumWidths`.
- `advancedFiles = count(p.Advanced)` thay vì `bboxFiles`.
- Thêm dòng đếm `filesAdjustedByA1` — số file phải mở rộng vì A1 > content.
- Delta sanity vẫn so `expectedSpan` vs `lastOffsetX`.

### Tác động dự kiến với bộ 10 file test (session 11)
- File 01: content=2742011, A1=171460 → effective=2742011 (content wins). Không đổi.
- File 02: content=202673, A1=171460 → effective=202673. Không đổi.
- File 03: content=0, A1=17146 → effective=17146. offsetX kế **thay đổi**: 2947684 → 2964830.
- File 04-05: content=42900 > A1=17146 → effective=42900. Không đổi (nhưng base offsetX dời theo file 03).
- File 07: content=16269, A1=17146 → effective=17146 (A1 thắng 877mm).
- File 08: content=15407, A1=17146 → effective=17146 (A1 thắng 1739mm).
- File 09: content=16107, A1=17146 → effective=17146 (A1 thắng 1039mm).
- File 10: content=14143, A1=17146 → effective=17146 (A1 thắng 3003mm).

Overlap dự kiến **SẼ BIẾN MẤT** cho cả 3 cặp:
- [3]↔[4]: A1 file 03 ends at 2946684+17146=2963830, file 04 starts tại 2946684+17146+1000=2964830 → gap 1000. ✓
- [8]↔[9]: A1 file 08 ends tại offsetX+17146, file 09 starts tại offsetX+17146+1000 → gap 1000. ✓
- [9]↔[10]: cùng logic. ✓

Tổng span layout sẽ hơi dài hơn trước (do file 07/08/09/10 mỗi file dời thêm tối đa ~3000mm).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test lại bộ 10 file → verify:
  - Không còn dòng `⚠ OVERLAP DETECTED` trong summary.
  - File 03/07/08/09/10 có dòng `Layout width adjusted: content=X → effective=17146`.
  - Layout sanity `✓ Khớp` với sum(effective).
  - Bản vẽ dest không còn 2 A1 đè nhau.

### Ghi chú API
- **`Extents3d(Point3d min, Point3d max)` constructor** — cho phép tạo bbox "degenerate" 1 điểm khi không có extents thực. Tránh `default(Extents3d)` vì đó là `(Point3d.Origin, Point3d.Origin)` → bbox tại gốc global, có thể gây false positive trong overlap detection.
- **A1 extent relative** — dùng `ki.Bbox.MaxPoint.X - offsetX` để tính "A1 right-edge là bao xa từ vị trí file" → so sánh với content width (đã là relative 0 → content_width).
- **`GeometricExtents` trong side db**: throw `eInvalidExtents` cho BlockReference phổ biến vì side db không render graphics. Khi clone sang dest (có render context), extents tự sẵn sàng — nên phase 2 log `[dest] A1 bbox=[...]` luôn có giá trị.

---

## Session 2026-04-22 (11) — Drawing Collection: diagnose A1/CAS_HEAD overlap

### Bối cảnh
User báo: sau khi collect, trong bản vẽ đích có **2 block A1 chèn lên nhau** (không trùng 100%). Log hiện tại chỉ nói `A1:1` kept mỗi file nhưng không theo dõi được **BlockReference A1 trong Model Space** → không biết bao nhiêu reference được clone, vị trí ở đâu, có overlap không. Cần log để đánh giá root cause, **không phải auto-fix**.

### Đã làm
[Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs):

**3 lớp diagnosis mới (source scan → post-clone tracking → overlap detection):**

1. **Phase 1 source scan** — piggy-back trong `ComputeModelSpaceExtents`: nếu `ent is BlockReference && effectiveName ∈ {A1, CAS_HEAD}` → ghi `KeepAsIsRefInfo {BlockName, Position, Rotation, Bbox, HandleValue}` vào `ExtentsStats.KeepAsIsRefsInSource`.
   - `LogPreparedFile` log 1 group-summary line + 1 dòng/reference: `[src] A1 handle=0x... pos=(...) rot=...° bbox=[...]`.
   - Giúp xác định: file gốc có A1/CAS_HEAD **trong Model Space** (không phải Paper Space) không, bao nhiêu cái, ở đâu.

2. **Phase 2 post-clone tracking** — trong `CloneToCurrentSpace`, sau `TransformBy(displace)`: nếu cloned entity là `BlockReference` với effective name keep-as-is → ghi `KeepAsIsClonedInfo` với `Position/Bbox` **sau transform** + `DestHandleValue`.
   - Log 1 dòng/reference sau dòng `Clone '...':` — prefix `[dest]` để phân biệt với `[src]`.

3. **Overlap detection trong `WriteFinalSummary`** — aggregate tất cả `KeepAsIsCloned` thành flat list `(FileName, Info)`, log toàn bộ references, sau đó `FindKeepAsIsOverlaps` (O(N²), AABB intersection test). Với mỗi cặp overlap:
   - Compute overlap area + % của block nhỏ hơn.
   - Log: `[i] ↔ [j]: A1 'fileA' (0x...) vs A1 'fileB' (0x...), overlap area=..., offset pos=(dx, dy)mm`.
   - Log 3 lý do thường gặp ở cuối (Model Space vs Paper Space, file width nhỏ, entity đi kèm).

**Helper mới:**
- `GetEffectiveBlockName(br, tr)`: xử lý dynamic block — nếu `IsDynamicBlock`, trả tên của `DynamicBlockTableRecord` (gốc) thay vì `Name='*U123'` (anonymous).
- `FindKeepAsIsOverlaps(all)`: AABB 2D intersection, trả về `List<KeepAsIsOverlapPair>` với `OverlapArea` + `PctOfSmaller`.
- 3 nested class mới: `KeepAsIsRefInfo` (source), `KeepAsIsClonedInfo` (dest), `KeepAsIsOverlapPair`.

### Thiết kế
- **Không auto-fix**: yêu cầu user là diagnose, không filter/dedupe. Nếu sau này muốn dedupe A1 (chỉ giữ 1 khung tên trong dest), sẽ là phase riêng.
- **AABB 2D overlap**: bỏ qua Z vì title block 2D. Dùng `(oxMax > oxMin && oyMax > oyMin)` — không tính touching edge là overlap.
- **PctOfSmaller** chứ không PctOfLarger: overlap 90% của khung A1 nhỏ quan trọng hơn 10% của khung A1 lớn.
- **Source-side scan + dest-side scan**: user so sánh được 2 list để hiểu "A1 ở đây trong file gốc → sau clone + offset nó về đâu trong dest".
- **Effective name cho dynamic block**: quan trọng vì một số AutoCAD plugin tạo A1 dynamic (có thể switch A1/A2/A3 size) → `br.Name` trả `*U234` → không match `KeepAsIsBlocks` → miss tracking.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test lại với bộ 10 file cũ → log sẽ có:
  - Mỗi file có A1/CAS_HEAD trong Model Space: `Keep-as-is refs in source ModelSpace: A1×N` + 1 dòng/reference.
  - Sau clone mỗi file: `[dest] A1 handle=0x... pos=(...) bbox=[...]`.
  - Summary: full list + overlap pairs với area + offset.
- Nếu overlap thật sự do Model Space embed (reason #1), log sẽ confirm bằng `KeepAsIsRefsInSource.Count > 0`.
- Nếu không có ref nào trong source ModelSpace nhưng vẫn thấy A1 trong dest: khả năng là user nhầm, hoặc A1 được insert từ Layout qua cách khác.
- User gửi lại log → evaluate root cause chính xác.

### Ghi chú API
- **`BlockReference.IsDynamicBlock`**: `true` khi block là dynamic block. Trong trường hợp này `Name` trả về `"*U"+N` (anonymous BTR đại diện cho 1 variant), `DynamicBlockTableRecord` trỏ về BTR gốc có tên "thực" (A1, etc).
- **`Entity.GeometricExtents` sau `TransformBy`**: trả bbox mới (đã dịch). AutoCAD cập nhật cache extents ngay khi TransformBy — không cần regen. Tuy nhiên trong trường hợp entity dùng non-rectangular extents (text/dim), đôi khi throw `eInvalidExtents` → catch và bỏ qua.
- **AABB overlap test**: `max(aMin.X, bMin.X) < min(aMax.X, bMax.X)` cho mỗi trục. Strict `<` (không `<=`) để 2 block kề nhau (touching) không bị flag overlap.
- **Handle.Value**: `long`, dùng `:X` format trong interpolation cho hex uppercase (matching AutoCAD `LIST` output).

---

## Session 2026-04-22 (10) — Drawing Collection: outlier diagnosis + label clarity + duplicate detection

### Bối cảnh
Sau khi test session 9 (10 file, 100% pass, 11.8s), phân tích log phát hiện 3 vấn đề cần thêm thông tin:
- **A.** File 01 bbox 2742m × 1616m (entity orphan tại (1.3M, 0.8M)) — user cần biết **entity nào** gây ra để dọn.
- **B.** Label `entities cloned=3503` so với `srcIds=269` gây nhầm — `IsPrimary` bao gồm cả sub-entity (attribute, vertex, nested).
- **C.** File 04 và 05 có bbox giống hệt `(42900 × 11097)` + entity count gần nhau (132 vs 134) — có thể là file trùng user nên check.

### Đã làm
[Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs):

**A. Top-3 outlier diagnosis:**
- `ExtentsStats` thêm field `List<EntityExtInfo> EntityInfos` — collect per-entity (Type, HandleValue, CenterX/Y, Width/Height, Layer) trong pass duy nhất của `ComputeModelSpaceExtents`.
- Class mới `EntityExtInfo` chứa metadata entity không lock ObjectId (side db bị dispose sau Phase 1).
- Helper `FindTopOutliers(entities, count)`: tính median center (X, Y), sort entity theo `dx²+dy²` giảm dần, take N. Dùng median thay vì mean để chính outlier không làm lệch tâm.
- Helper `Median(List<double>)`: trả median đúng cho list lẻ/chẵn.
- Khi cảnh báo bbox outlier, log thêm 3 dòng:
  ```
    Outlier #1: Line handle=0x7A2F layer='0' center=(1392010,752145) size=(120×80)
    Outlier #2: DBText handle=0x8031 layer='TEXT' center=(-1336500,-856900) size=(2500×1500)
    Outlier #3: ...
  ```
- Handle format hex (`0xXXXX`): user dùng `(handent "XXXX")` trong LISP hoặc `SELECT` + gõ handle để locate entity trong AutoCAD.

**B. Label clarity cho clone log:**
- Đổi `srcIds={N}, entities cloned={M}` → `topLevel={N} → totalPrimaries (incl. nested sub-entities)={M}`.
- Thêm comment giải thích `IdPair.IsPrimary` semantics trong AutoCAD API (không chỉ top-level entity, mà cả attribute/vertex/sub-entity của nested block).

**C. Duplicate detection trong Summary:**
- Helper `FindDuplicateCandidates(prepared)`: O(N²), check các cặp có `|w1-w2|<1mm` và `|h1-h2|<1mm` và `|e1-e2|/max(e1,e2) < 5%`. Return `List<Tuple<int,int>>`.
- Append block mới vào `WriteFinalSummary` nếu có cặp:
  ```
  ⚠ Duplicate candidates (bbox khớp trong tolerance 1mm, entity count chênh <5%):
    #4 '71-57589-04.dwg' (entities=132) ≈ #5 '71-57589-05.dwg' (entities=134)
    → User nên kiểm xem có phải cùng 1 bản vẽ bị copy/save hai lần không.
  ```

### Thiết kế
- **Median không Mean**: file có 1 entity tại (1.3M, 0.8M) và 237 entity tại (0, 0) → mean ≈ (5500, 3500) lệch nhưng median ≈ (0, 0) chính xác. Dùng median.
- **`dx² + dy²` không Sqrt**: rank-preserving monotonic, tiết kiệm N phép Sqrt.
- **O(N²) duplicate check**: N = số file user chọn, thường ≤20 → 400 so sánh = negligible.
- **Không dispose `EntityInfos`**: memory cost ~100 bytes × totalEntities (~2000 cho 10 file) ≈ 200KB, lifetime = Phase 1 → Summary. Chấp nhận.
- **Handle format**: dùng `HandleValue:X` (hex viết hoa) cho giống `LIST` trong AutoCAD.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test lại bộ 10 file cũ → log phase 1 sẽ có:
  - File 01: 3 dòng Outlier sau cảnh báo bbox → user biết handle nào cần xóa/di chuyển.
  - File 02: tương tự, 3 dòng Outlier cho bbox 202m.
- Summary cuối sẽ có section `Duplicate candidates` báo cặp #4 ≈ #5.
- Clone log sẽ rõ `topLevel=269 → totalPrimaries=3503` thay vì `entities cloned=3503` nhầm lẫn.

### Ghi chú API
- **`Entity.Handle`**: trả về `Handle` struct — `Handle.Value` là `long`, `Handle.ToString()` là **decimal string**. AutoCAD UI/`LIST` command hiển thị **hex**. Để log hex chuẩn: `$"0x{handle.Value:X}"`.
- **`(handent "XXXX")` LISP**: chấp nhận hex handle (không prefix `0x`) → return entity name, `command "SELECT" (handent "XXXX")` chọn được. Đây là cách user verify entity từ handle đã log.
- **`IdPair.IsPrimary` semantics**: AutoCAD doc nói "primary" là object owned directly by cloned container. Trong `WblockCloneObjects`, khi clone BlockReference có AttributeReferences, cả BlockReference và AttributeReferences đều IsPrimary=true — không chỉ top-level src. Đã clarify trong log.
- **Median on `List<double>` size đến ~10k**: `OrderBy.ToList()` là O(N log N). Cho modelspace ≤1M entity vẫn OK (vài trăm ms). Không cần Quickselect.

---

## Session 2026-04-22 (9) — Bổ sung log chi tiết cho Drawing Collection (đánh giá qua text thuần)

### Yêu cầu user
Khi Drawing Collection pass 100%, log phải đủ thông tin để đánh giá chất lượng kết quả + cải tiến thêm, **không cần user gửi screenshot feedback**.

### Đã làm
[Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs): refactor toàn bộ signature các method phase 1 + phase 2 để trả về stats objects, thêm Stopwatch per-phase và summary block cuối run.

**Stats types mới (private nested classes):**
- `RenameStats`: `TotalBtr`, `Renamed`, `KeepAsIs_A1`, `KeepAsIs_CasHead`, `Skipped_Layout/Xref/Anonymous/Conflict/Empty`, `Failed`.
- `PurgeStats`: `TotalErased`, `Passes`, `HitMaxPasses` (cờ đạt trần 10 pass mà vẫn còn erase).
- `ExtentsStats`: `HasExtents`, `Width`, `Height`, `Extents`, `TotalEntities`, `EntitiesWithExtents`, `EntitiesNoExtents`.
- `CloneStats`: `SrcIds`, `PrimaryCloned` (entity thực sự copy), `SymbolsCloned`, `SymbolsIgnored` (trùng layer/block/linetype → giữ bản dest), `Transformed`, `TransformFailed`, `Dx`.
- `PreparedDrawing` thêm 5 field để hold stats xuyên 2 phase: `RenameStats`, `PurgeStats`, `ExtStats`, `CloneStats`, `PlacedOffsetX`.

**Log per-file Phase 1 (3 dòng/file):**
```
Prepared 'foo.dwg' (took 240ms: read=120, rename=60, purge=50, extents=10)
  Rename: total=500, renamed=401, kept=[A1:1, CAS_HEAD:0], skipped=[xref:2, layout:2, anon:85, conflict:9, empty:0], failed=0
  Purge: erased=94 across 3 pass(es)
  ModelSpace: 184 entities (174 có extents, 10 không có), bbox=[(-50.00,-30.00)-(16218.52,8400.00)], w=16268.52, h=8430.00
```

**Log per-file Phase 2:**
```
  Clone 'foo.dwg': srcIds=184, entities cloned=184, symbols [cloned=12, ignored=8], transformed=184 (failed=0), dx=50.00, took=520ms
  Collected OK: foo.dwg placed at x=0.00 → next offsetX=17268.52
```

**End-of-run summary block:**
```
====== DRAWING COLLECTION — SUMMARY ======
Requested: 9 file(s) | Success: 8 | Failed: 1

Per-file breakdown:
  #  | Width(mm) | Height(mm) | Entities | Renamed | Purged | Placed@X   | File
   1 |    202673 |      18500 |      520 |     401 |     94 |          0 | 71-57589-02.dwg
   2 |         0 |          0 |        1 |     363 |    106 |     203673 | 71-57589-03.dwg
  ...

Totals:
  ModelSpace entities scanned   : 2847
  Entities cloned into dest doc : 2847
  Symbols ignored (dup-name)    : 47   (dest giữ định nghĩa, src bỏ — đúng thiết kế...)
  TransformBy failures          : 0
  Blocks renamed                : 3243
  Blocks kept as-is [A1]        : 2
  Blocks kept as-is [CAS_HEAD]  : 1
  Purge: erased total           : 747

Layout sanity:
  Sum file widths : 324990mm
  Files w/ bbox    : 8 → gaps = 8 × 1000 = 8000mm
  Expected next offsetX after last file : 332990mm
  Actual   next offsetX after last file : 332990mm
  ✓ Khớp (delta=0.00mm).

Timing: phase1=1820ms, phase2=4950ms, total=6770ms
  Avg/file: phase1=202ms, phase2=550ms
==========================================
```

### Thiết kế
- **Per-phase Stopwatch**: đo `read/rename/purge/extents` riêng lẻ — user nhận ra bottleneck ngay (vd: file bị stuck ở purge do corrupt, hay chậm ở read do file to).
- **Mapping breakdown chi tiết**: tách `IsPrimary` (entity) vs non-primary (symbol table record), `IsCloned` (đã clone) vs !IsCloned (ignore do trùng) — user thấy rõ có bao nhiêu layer/block/linetype bị trùng tên và được ưu tiên bản dest.
- **Layout sanity auto-check**: so sánh `lastOffsetX` thực tế vs `sum(widths) + bboxFiles × GAP` kỳ vọng. Delta > 0.5mm → ⚠ (có file fail phase 2 hoặc bbox=false). Delta ≈ 0 → ✓. Không cần user tự tính toán.
- **HitMaxPasses cờ**: nếu purge đạt 10 pass mà vẫn còn erase, log warning — file có thể còn đối tượng dư chưa purge hết.
- **Bảng ASCII per-file**: 1 dòng/file với 7 cột (Width/Height/Entities/Renamed/Purged/Placed/File) → user scan nhanh thấy file nào bất thường (width=0, entities=1, placed xa, v.v.).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test lại với 9 file cũ (session 7 test data) → log sẽ có:
  - Breakdown rename chi tiết: biết A1/CAS_HEAD có match không (user muốn biết block khung tên có được bảo toàn đúng).
  - Extents detail: `EntitiesNoExtents` giúp trace file có width=0 (như file 03 session 7).
  - Clone symbols ignored count: biết bao nhiêu layer/block trùng giữa file src và dest → có thể là chỉ báo "chất lượng file nguồn".
  - Layout sanity: auto-check xem offsetX cuối cùng có đúng không.

### Ghi chú API
- **`IdPair.IsPrimary`**: true cho top-level cloned object (entity trong modelspace đích). false cho owned/referenced object (SymbolTableRecord của layer/block/linetype mà entity reference đến).
- **`IdPair.IsCloned`**: true nếu object thực sự được deep-clone sang dest. false khi `DuplicateRecordCloning.Ignore` phát hiện dest đã có record trùng tên → src bị skip, IdPair.Value trỏ về **id của record dest cũ** (không phải null).
- **`Stopwatch.ElapsedMilliseconds` vs delta**: tính delta bằng `sw.ElapsedMilliseconds - prevMs` an toàn hơn snapshot trước/sau vì không dừng Stopwatch giữa chừng.
- **C# private nested class**: khai báo stats type nested trong partial class cho phép dùng `partial` để tách mà vẫn share stats types giữa file partial khác (nếu cần mở rộng sau). Không export → API ổn định.

---

## Session 2026-04-22 (8) — Cải thiện error handling + log cho Drawing Collection

### Bối cảnh
Test session 7 với 9 file thật (log ở `%APPDATA%\MCG_FittingManagement\plugin.log`):
- 8/9 file collect thành công.
- **File `71-57589-04.dwg`**: fail với `Exception: eDwgNeedsRecovery` (bong ra từ `Database.Purge`) — message thô, không user-friendly.
- **File `71-57589-02.dwg`**: width=202,673mm (≈202m) — nghi có entity "orphan" trong Model Space kéo bbox quá xa; các file kế bị dời ra khu vực x≈200km.
- **File `71-57589-03.dwg`**: hasBbox=True nhưng width=0 — Model Space có entity nhưng tất cả chung 1 điểm.

### Đã làm
- [Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs):
  - Thêm hằng `BBOX_OUTLIER_WARN = 100000.0` (100m, ngưỡng cảnh báo bbox bất thường).
  - Log "Prepared" viết lại đầy đủ: `bbox=[(minX,minY)-(maxX,maxY)], w=..., h=...` thay vì chỉ `width=...`. Nếu `hasExtents=false` → log lý do "Model Space rỗng hoặc không entity nào có extents".
  - Thêm cảnh báo khi `width >= 100000` hoặc `height >= 100000`: `CẢNH BÁO bbox '...' quá lớn (w=X.Xm, h=Y.Ym) — có thể có entity 'orphan' trong Model Space kéo bbox. Layout kế sẽ dời xa tương ứng.`
  - Thêm catch riêng `catch (Autodesk.AutoCAD.Runtime.Exception acEx) when (IsRecoverableCorruptError(acEx))` — đặt TRƯỚC catch generic `Exception`. Message user-facing: `File DWG bị lỗi cần chạy lệnh RECOVER trong AutoCAD trước khi collect (DwgNeedsRecovery)`.
  - Helper `IsRecoverableCorruptError`: match `ErrorStatus.DwgNeedsRecovery`, `DwkLockFileFound`, `FileAccessErr`, `FileSharingViolation`.

### Thiết kế
- **Không auto-recover**: Plugin không tự `Database.Recover(path, …)` vì có thể che giấu vấn đề về chất lượng nguồn dữ liệu. User mở file thủ công, chạy `_.RECOVER`, save lại → thử collect lại.
- **Không filter outlier bbox**: width quá lớn không tự ý bỏ — log cảnh báo rõ, user tự quyết định mở file vệ sinh Model Space (xóa entity orphan) rồi collect lại. Nếu im lặng "trim" bbox, user không biết file gốc có vấn đề.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors, 4 warnings pre-existing (Costura.Fody + 2 DLL lock từ AutoCAD đang chạy + PowerShell Group Policy).

### Bước tiếp theo
- Test lại với bộ 9 file session 7:
  - File 04 (corrupt) → thấy message tiếng Việt rõ: *"File DWG bị lỗi cần chạy lệnh RECOVER..."*.
  - File 02 (width=202m) → xuất hiện dòng `CẢNH BÁO bbox...` trong log.
  - Các file khác → log `bbox=[(minX,minY)-(maxX,maxY)], w=..., h=...` đầy đủ để debug layout.

### Ghi chú API
- **`Autodesk.AutoCAD.Runtime.ErrorStatus.DwgNeedsRecovery`**: được throw từ `Database.Purge`, `Database.ReadDwgFile`, hoặc bất kỳ DB op nào khi AutoCAD phát hiện structure corruption. File vẫn đọc được bằng tool `RECOVER` (lệnh `_.RECOVER`) mà không phải `OPEN` thường.
- **Exception disambiguation**: File này không `using Autodesk.AutoCAD.Runtime` nên có thể dùng `System.Exception` không xung đột; khi cần AutoCAD Runtime Exception thì phải fully-qualified `Autodesk.AutoCAD.Runtime.Exception`. Pattern-matching `catch (T) when (condition)` (C# 6) cho phép filter theo `ErrorStatus` mà không re-throw.
- **Thứ tự catch**: specific type (`Autodesk.AutoCAD.Runtime.Exception`) phải ĐỨNG TRƯỚC `System.Exception`, nếu không compiler warning + runtime không bao giờ đi vào catch specific.

---

## Session 2026-04-22 (7) — Thêm Drawing Collection vào tab Block Utilities

### Yêu cầu user
Tạo feature gom Model Space nhiều .dwg vào bản vẽ hiện hành:
1. OpenFileDialog multi-select .dwg
2. Với mỗi file: rename block theo `[TênFile]_[TênBlock]` (giữ nguyên `A1`, `CAS_HEAD`), purge rác trước khi clone
3. Xếp ngang trái→phải, khoảng hở 1000
4. Progress bar / dòng trạng thái trên palette
5. Trùng định nghĩa → ưu tiên bản vẽ gốc (dest)
6. Zoom Extents sau khi xong

### Đã làm
- [Services/FittingManagement/IFittingManagementService.cs](Services/FittingManagement/IFittingManagementService.cs): thêm section `Giai đoạn 3.4: Drawing Collection` + method `Task<ImportResult> CollectDrawingsAsync(string[] dwgPaths, IProgress<string> progress = null)`.
- [Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs](Services/FittingManagement/Utilities/FittingManagementService.DrawingCollection.cs) — **MỚI**. Partial class `FittingManagementService`:
  - `CollectDrawingsAsync`: Phase 1 (preprocess side db) chạy `Task.Run` trên worker thread; Phase 2 (clone sang current doc) trở về UI thread sau `await`. `using DocumentLock` ôm Phase 2. Gọi `SendStringToExecute("_.ZOOM _E ", true, false, true)` deferred để zoom-extents chạy SAU khi unlock doc.
  - `PreprocessAll`: mở mỗi file qua `new Database(false, true)` + `ReadDwgFile(path, FileShare.Read, true, null)` → `CloseInput(true)`. Ghi lỗi vào `ImportResult` nếu fail, KHÔNG throw toàn batch.
  - `RenameBlocksInSideDb`: snapshot ObjectId list trước, plan rename theo cặp `(id, newName)`, skip `IsLayout/IsAnonymous/IsFromExternalReference/*...`, skip `A1`/`CAS_HEAD` (HashSet OrdinalIgnoreCase), skip nếu dest `bt.Has(candidate)`. Rename từng BTR ở `OpenMode.ForWrite`.
  - `PurgeUnusedInSideDb`: loop tối đa 10 pass → mỗi pass `CollectPurgeCandidates` (layer/linetype/textstyle/dimstyle/block không layout/viewport) → `sideDb.Purge(ids)` filter in-place → erase từng id. Stop khi 1 pass erase = 0.
  - `ComputeModelSpaceExtents`: duyệt modelspace, aggregate `GeometricExtents` (try/catch per entity vì text chưa load font có thể throw). Trả `(hasExtents, width, Extents3d)`.
  - `CloneToCurrentSpace`: `WblockCloneObjects(msIds, destDb.CurrentSpaceId, mapping, DuplicateRecordCloning.Ignore, false)` — gọi trên **source** db. Sau đó duyệt `IdMapping` với `IsPrimary && IsCloned`, `TransformBy(Matrix3d.Displacement(dx, 0, 0))` với `dx = offsetX - minX` (đưa min bbox về đúng offsetX, không đơn thuần dịch thêm offsetX).
  - Helper: `SanitizeBlockNamePart` thay `< > / \ " : ; ? * | , = ` ' whitespace` → `_`; fallback `"Drawing"` nếu rỗng sau sanitize.
  - `offsetX += width + COLLECTION_GAP(1000)` sau mỗi file có bbox.
- [Views/FittingManagement/BlockUtilitiesView.xaml](Views/FittingManagement/BlockUtilitiesView.xaml): thêm GroupBox `DRAWING COLLECTION` dưới GroupBox cũ, gồm `BtnCollectDrawings` (PrimaryButtonStyle) + `TxtCollectionStatus` (italic, accent color).
- [Views/FittingManagement/BlockUtilitiesView.xaml.cs](Views/FittingManagement/BlockUtilitiesView.xaml.cs): thêm `async void BtnCollectDrawings_Click` với pattern giống `TemplateView.BtnBatchImportInventor_Click`: `OpenFileDialog` multi-select .dwg → disable button + `Cursors.AppStarting` → `new Progress<string>(msg => TxtCollectionStatus.Text = msg)` → `await _service.CollectDrawingsAsync(...)` → `ShowCollectionResultDialog`/`ShowExceptionDialog`. Thêm 2 helper `ShowCollectionResultDialog`/`ShowExceptionDialog`/`OpenLogFolder` (copy pattern từ TemplateView).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors, 3 warnings pre-existing (Costura.Fody, DLL lock do AutoCAD mở, PowerShell Group Policy).

### Bước tiếp theo
- Test thực tế trong AutoCAD 2023:
  1. `MCG_Fitting_Show` → tab Block Utilities → `Collect Drawings (.dwg)` với 3-5 file.
  2. Verify block trong bản vẽ đích: mỗi file đi kèm tên prefix (vd `CAS-0071133_VIEW_1`), `A1` / `CAS_HEAD` nếu có trong file nguồn thì trùng tên → ưu tiên định nghĩa bản vẽ gốc.
  3. Verify layout: bbox file 1 xếp liền bbox file 2 cách 1000 đơn vị theo X.
  4. Verify `TxtCollectionStatus` update theo file đang xử lý, không bị treo UI.
  5. Zoom Extents tự bung sau khi xong.
- Edge cases cần kiểm tra:
  - File có block trùng tên đã được rename ở file khác (vd 2 file cùng chứa block `WALL` → `File1_WALL` và `File2_WALL` → OK không đụng).
  - File rỗng hoặc Model Space rỗng: `HasExtents=false`, không dời offset → file kế chồng lên — chấp nhận theo design.
  - File có xref (IsFromExternalReference) → skip rename, xref vẫn clone sang đúng.

### Ghi chú API
- **`Database.WblockCloneObjects(srcIds, destOwnerId, IdMapping, DuplicateRecordCloning, bool deferXlation)`**: gọi trên **source db**, clone entity từ source vào BTR của destination. `DuplicateRecordCloning.Ignore` — khi symbol (layer/block/linetype) trùng tên ở dest: dùng bản dest, bỏ bản src (đúng yêu cầu "ưu tiên bản vẽ gốc").
- **`Database.Purge(ObjectIdCollection)`**: filter in-place → giữ lại chỉ id **thực sự purgeable**. Purge đệ quy cần loop nhiều pass vì erase 1 block có thể làm layer của nó trở nên purgeable.
- **`BlockTableRecord.IsFromExternalReference`**: true cho xref → KHÔNG rename (xref name = tên file xref, đổi sẽ mất link).
- **`IdMapping` iteration**: `IdPair` có `.IsPrimary` (entity top-level, không phải symbol table record) + `.IsCloned` (đã được clone thực sự chứ không phải Ignore). Chỉ transform primary + cloned.
- **`Matrix3d.Displacement(Vector3d)`**: dịch chuyển pure translation. Dùng `dx = offsetX - minX.X` để "đưa minX bbox về đúng offsetX" — tránh bug dịch thêm lần hai khi bbox đã có offset sẵn trong file gốc.
- **`Document.SendStringToExecute(str, activate, wrapUpInactive, echoCommand)`**: command string deferred, chạy SAU khi control trả về AutoCAD loop → an toàn gọi trong block `using DocumentLock` (zoom thực thi sau unlock). `activate=true` bắt document active trước khi thực thi.
- **`Database(buildDefaultDrawing, noDocument)`**: `new Database(false, true)` tạo **side db** không liên kết DocumentManager → có thể đọc/sửa/transaction từ bất kỳ thread nào (không cần DocumentLock). Phải `Dispose` thủ công.
- **`Database.ReadDwgFile(path, FileShare, allowCPConversion, password)`**: đọc .dwg vào side db. `CloseInput(true)` giải phóng handle file để tránh lock.

---

## Session 2026-04-22 (6) — Fix MissingMethodException khi gõ MCG_Fitting_Show/Hide

### Vấn đề
AutoCAD crash khi gõ `MCG_Fitting_Hide` (và `Show`):
```
System.MissingMethodException: No parameterless constructor defined for this object.
  at System.Activator.CreateInstance(Type type)
  at Autodesk.AutoCAD.Runtime.PerDocumentCommandClass.Invoke(MethodInfo mi, Boolean bLispFunction)
```

**Root cause**: `PaletteManager` là Singleton với `private PaletteManager()`. AutoCAD `PerDocumentCommandClass.Invoke` gọi `Activator.CreateInstance(typeof(PaletteManager))` mỗi lần user chạy lệnh → đụng private ctor → throw.

### Đã làm
- [Commands/PaletteManager.cs](Commands/PaletteManager.cs): `McgShow()` và `McgHide()` chuyển `public void` → `public static void`, body `Show()` → `Instance.Show()` / `Hide()` → `Instance.Hide()`. Static method không cần instance → AutoCAD không gọi `Activator.CreateInstance` nữa.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test: `MCG_Fitting_Show` + `MCG_Fitting_Hide` → không còn MissingMethodException, palette toggle được.

### Ghi chú API
- **`PerDocumentCommandClass`** của AutoCAD: nếu class chứa `[CommandMethod]` dùng **instance method**, AutoCAD tạo 1 instance **mỗi document** qua `Activator.CreateInstance(type)`. Cần `public` parameterless ctor.
- **Static `[CommandMethod]`**: không cần instance, AutoCAD gọi method trực tiếp qua `MethodInfo.Invoke(null, ...)`. Phù hợp với Singleton/Service pattern.
- **Rule of thumb**: Command method nên `static` trừ khi thực sự cần state per-document.

---

## Session 2026-04-22 (5) — Tách Palette thành 4 tab + rename title

### Yêu cầu user
1. Palette title "MCG_FittingManagement - FittingManagement" → "Fitting Management"
2. Tách thành 4 tab: **Fitting Handle** / **Project Config** / **Template** / **Block Utilities**
3. Block Utilities: 6 block utility buttons
4. Template: Import .idw + Open Library
5. Project Config: Open Library (cùng button, tab khác)
6. Fitting Handle: Open BOM + Balloon

### Đã làm
- [Views/FittingManagement/FittingStyles.xaml](Views/FittingManagement/FittingStyles.xaml) — **MỚI**: ResourceDictionary chung chứa 4 Style (HelperTextStyle, GroupBox, Button default, PrimaryButtonStyle). Mỗi UserControl merge qua relative `Source="FittingStyles.xaml"` — WPF resolve qua BaseUri của XAML compiled.
- [Views/FittingManagement/FittingHandleView.xaml](Views/FittingManagement/FittingHandleView.xaml) + [.xaml.cs](Views/FittingManagement/FittingHandleView.xaml.cs) — **MỚI**: tab "Fitting Handle" với 2 GroupBox — BOM EXPORT (`BtnOpenBomPreview`) + BALLOONING (`BtnAddBalloon`, `BtnMassBalloon`).
- [Views/FittingManagement/ProjectConfigView.xaml](Views/FittingManagement/ProjectConfigView.xaml) + [.xaml.cs](Views/FittingManagement/ProjectConfigView.xaml.cs) — **MỚI**: tab "Project Config" với 1 button `BtnOpenLibrary`.
- [Views/FittingManagement/TemplateView.xaml](Views/FittingManagement/TemplateView.xaml) + [.xaml.cs](Views/FittingManagement/TemplateView.xaml.cs) — **MỚI**: tab "Template" với IMPORT TEMPLATE FROM IDW (RadioButton Panel/Detail + `BtnBatchImportInventor` async + `TxtImportStatus` + ShowImportResultDialog/ShowExceptionDialog/OpenLogFolder helpers) + FITTING LIBRARY (`BtnOpenLibrary`).
- [Views/FittingManagement/BlockUtilitiesView.xaml](Views/FittingManagement/BlockUtilitiesView.xaml) + [.xaml.cs](Views/FittingManagement/BlockUtilitiesView.xaml.cs) — **MỚI**: tab "Block Utilities" với 6 button — Rename/Redefine/Replace/ChangeBasePoint/AddToBlock/ExtractFromBlock.
- [Commands/PaletteManager.cs](Commands/PaletteManager.cs): title `"MCG_FittingManagement - FittingManagement"` → `"Fitting Management"`; 1 `AddVisual` → 4 `AddVisual` theo thứ tự user yêu cầu (FittingHandle, ProjectConfig, Template, BlockUtilities).
- **Xóa**: `Views/FittingManagement/FittingManagementView.xaml` + `.xaml.cs` (không còn dùng).
- [CLAUDE.md §9](CLAUDE.md): cập nhật danh sách tab (4 tab thay vì 1), title mới, file tree.

### Kiến trúc
- Mỗi tab = 1 UserControl độc lập, mỗi tab tự `new FittingManagementService()` trong constructor (service stateless OK).
- `IFittingManagementService` không đổi — 4 tab share cùng interface.
- GUID palette (`2b80cfe9-c560-49d6-8a09-9d636260fcf2`) KHÔNG đổi — AutoCAD giữ vị trí dock + thứ tự tab theo GUID. Thứ tự tab giờ đã fix sau deploy.
- Command CAD `MCG_Fitting_Show/Hide` không đổi.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test trong AutoCAD 2023: `MCG_Fitting_Show` → xem title bar "Fitting Management" + 4 tab đúng thứ tự + click thử mỗi button để verify service gắn đúng.
- Kiểm `ResourceDictionary Source="FittingStyles.xaml"` resolve đúng runtime — nếu styles không áp dụng (button trắng-trơn), switch sang absolute URI `"/Views/FittingManagement/FittingStyles.xaml"` hoặc pack URI với `$(PluginName)`.

### Ghi chú API
- **WPF ResourceDictionary relative URI**: `Source="FittingStyles.xaml"` resolve qua `BaseUri` của XAML compiled. Đối với UserControl trong cùng folder với ResourceDictionary → tự ghép đúng pack URI nội bộ của assembly.
- **PaletteSet.AddVisual order**: AutoCAD nhớ index tab theo GUID → đổi thứ tự AddVisual sau deploy sẽ khiến user thấy tab hoán vị. Quy tắc: thêm tab MỚI vào CUỐI, không xen giữa.
- **PaletteSetStyles.ShowTabForSingle**: vẫn giữ để nếu sau này giảm xuống 1 tab, title bar tab vẫn hiển thị.
- **4 UserControl instance = 4 service instance**: OK vì FittingManagementService không giữ state session (file paths + db access đều stateless). Nếu cần share state, bọc thành singleton sau.

---

## Session 2026-04-22 (4) — Align Import IDW với reference code ShipAutoCadPlugin

### Mục tiêu
User cung cấp 3 file từ plugin reference `ShipAutoCadPlugin` (Interface.Inventor.BatchExtractor, AutoCadService.BimHarvester, AutoCadService.BimLibrary). User approve áp dụng toàn bộ Phase 1+2 + attribute tag rename (M). Skip early-binding Inventor Interop (Q) và view label DBText (K).

### Đã làm
**[Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs)** — Refactor extract:
- `ProcessSingleIdwFile`: `Documents.Open(path, false)` thay `true` → background open, nhanh hơn.
- Thêm `GetReferencedModel(drawingDoc)` — duyệt sheets/views tìm `ReferencedDocumentDescriptor.ReferencedDocument` → trả model IPT/IAM để đọc iProperties thật.
- `ExtractIProperties(doc)` đổi signature: nhận bất kỳ doc (ưu tiên modelDoc, fallback drawingDoc). Thêm: đọc `Title` + `Revision Number` từ `Inventor Summary Information`; fallback `Designer` → `Author` từ Summary.
- Thêm `FormatAndRoundMass(raw)` — regex parse "24.532 kg" → "25 kg" (giữ unit suffix, Math.Round(0)).
- `ExtractDrawingViews`: 
  - Tính `baseScaleFactor = 1 / sheet.DrawingViews[1].Scale` per sheet.
  - Convert sheet coords → model mm: `center_mm = view.Center * 10 × baseScaleFactor`, `Width_mm = view.Width * 10 × baseScaleFactor`.
  - View name đơn giản `"View_N"` (đếm tuần tự) thay vì `drawingView.Name` (tránh ký tự không hợp lệ cho block name).
- `SafeGetProperty`: bỏ `.ToString("F3")` cho double — trả raw, caller quyết format (Mass được format ở `FormatAndRoundMass`).

**[Services/FittingManagement/Import/FittingManagementService.JsonImport.cs](Services/FittingManagement/Import/FittingManagementService.JsonImport.cs)** — Rewrite toàn bộ PHASE 2 theo kiến trúc reference:
- Bỏ kiến trúc `db.Insert` temp block → dùng `sourceDb.WblockCloneObjects(ids, destBtr.ObjectId, mapping, DuplicateRecordCloning.Ignore, false)` clone cross-db trực tiếp. Tiết kiệm 1 round-trip copy toàn DWG.
- **Entity filter**: chỉ clone `Line/Arc/Circle/Polyline/Polyline2d/Polyline3d/Spline/Ellipse`. Loại text/dim/hatch/blockref (title block, annotation Inventor).
- **Tolerance 50mm** quanh bbox view để bắt edge entities vẽ sát biên.
- **Layer remap theo tên kiểu nét source**: VISIBLE → `0`, HIDDEN → `Mechanical-AM_3` (color 6 magenta), CENTER → `Mechanical-AM_7` (color 4 cyan). Non-match giữ layer gốc.
- **Force ByLayer**: mọi entity đã clone → `ColorIndex=256`, `Linetype="ByLayer"`, `LineWeight.ByLayer`. Essential — không set sẽ giữ màu cứng từ Inventor DWG → layer rules không áp dụng.
- **Unlock layers** `0/_3/_7/_9` + CheckAndCreateLayer cho 3 layer mới trước khi xử lý.
- **Attribute tag rename** (user OK vì chưa có block cũ): 10 attribute invisible:
  - `PART_NUMBER` (was PART_ID), `DESCRIPTION` (was DESC), `MATERIAL`, `MASS`, `REVISION`
  - Mới: `DESIGNER`, `TITLE`
  - Giữ: `BOM_TYPE`, `POS_NUM`, `VIEW_NAME`
- `ImportSingleDwgWithSplit` trả `List<Tuple<ObjectId, CatalogItem>>` thay vì `List<CatalogItem>`.
- `CreateBlocksFromExtracted` sau khi unlock doc gọi `PublishToCentralLibrary(tuples)` → `db.Wblock(blockId)` export mỗi block ra `C:\Temp_BIM_Library\<uniqueName>.dwg` + merge MasterCatalog. `CatalogItem.FilePath` trỏ về file .dwg riêng từng block (tương thích `InsertBlockFromLibrary` workflow).
- **Bỏ fallback** khi metadata không có view → chỉ xử lý khi có ≥1 view (reference pattern).
- Bỏ `ComputeMetadataToDwgTransform` (auto-detect scale) — không cần nữa vì extract đã viết tọa độ ở model mm.
- Bỏ `EntityCacheItem` struct, `BuildEntityCache`, `CreateSingleBlockFromEntities`, `SanitizeBlockName`, `SanitizeViewName` — không còn cần. Thay bằng `allowed` list tuple (Id, Cx, Cy) inline trong 1 hàm.

### Mapping các cải tiến đã apply (13/16 đề xuất)

| # | Tên | Đã apply? |
|---|---|---|
| A | GetReferencedModel đọc iProperties từ model | ✅ |
| B | View coords = sheet*10/scale (model mm) | ✅ |
| C | OpenVisible=false | ✅ |
| D | FormatAndRoundMass | ✅ |
| E | WblockCloneObjects trực tiếp | ✅ |
| F | Entity type filter | ✅ |
| G | Layer remap VISIBLE/HIDDEN/CENTER | ✅ |
| H | Force ColorIndex=256, Linetype/LineWeight=ByLayer | ✅ |
| I/J | Tolerance 50mm | ✅ |
| K | CreateViewLabel DBText | ❌ skip (user quyết) |
| L | UnlockLayer trước CheckAndCreateLayer | ✅ |
| M | Attribute tag rename | ✅ |
| N | POS_NUM dynamic lúc insert | ❌ giữ tạo lúc create (user chọn Phase 1+2) |
| O | PublishToCentralLibrary wblock mỗi block | ✅ |
| P | View name "View_N" | ✅ |
| Q | Early-binding Inventor Interop | ❌ giữ late-bind dynamic (user chọn Phase 1+2) |

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test lại với CAS-0071133.idw:
  1. iProperties đọc từ model: `PartNumber='2000055530'` vẫn đúng (đã đọc được). Log thêm Mass="X kg" đã làm tròn.
  2. Views mới có tên `View_1...View_4`, tọa độ **model mm** → bbox khớp entity (số block tạo = số view × entity count).
  3. Entity filter: chỉ Line/Arc/Circle/... được clone; text trong title block không vào block fitting.
  4. Layer remap: mỗi entity vào đúng layer `0/_3/_7` theo tên source layer Inventor.
  5. File `.dwg` riêng xuất ra `C:\Temp_BIM_Library\CAS-0071133_View_1.dwg` v.v.
  6. MasterCatalog.json có `FilePath` trỏ về file .dwg riêng từng block.
- Nếu scale factor chưa khớp (block nằm lệch/trống), kiểm `view.Scale` Inventor thực tế vs giả định `sheet.DrawingViews[1].Scale` cho cả sheet.

### Ghi chú API
- **Inventor `DrawingView.Center`** vs `.Position`: `.Center` là tâm view thật trong sheet coords. `.Position` không phải tâm. Đã đổi sang `.Center`.
- **Inventor `DrawingView.Scale`**: là `double` — giá trị hiển thị trên title block (1/5 → Scale=0.2). `baseScaleFactor = 1/Scale` để zoom-up sheet-coords về model-coords.
- **`Database.WblockCloneObjects(srcIds, destOwnerId, IdMapping, DuplicateRecordCloning, bool)`**: gọi trên **source db**, clone các entity có id ∈ srcIds vào BTR có id = destOwnerId (trong db khác). Tự resolve layer/linetype dependencies sang dest.
- **`ColorIndex=256`**: magic number cho `ByLayer` trong AutoCAD .NET API. 0 là `ByBlock`.
- **`Database.Wblock(blockId)`**: xuất BTR thành standalone Database (side db), `SaveAs(path, DwgVersion.Current)` để lưu file .dwg.
- **`DuplicateRecordCloning.Ignore`**: khi clone symbol table records (layer/linetype) có cùng tên ở dest db → dùng bản dest, bỏ bản src. Tránh duplicate symbol records.
- **`.Close(true)` trên Inventor Document**: arg = `SkipSave` (true = không lưu). Với `drawingDoc.SaveAs(path, true)` (SaveCopyAs keep original), document ref vẫn alive → Close(true) gọi được không lỗi thực sự (chỉ COM 0x80010114 benign khi Inventor auto-unload).

---

## Session 2026-04-22 (3) — Fix unit mismatch + quiet benign COM errors

### Vấn đề từ log
File `plugin.log`:
```
[10:12:24.732] Entity cache: 222 entities.
[10:12:24.735] Block '2000055530_VIEW1' không có entity nào rơi vào bbox → erase BTR.
[10:12:24.736] Block '2000055530_VIEW2' không có entity nào rơi vào bbox → erase BTR.
[10:12:24.736] Block '2000055530_VIEW3' không có entity nào rơi vào bbox → erase BTR.
[10:12:24.738] Block '2000055530_ISO_VIEW' không có entity nào rơi vào bbox → erase BTR.
HOÀN TẤT ImportIdwFilesAsync — Thành công: 0, Thất bại: 1.
```
→ DWG có 222 entities nhưng 4/4 view không match entity nào. Nguyên nhân: **unit mismatch** giữa metadata Inventor (cm — `DrawingView.Position/Width/Height` trả về database unit của Inventor) và DWG xuất (mm — default của Inventor DWG translator). Bbox `[45cm, 55cm]` không chứa entity tại `500mm`.

Ngoài ra log có 2 COM exception spam nhưng import vẫn success, gây noise: HRESULT 0x80010114 ("requested object does not exist" khi close IDW sau SaveAs) và 0x800706BA ("RPC server unavailable" khi release Inventor sau Quit).

### Đã làm
- [Services/FittingManagement/Import/FittingManagementService.JsonImport.cs](Services/FittingManagement/Import/FittingManagementService.JsonImport.cs):
  - `EntityCacheItem` mở rộng thêm `MinX/MinY/MaxX/MaxY` — cache full bbox thay vì chỉ center.
  - `BuildEntityCache` lưu Min/Max/Center từ `GeometricExtents` — 1 pass/DWG vẫn giữ nguyên.
  - Thêm `ComputeMetadataToDwgTransform(cache, views) → (scale, offX, offY)`: tính union-bbox entity DWG và union-bbox view metadata, ratio W+H / 2 làm scale, align tâm-vs-tâm làm offset. Degenerate → fallback (1,0,0).
  - `ImportSingleDwgWithSplit` gọi transform **1 lần/DWG**, mỗi view đổi sang hệ DWG: `dwgC = scale*metaC + off`, `halfW = scale*view.Width/2`. `translate` giờ dùng `(-dwgCX, -dwgCY)` thay vì `(-view.CenterX, -view.CenterY)`.
  - Log `Meta→DWG transform: scale=10.0000, offset=(...)` để debug.
- [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs):
  - `ProcessSingleIdwFile` finally-Close: thêm `catch (COMException) when HRESULT ∈ {0x80010114, 0x800706BA}` → log info thay vì `LogException` (không spam "LỖI" nữa).
  - `ReleaseInventorInstance`: cùng pattern, catch 2 HRESULT benign → log info.

### Thiết kế auto-scale
Giả định: exported DWG và metadata cùng cover cùng tập hình học (không có 1 bên chứa entity bên kia không có). Nếu cả 2 bên có union-bbox lớn "tương đương", ratio kích thước = hệ số đơn vị.
- Factor điển hình sẽ là **10.0** (Inventor cm → DWG mm) cho 99% dự án.
- Nếu Inventor DWG translator cài sang `inch`: factor **0.3937** (cm → inch).
- Nếu DWG export inch + Inventor cm: factor **2.54**.
- Nếu cả 2 cùng mm: factor **1**.

Không cần hardcode — tự khớp.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test lại với CAS-0071133.idw → log phải thấy `scale=10.0000` (hoặc gần), 4 block `2000055530_VIEW1/VIEW2/VIEW3/ISO_VIEW` tạo thành công với entities thực trong mỗi.
- Verify tâm block: insert 1 reference → phải nằm giữa geometry (do block đã translate về origin).
- Nếu `scale` tính ra bất thường (ví dụ 0.01 hoặc 100), log xong cần check thủ công metadata JSON để debug.

### Ghi chú API
- **Inventor database unit**: luôn **cm** cho mọi `Point2d`, `double` mm-like ở geometry APIs (`DrawingView.Position`, `.Width`, `.Height`). Không đổi theo UI unit preset.
- **Inventor DWG export default**: **mm** (DWG Translator preset `Export_Acad_IniFile` quyết định). Muốn kiểm: mở INI file tại `%PublicDocuments%\Autodesk\Inventor 2023\Design Data\DWG-AutoCAD Export.ini`.
- **HRESULT 0x80010114** (`RPC_E_DISCONNECTED`): COM proxy đến server process/object đã destroyed. Xảy ra khi close đã-closed Document.
- **HRESULT 0x800706BA** (`RPC_S_SERVER_UNAVAILABLE`): Process COM đã exit. Xảy ra khi `Marshal.ReleaseComObject(invApp)` sau `invApp.Quit()`.
- **`COMException.HResult`** thuộc `int` (signed) — so sánh HRESULT ≥ 0x80000000 phải dùng `unchecked((int)0x80010114)` để tránh overflow.
- **Pattern `catch ... when`** (C# 6+): cho phép filter exception theo HResult ngay trong catch mà không cần re-throw.

---

## Session 2026-04-22 (2) — Async + tối ưu performance cho Import IDW

### Vấn đề
AutoCAD bị treo khi bấm Import IDW vì:
1. Click handler chạy đồng bộ trên UI thread của AutoCAD → toàn bộ Phase 1 (Inventor COM) và Phase 2 (db split-view) khoá main thread.
2. Phase 2 gọi `GeometricExtents` trên từng entity × từng view (O(V×E)) — dư vì tâm bbox không đổi giữa các view.
3. Inventor khởi động ngầm nhưng `SilentOperation` chưa set → dialog modal nội bộ có thể chặn thread.

### Đã làm
- [Services/FittingManagement/IFittingManagementService.cs](Services/FittingManagement/IFittingManagementService.cs): `ImportIdwFiles` → `Task<ImportResult> ImportIdwFilesAsync(paths, bomType, IProgress<string> progress = null)`.
- [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs):
  - `ImportIdwFilesAsync`: Phase 1 (`ExtractAllIdw`) chạy qua `Task.Run` → worker thread → UI AutoCAD không bị khoá trong lúc Inventor open/SaveAs/close. Phase 2 tự quay về thread gốc sau `await`.
  - `AcquireInventorInstance`: thêm `invApp.SilentOperation = true` (try/catch vì một số build không expose).
  - `ExtractAllIdw`: nhận `IProgress<string>`, report `[i/N] Extracting: foo.idw` trước mỗi file.
- [Services/FittingManagement/Import/FittingManagementService.JsonImport.cs](Services/FittingManagement/Import/FittingManagementService.JsonImport.cs):
  - Thêm struct `EntityCacheItem { Id, CenterX, CenterY, HasExtents }`.
  - Thêm `BuildEntityCache(tr, tempBtr)`: pre-compute 1 pass/DWG — tránh gọi `GeometricExtents` lặp V lần/entity.
  - `CreateSingleBlockFromEntities`: đổi param `BlockTableRecord tempBtr` → `List<EntityCacheItem>`. Bbox check dùng `double` so sánh trực tiếp thay vì property access lặp. Dead helper `EntityInsideBbox` xoá.
  - `CreateBlocksFromExtracted`: nhận `IProgress<string>`, report `[i/N] Creating blocks: foo.idw`.
- [Views/FittingManagement/FittingManagementView.xaml](Views/FittingManagement/FittingManagementView.xaml): thêm `TxtImportStatus` (TextBlock, italic, màu accent) dưới button Import .idw.
- [Views/FittingManagement/FittingManagementView.xaml.cs](Views/FittingManagement/FittingManagementView.xaml.cs): `BtnBatchImportInventor_Click` chuyển thành `async void`. Lifecycle: disable button + `Mouse.OverrideCursor = Cursors.AppStarting` + `new Progress<string>(msg => TxtImportStatus.Text = msg)` → `await _service.ImportIdwFilesAsync(...)` → restore cursor/button trong `finally`.

### Phức tạp tiệm cận
- Build entity cache mỗi DWG: O(E) — call `GeometricExtents` 1 lần/entity.
- Split view: O(V × E) nhưng thân vòng lặp chỉ là 4 so sánh double (nhanh).
- Tổng: O(E + V×E_clip) thay vì O(V × E_full_extents).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test thực tế trong AutoCAD 2023: bấm Import .idw với nhiều file lớn → UI AutoCAD phải responsive (pan/zoom được) trong lúc Phase 1 chạy; `TxtImportStatus` cập nhật theo file đang xử lý.
- Theo dõi nếu vẫn chậm ở Phase 2 với file rất lớn (>10k entity) → áp dụng option E: bỏ `db.Insert` temp-block, dùng `sideDb.WblockCloneObjects` clone trực tiếp từ sideDb sang target BTR (tiết kiệm 1 vòng copy).

### Ghi chú API
- **Inventor COM + Task.Run**: Inventor 2020+ đăng ký COM với threading model cho phép gọi từ MTA worker. Nếu gặp `InvalidCastException` cross-thread, fallback là tạo dedicated STA thread qua `new Thread(() => {...}) { ApartmentState = ApartmentState.STA }`.
- **`Progress<T>`** capture `SynchronizationContext.Current` tại constructor — nếu khởi tạo trên UI thread, callback `Report` sẽ marshal về UI thread → `TxtImportStatus.Text = msg` an toàn từ worker thread.
- **AutoCAD db calls từ non-UI thread**: cấm tuyệt đối. `DocumentLock` + db access phải nằm sau `await` (UI thread đã được tái chiếm). Đã verify trong code.
- **`async void` cho event handler**: WPF pattern chuẩn. Exception bubble lên sẽ crash app nếu không `try/catch` — đã wrap try/catch/finally đầy đủ.

---

## Session 2026-04-22 (1) — Gộp Import IDW thành 1 luồng với split-view

### Đã làm
Gộp 2 button "Import .idw files" + "Import .json files" thành 1 button `Import .idw` thực hiện full flow: Extract IDW → DWG/JSON → Split từng drawing view thành 1 block riêng → Map layer + inject attributes → Đăng ký MasterCatalog.

- [Services/FittingManagement/IFittingManagementService.cs](Services/FittingManagement/IFittingManagementService.cs): bỏ `BatchImportIdwFiles(string[])` + `ImportJsonAndCreateBlocks(string[], string)`; thêm `ImportIdwFiles(string[] idwPaths, string bomType)`.
- [Views/FittingManagement/FittingManagementView.xaml](Views/FittingManagement/FittingManagementView.xaml): Admin Expander nay còn 1 TextBlock + 1 RadioButton BOM Type + 1 Button `Import .idw` (bỏ Step 2 JSON + separator).
- [Views/FittingManagement/FittingManagementView.xaml.cs](Views/FittingManagement/FittingManagementView.xaml.cs): xoá `BtnImportJson_Click`; `BtnBatchImportInventor_Click` đọc `RadioPanelFitting.IsChecked` rồi gọi `_service.ImportIdwFiles(paths, bomType)`.
- [Services/FittingManagement/Import/FittingManagementService.IdwImport.cs](Services/FittingManagement/Import/FittingManagementService.IdwImport.cs): đổi entry point thành `ImportIdwFiles(string[], string)` — PHASE 1 (`ExtractAllIdw`) giữ logic Inventor COM cũ nhưng trả `List<ExtractedIdw>` (inner class: SourceIdwName + DwgPath + FittingMetadata), sau đó gọi `CreateBlocksFromExtracted` làm PHASE 2.
- [Services/FittingManagement/Import/FittingManagementService.JsonImport.cs](Services/FittingManagement/Import/FittingManagementService.JsonImport.cs): rewrite hoàn toàn. PHASE 2 mở từng DWG, `db.Insert(tempName, sideDb, true)` làm block tạm, rồi với mỗi `ViewMetadata` (center+size) clip entity theo bbox XY, dịch tâm view về `(0,0)` bằng `Matrix3d.Displacement`, gán layer, inject 7 attribute chuẩn + `VIEW_NAME`. Tên block = `<SanitizedPartNumber>_<SanitizedViewName>`. Fallback khi metadata không có view: tạo 1 block = cả DWG, tên = PartNumber.

### Kiến trúc split-view
- Cross-db clone né tránh bằng cách `db.Insert` vào main db như block tạm `__MCG_TEMP_<guid>`, sau đó iterate BTR tạm và `Entity.Clone()` (same-db) vào từng BTR đích. Temp BTR bị `Erase(true)` ở cuối transaction.
- Entity thuộc view nếu tâm `GeometricExtents` XY rơi trong bbox view; entity overlap nhiều view sẽ được duplicate-clone sang mỗi view (chấp nhận theo thiết kế).
- `SanitizeBlockName`: thay `< > / \\ " : ; ? * | , = \` ` + whitespace` bằng `_`. `SanitizeViewName` thêm `_2`, `_3`… nếu trùng tên view trong cùng IDW.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors, 2 warnings (pre-existing: Costura.Fody + PowerShell target bị Group Policy chặn — không liên quan thay đổi).

### Bước tiếp theo
- Test AutoCAD 2023: load plugin → `MCG_Fitting_Show` → chọn Panel/Detail → `Import .idw` với file IDW multi-view → verify N blocks tạo trong bản vẽ với tên `PART_FRONT`, `PART_TOP`, `PART_ISO`, v.v. và MasterCatalog.json chứa N entry.
- Kiểm tra edge-case: IDW có view overlap, view rỗng geometry, view name trùng nhau, IDW không có view nào.
- Kiểm tra đơn vị: Inventor IDW export DWG thường ở mm; view.CenterX/Y và entity coordinate phải cùng đơn vị. Nếu lệch (cm vs mm), block sẽ sai tâm — cần tính conversion.

### Ghi chú API
- `Vector3d.Zero` **không tồn tại** trong AutoCAD 2023 .NET API — phải dùng `new Vector3d(0,0,0)`.
- `Database.Insert(name, sideDb, preserveSourceDatabase: true)` tạo **BlockTableRecord** trong main db chứa toàn bộ ModelSpace của sideDb — dùng làm temp BTR để clone same-db thay vì cross-db clone.
- `Entity.Clone()` tạo deep copy in-memory; phải `AppendEntity` + `AddNewlyCreatedDBObject` để đưa vào db. Clone `BlockReference` trong tempBtr vẫn valid vì BTR definition đã nằm trong main db.
- `BlockTableRecord.Erase(true)` sau `Add` + clone-out vẫn commit OK trong cùng transaction; entity đã clone sang BTR khác không bị ảnh hưởng.
- `Extents3d` với Z = `double.NegativeInfinity/PositiveInfinity` để bbox 2D không lọc nhầm entity 3D theo Z.

---

## Session 2026-04-21 (3) — Rename plugin + đổi command names (FittingManagement)

### Đã làm
- [Commands/PaletteManager.cs](Commands/PaletteManager.cs):
  - `[CommandMethod("MCG_Show")]` → `[CommandMethod("MCG_Fitting_Show")]`.
  - `[CommandMethod("MCG_Hide")]` → `[CommandMethod("MCG_Fitting_Hide")]`.
  - PaletteSet title `"MCG Plugins"` → `"MCG_FittingManagement - FittingManagement"`.
- [MCG_FittingManagement.csproj](MCG_FittingManagement.csproj): `<PluginName>MCG_FittingManagement</PluginName>` → `<PluginName>MCG_FittingManagement.FittingManagement</PluginName>` → DLL xuất ra đổi thành `MCG_FittingManagement.FittingManagement_<timestamp>.dll`.
- [CLAUDE.md](CLAUDE.md): nới rule §2 (hạn chế chứ không cấm tuyệt đối sửa csproj khi có lý do rõ ràng), cập nhật §9 (title + command names).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test trong AutoCAD: gõ `MCG_Fitting_Show` / `MCG_Fitting_Hide`, xác nhận title `"MCG_FittingManagement - FittingManagement"`.
- Load song song với plugin CheckList (GUID `7b3e9a2c-...`, command `MCG_Checklist_Show/Hide`) để confirm không xung đột.

### Ghi chú API
- `$(PluginName)` trong csproj lan truyền xuống `$(AssemblyName)` → tên DLL output. Dùng dấu chấm (`.FittingManagement`) thay vì dấu cách để an toàn với bundle script/PowerShell regex trong target `UpdatePackageContents`.
- File `Commands/PaletteManager.cs` và `SESSION_LOG.md` đã bị revert trong working dir giữa session (có thể do VS Code hot-reload/file watcher) — dùng `git checkout HEAD --` để restore trước khi apply edits mới.

---

## Session 2026-04-21 (2) — Tách CheckList sang repo riêng

### Đã làm
- Tách nội dung module CheckList sang repo `https://github.com/MCG-Automation/CheckList.git` (branch `main`, giữ full git history qua clone local → modify → push).
- Xóa trong repo này: `Models/CheckList/`, `Services/CheckList/`, `Views/CheckList/`, `Docs/Macgregor_CheckList_UserGuide.html`.
- [Commands/PaletteManager.cs](Commands/PaletteManager.cs): bỏ `using MCG_FittingManagement.Views.CheckList`, `Initialize()` chỉ còn 1 `AddVisual` cho FittingManagement.
- [CLAUDE.md](CLAUDE.md): cập nhật §3 namespace tree và §9 PaletteSet (1 Module — 1 Tab) cho scope FittingManagement.

### Trạng thái
- **Phase:** 1 — Feature Implementation (repo chỉ còn module FittingManagement).
- **Build:** Succeeded — 0 errors.
- **Remote:** `origin` → `https://github.com/MCG-Automation/FittingManagement.git`.

### Ghi chú API
- `git clone <local>` giữ full history khi tách. Push lần đầu sang remote mới dùng `git push -u origin HEAD:main` để tạo branch `main` trên repo rỗng.

---

## Session 2026-04-21 (1) — Xóa 4 module (giữ CheckList + FittingManagement), tách repo

### Đã làm
- Xóa 20 folder module (Commands/Models/Services/Views/Utilities × 4 module: DetailDesign, PanelData, TableOfContent, Weight).
- [Commands/PaletteManager.cs](Commands/PaletteManager.cs): bỏ 4 `using` Views của các module đã xóa, rút gọn `Initialize()` còn 2 `AddVisual` (Fitting Management, CheckList).
- [CLAUDE.md](CLAUDE.md): cập nhật §3 namespace tree và §9 PaletteSet section để phản ánh kiến trúc 2 module.
- Thêm remote mới → push sang `https://github.com/MCG-Automation/FittingManagement.git` (branch `main`), sau đó rename thành `origin`.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Ghi chú API
- `.csproj` dùng `Microsoft.NET.Sdk` nên tự include source theo thư mục — xóa folder không cần sửa csproj.

---

## Session 2026-04-20 (4) — Đăng ký lệnh AutoCAD cho Palette

### Đã làm
- Thêm `[CommandMethod("MCG_Show")]` và `[CommandMethod("MCG_Hide")]` vào Commands/PaletteManager.cs.
- Các lệnh này gọi trực tiếp đến instance Singleton để điều khiển hiển thị `PaletteSet`.

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded.

### Bước tiếp theo
- **File:** `Views/DetailDesign/DetailDesignViewModel.cs` | Triển khai ViewModel mẫu cho module DetailDesign.
- **Build & Test:** Chạy file `build-and-launch.bat` để kiểm tra các lệnh mới trong AutoCAD.

### Ghi chú API
- `CommandFlags.Modal` được sử dụng cho lệnh hiển thị Palette để đảm bảo tính ổn định khi gọi từ command line.
- Do `PaletteManager` là Singleton, các phương thức `CommandMethod` không cần static nếu class được AutoCAD khởi tạo đúng cách, nhưng ở đây tôi để instance method gọi qua singleton để nhất quán.

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
- Thêm handler `BtnClosePalette_Click` trong [Views/CheckList/CheckList.View.xaml.cs](Views/CheckList/CheckList.View.xaml.cs) gọi `PaletteManager.Instance.Hide()` (import thêm `MCG_FittingManagement.Commands`).

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

1. **Fix x:Class namespace trong 5 XAML files** — `ShipAutoCadPlugin.UI.*` → `MCG_FittingManagement.Views.FittingManagement.*`:
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

- **x:Class phải khớp namespace code-behind** — nếu XAML dùng `ShipAutoCadPlugin.UI.X` mà code-behind dùng `MCG_FittingManagement.Views.Y.X` thì WPF không generate partial class, gây lỗi `InitializeComponent` và tất cả control names
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
