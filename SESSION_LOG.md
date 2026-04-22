# SESSION_LOG.md — Tiến độ theo session

# Claude Code tự cập nhật file này. Không sửa thủ công phần log.

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
  - PaletteSet title `"MCG Plugins"` → `"MCGCadPlugin - FittingManagement"`.
- [MCGCadPlugin.csproj](MCGCadPlugin.csproj): `<PluginName>MCGCadPlugin</PluginName>` → `<PluginName>MCGCadPlugin.FittingManagement</PluginName>` → DLL xuất ra đổi thành `MCGCadPlugin.FittingManagement_<timestamp>.dll`.
- [CLAUDE.md](CLAUDE.md): nới rule §2 (hạn chế chứ không cấm tuyệt đối sửa csproj khi có lý do rõ ràng), cập nhật §9 (title + command names).

### Trạng thái
- **Phase:** 1 — Feature Implementation.
- **Build:** Succeeded — 0 errors.

### Bước tiếp theo
- Test trong AutoCAD: gõ `MCG_Fitting_Show` / `MCG_Fitting_Hide`, xác nhận title `"MCGCadPlugin - FittingManagement"`.
- Load song song với plugin CheckList (GUID `7b3e9a2c-...`, command `MCG_Checklist_Show/Hide`) để confirm không xung đột.

### Ghi chú API
- `$(PluginName)` trong csproj lan truyền xuống `$(AssemblyName)` → tên DLL output. Dùng dấu chấm (`.FittingManagement`) thay vì dấu cách để an toàn với bundle script/PowerShell regex trong target `UpdatePackageContents`.
- File `Commands/PaletteManager.cs` và `SESSION_LOG.md` đã bị revert trong working dir giữa session (có thể do VS Code hot-reload/file watcher) — dùng `git checkout HEAD --` để restore trước khi apply edits mới.

---

## Session 2026-04-21 (2) — Tách CheckList sang repo riêng

### Đã làm
- Tách nội dung module CheckList sang repo `https://github.com/MCG-Automation/CheckList.git` (branch `main`, giữ full git history qua clone local → modify → push).
- Xóa trong repo này: `Models/CheckList/`, `Services/CheckList/`, `Views/CheckList/`, `Docs/Macgregor_CheckList_UserGuide.html`.
- [Commands/PaletteManager.cs](Commands/PaletteManager.cs): bỏ `using MCGCadPlugin.Views.CheckList`, `Initialize()` chỉ còn 1 `AddVisual` cho FittingManagement.
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
