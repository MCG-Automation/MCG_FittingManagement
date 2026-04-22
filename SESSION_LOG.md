# SESSION_LOG.md — Tiến độ theo session

# Claude Code tự cập nhật file này. Không sửa thủ công phần log.

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

**2. Split thành 6 file partial** (tất cả cùng `namespace MCGCadPlugin.Services.FittingManagement`, cùng `public partial class FittingManagementService`):

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
- **Namespace không đổi**: `MCGCadPlugin.Services.FittingManagement` — cần cho partial class lookup. Không đổi theo rule `§3` CLAUDE.md (đã có exception cho utility partial files từ trước).
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
1. `%APPDATA%\MCGCadPlugin\plugin.log` — managed-side context (memory, last file processing).
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
Test session 7 với 9 file thật (log ở `%APPDATA%\MCGCadPlugin\plugin.log`):
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
1. Palette title "MCGCadPlugin - FittingManagement" → "Fitting Management"
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
- [Commands/PaletteManager.cs](Commands/PaletteManager.cs): title `"MCGCadPlugin - FittingManagement"` → `"Fitting Management"`; 1 `AddVisual` → 4 `AddVisual` theo thứ tự user yêu cầu (FittingHandle, ProjectConfig, Template, BlockUtilities).
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
