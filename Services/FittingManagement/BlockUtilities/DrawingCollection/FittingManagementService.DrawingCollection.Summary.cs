using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Drawing Collection — End-of-run summary: per-file breakdown table, totals, layout sanity,
    /// keep-as-is overlap detection, duplicate candidate detection, timing.
    /// </summary>
    public partial class FittingManagementService
    {
        #region End-of-run summary

        /// <summary>
        /// Log 1 block tổng kết ở cuối run — đủ thông tin để đánh giá quality mà không cần screenshot UI.
        /// Bao gồm: per-file compact table, totals, layout sanity check (sum widths + gaps vs span),
        /// keep-as-is overlap detection, duplicate candidate detection.
        /// </summary>
        private void WriteFinalSummary(
            string[] requestedPaths, List<PreparedDrawing> prepared, ImportResult result,
            double firstOffsetX, double lastOffsetX,
            long phase1Ms, long phase2Ms, long totalMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("====== DRAWING COLLECTION — SUMMARY ======");
            sb.AppendLine($"Requested: {requestedPaths.Length} file(s) | Success: {result.SuccessCount} | Failed: {result.FailCount}");

            // Per-file compact table — chỉ file preparedOK (lỗi đã log riêng phía trên).
            if (prepared.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Per-file breakdown:");
                sb.AppendLine("  #  | Width(mm) | Height(mm) | Entities | Renamed | Purged | Placed@X   | File");

                int totalEntitiesMs = 0, totalRenamed = 0, totalPurged = 0;
                int totalKeptA1 = 0, totalKeptCasHead = 0;
                int totalPrimaryCloned = 0, totalSymbolsIgnored = 0, totalTransformFailed = 0;
                double sumWidths = 0;

                for (int i = 0; i < prepared.Count; i++)
                {
                    var p = prepared[i];
                    string wStr = p.HasExtents ? p.Width.ToString("F0") : "N/A";
                    string hStr = p.HasExtents ? (p.Extents.MaxPoint.Y - p.Extents.MinPoint.Y).ToString("F0") : "N/A";
                    int ents = p.ExtStats?.TotalEntities ?? 0;
                    int rnm = p.RenameStats?.Renamed ?? 0;
                    int prg = p.PurgeStats?.TotalErased ?? 0;
                    string placedStr = p.PlacedOffsetX.HasValue ? p.PlacedOffsetX.Value.ToString("F0") : "--";

                    sb.AppendLine($"  {i + 1,2} | {wStr,9} | {hStr,10} | {ents,8} | {rnm,7} | {prg,6} | {placedStr,10} | {p.FileName}");

                    if (p.HasExtents) sumWidths += p.Width;
                    totalEntitiesMs += ents;
                    totalRenamed += rnm;
                    totalPurged += prg;
                    totalKeptA1 += p.RenameStats?.KeepAsIs_A1 ?? 0;
                    totalKeptCasHead += p.RenameStats?.KeepAsIs_CasHead ?? 0;
                    if (p.CloneStats != null)
                    {
                        totalPrimaryCloned += p.CloneStats.PrimaryCloned;
                        totalSymbolsIgnored += p.CloneStats.SymbolsIgnored;
                        totalTransformFailed += p.CloneStats.TransformFailed;
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Totals:");
                sb.AppendLine($"  ModelSpace entities scanned   : {totalEntitiesMs}");
                sb.AppendLine($"  Entities cloned into dest doc : {totalPrimaryCloned}");
                sb.AppendLine($"  Symbols ignored (dup-name)    : {totalSymbolsIgnored}" +
                              "   (dest giữ định nghĩa, src bỏ — đúng thiết kế khi trùng layer/block/linetype)");
                sb.AppendLine($"  TransformBy failures          : {totalTransformFailed}" +
                              (totalTransformFailed > 0 ? "   ⚠ nên kiểm log chi tiết phía trên" : ""));
                sb.AppendLine($"  Blocks renamed                : {totalRenamed}");
                sb.AppendLine($"  Blocks kept as-is [A1]        : {totalKeptA1}");
                sb.AppendLine($"  Blocks kept as-is [CAS_HEAD]  : {totalKeptCasHead}");
                sb.AppendLine($"  Purge: erased total           : {totalPurged}");

                // Sanity-check layout: lastOffsetX ≈ firstOffsetX + sum(effectiveWidth) + (N_advanced) × GAP.
                // Hướng A: effectiveWidth = A1.Width nếu HasA1, ngược lại fallback bbox.Width.
                int advancedFiles = prepared.Count(p => p.Advanced);
                double sumEffective = prepared.Where(p => p.Advanced).Sum(p => p.EffectiveWidth);
                double expectedSpan = firstOffsetX + sumEffective + advancedFiles * COLLECTION_GAP;
                int filesUsingA1 = prepared.Count(p => p.Advanced && p.HasA1);
                int filesUsingBboxFallback = prepared.Count(p => p.Advanced && !p.HasA1);
                int filesWithMultipleA1 = prepared.Count(p => p.ValidA1Count > 1);
                int filesWithOverflow = prepared.Count(p => p.HasA1 && (p.LeftOverflowSrc > 0.5 || p.RightOverflowSrc > 0.5));
                double sumLeftOverflow = prepared.Where(p => p.HasA1).Sum(p => p.LeftOverflowSrc);
                double sumRightOverflow = prepared.Where(p => p.HasA1).Sum(p => p.RightOverflowSrc);
                sb.AppendLine();
                sb.AppendLine("Layout sanity (Hướng A — A1-frame anchored, N=1):");
                sb.AppendLine($"  Initial offsetX                       : {firstOffsetX:F0}mm " +
                              (firstOffsetX > 0 ? "(nối tiếp sau A1 existing trong dest)" : "(dest trống A1)"));
                sb.AppendLine($"  Sum bbox widths (Extents.Width)       : {sumWidths:F0}mm");
                sb.AppendLine($"  Sum effective widths                  : {sumEffective:F0}mm " +
                              $"({filesUsingA1} file dùng A1.Width, {filesUsingBboxFallback} file fallback bbox)");
                if (filesWithMultipleA1 > 0)
                    sb.AppendLine($"  Files với nhiều A1 (anchor leftmost)  : {filesWithMultipleA1} " +
                                  $"⚠ A1 phụ có thể chồng A1 file kế bên");
                sb.AppendLine($"  Files advanced                        : {advancedFiles} → gaps = {advancedFiles} × {COLLECTION_GAP:F0} = {advancedFiles * COLLECTION_GAP:F0}mm");
                sb.AppendLine($"  Expected next offsetX after last file : {expectedSpan:F0}mm");
                sb.AppendLine($"  Actual   next offsetX after last file : {lastOffsetX:F0}mm");
                double delta = Math.Abs(expectedSpan - lastOffsetX);
                if (delta > 0.5)
                    sb.AppendLine($"  ⚠ Delta {delta:F2}mm — không khớp, có thể 1 file fail ở phase 2.");
                else
                    sb.AppendLine($"  ✓ Khớp (delta={delta:F2}mm).");

                // Overflow summary — entity nằm ngoài A1 frame (dim/leader/text/orphan).
                // Overflow > COLLECTION_GAP có thể chồng A1 file kế bên.
                if (filesWithOverflow > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Overflow check ({filesWithOverflow}/{filesUsingA1} file có entity ngoài A1):");
                    sb.AppendLine($"  Sum left overflow  : {sumLeftOverflow:F0}mm");
                    sb.AppendLine($"  Sum right overflow : {sumRightOverflow:F0}mm");
                    foreach (var p in prepared.Where(p => p.HasA1 && (p.LeftOverflowSrc > 0.5 || p.RightOverflowSrc > 0.5)))
                    {
                        string risk = (p.LeftOverflowSrc > COLLECTION_GAP || p.RightOverflowSrc > COLLECTION_GAP)
                            ? $"  ⚠ vượt GAP={COLLECTION_GAP:F0}mm → CHỒNG A1 lân cận"
                            : "";
                        string multiNote = p.ValidA1Count > 1 ? $"  [N={p.ValidA1Count} A1, anchor leftmost]" : "";
                        sb.AppendLine($"  • {p.FileName}: L={p.LeftOverflowSrc:F0}, R={p.RightOverflowSrc:F0}mm{multiNote}{risk}");
                    }
                    sb.AppendLine("  → Kiểm dim/leader/text/entity rảnh nằm ngoài frame A1 trong file source.");
                    sb.AppendLine("    File có N>1: A1 phụ trong source chiếm phần overflow phải; remove bớt A1 hoặc tách thành nhiều file để layout sạch.");
                }

                // Keep-as-is overlap detection — scan mọi A1/CAS_HEAD đã clone vào dest,
                // tìm các cặp có bbox chồng lên nhau.
                var allKeepAsIs = new List<(string FileName, KeepAsIsClonedInfo Info)>();
                foreach (var p in prepared)
                {
                    if (p.CloneStats?.KeepAsIsCloned == null) continue;
                    foreach (var ki in p.CloneStats.KeepAsIsCloned)
                        allKeepAsIs.Add((p.FileName, ki));
                }

                sb.AppendLine();
                sb.AppendLine($"Keep-as-is blocks in dest (A1/CAS_HEAD): {allKeepAsIs.Count} reference(s)");
                if (allKeepAsIs.Count > 0)
                {
                    for (int i = 0; i < allKeepAsIs.Count; i++)
                    {
                        var (fn, info) = allKeepAsIs[i];
                        sb.AppendLine($"  [{i + 1}] {info.BlockName} từ '{fn}' handle=0x{info.DestHandleValue:X} " +
                                      $"pos=({info.Position.X:F0},{info.Position.Y:F0}) " +
                                      $"bbox=[({info.Bbox.MinPoint.X:F0},{info.Bbox.MinPoint.Y:F0})-({info.Bbox.MaxPoint.X:F0},{info.Bbox.MaxPoint.Y:F0})]");
                    }

                    var overlaps = FindKeepAsIsOverlaps(allKeepAsIs);
                    if (overlaps.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"⚠ OVERLAP DETECTED — {overlaps.Count} cặp Keep-as-is block chèn lên nhau:");
                        foreach (var ov in overlaps)
                        {
                            var (fn1, k1) = allKeepAsIs[ov.IndexA];
                            var (fn2, k2) = allKeepAsIs[ov.IndexB];
                            sb.AppendLine($"  [{ov.IndexA + 1}] ↔ [{ov.IndexB + 1}]: " +
                                          $"{k1.BlockName} '{fn1}' (0x{k1.DestHandleValue:X}) " +
                                          $"vs {k2.BlockName} '{fn2}' (0x{k2.DestHandleValue:X})");
                            sb.AppendLine($"      Overlap area={ov.OverlapArea:F0}mm² " +
                                          $"(~{ov.PctOfSmaller:F1}% của block nhỏ hơn), " +
                                          $"offset pos=({k2.Position.X - k1.Position.X:F0},{k2.Position.Y - k1.Position.Y:F0})mm");
                        }
                        sb.AppendLine("  → Lý do thường gặp: (1) A1/CAS_HEAD được insert TRỰC TIẾP trong Model Space của nhiều file (không phải Paper Space); " +
                                      "(2) file width nhỏ không đủ để 2 khung tên cách nhau; (3) A1 của file trước mở rộng quá bbox chính do có entity đi kèm.");
                    }
                    else
                    {
                        sb.AppendLine("  ✓ Không có overlap giữa các Keep-as-is blocks.");
                    }
                }

                // Duplicate detection — bbox giống + entity count chênh <5% → ứng viên file trùng.
                var dupPairs = FindDuplicateCandidates(prepared);
                if (dupPairs.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ Duplicate candidates (bbox khớp trong tolerance 1mm, entity count chênh <5%):");
                    foreach (var pair in dupPairs)
                    {
                        var p1 = prepared[pair.Item1]; var p2 = prepared[pair.Item2];
                        int e1 = p1.ExtStats?.TotalEntities ?? 0;
                        int e2 = p2.ExtStats?.TotalEntities ?? 0;
                        sb.AppendLine($"  #{pair.Item1 + 1} '{p1.FileName}' (entities={e1}) ≈ #{pair.Item2 + 1} '{p2.FileName}' (entities={e2})");
                    }
                    sb.AppendLine("  → User nên kiểm xem có phải cùng 1 bản vẽ bị copy/save hai lần không.");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Timing: phase1={phase1Ms}ms, phase2={phase2Ms}ms, total={totalMs}ms");
            if (prepared.Count > 0)
                sb.AppendLine($"  Avg/file: phase1={phase1Ms / prepared.Count}ms, phase2={phase2Ms / prepared.Count}ms");
            sb.AppendLine("==========================================");

            FileLogger.Log(LOG_PREFIX, sb.ToString());
        }

        /// <summary>
        /// Tìm các cặp file có bbox gần trùng (tolerance 1mm trên w và h) và entity count chênh &lt;5%.
        /// O(N²) — OK vì user chọn thủ công, N thường ≤ 20.
        /// </summary>
        private static List<Tuple<int, int>> FindDuplicateCandidates(List<PreparedDrawing> prepared)
        {
            const double BBOX_TOLERANCE_MM = 1.0;
            const double ENTITY_COUNT_TOLERANCE_PCT = 0.05;

            var pairs = new List<Tuple<int, int>>();
            for (int i = 0; i < prepared.Count; i++)
            {
                var p1 = prepared[i];
                if (!p1.HasExtents) continue;
                double h1 = p1.Extents.MaxPoint.Y - p1.Extents.MinPoint.Y;
                int e1 = p1.ExtStats?.TotalEntities ?? 0;

                for (int j = i + 1; j < prepared.Count; j++)
                {
                    var p2 = prepared[j];
                    if (!p2.HasExtents) continue;
                    double h2 = p2.Extents.MaxPoint.Y - p2.Extents.MinPoint.Y;
                    int e2 = p2.ExtStats?.TotalEntities ?? 0;

                    if (Math.Abs(p1.Width - p2.Width) > BBOX_TOLERANCE_MM) continue;
                    if (Math.Abs(h1 - h2) > BBOX_TOLERANCE_MM) continue;

                    int maxE = Math.Max(e1, e2);
                    if (maxE == 0) continue;
                    double diffPct = Math.Abs(e1 - e2) / (double)maxE;
                    if (diffPct > ENTITY_COUNT_TOLERANCE_PCT) continue;

                    pairs.Add(Tuple.Create(i, j));
                }
            }
            return pairs;
        }

        /// <summary>
        /// Tìm cặp Keep-as-is reference trong dest có bbox 2D chồng lên nhau.
        /// O(N²) — OK vì số A1/CAS_HEAD thường &lt; 50.
        /// </summary>
        private static List<KeepAsIsOverlapPair> FindKeepAsIsOverlaps(
            List<(string FileName, KeepAsIsClonedInfo Info)> all)
        {
            var result = new List<KeepAsIsOverlapPair>();
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    var a = all[i].Info.Bbox;
                    var b = all[j].Info.Bbox;

                    double oxMin = Math.Max(a.MinPoint.X, b.MinPoint.X);
                    double oxMax = Math.Min(a.MaxPoint.X, b.MaxPoint.X);
                    double oyMin = Math.Max(a.MinPoint.Y, b.MinPoint.Y);
                    double oyMax = Math.Min(a.MaxPoint.Y, b.MaxPoint.Y);

                    if (oxMax <= oxMin || oyMax <= oyMin) continue; // không overlap

                    double overlapArea = (oxMax - oxMin) * (oyMax - oyMin);
                    double areaA = (a.MaxPoint.X - a.MinPoint.X) * (a.MaxPoint.Y - a.MinPoint.Y);
                    double areaB = (b.MaxPoint.X - b.MinPoint.X) * (b.MaxPoint.Y - b.MinPoint.Y);
                    double smaller = Math.Min(areaA, areaB);
                    double pct = smaller > 0 ? (overlapArea / smaller * 100) : 0;

                    result.Add(new KeepAsIsOverlapPair
                    {
                        IndexA = i,
                        IndexB = j,
                        OverlapArea = overlapArea,
                        PctOfSmaller = pct
                    });
                }
            }
            return result;
        }

        #endregion
    }
}
