using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.DatabaseServices;

namespace MCGCadPlugin.Utilities.FittingManagement
{
    /// <summary>
    /// Trích thumbnail từ file .dwg qua side-database.
    /// Priority:
    ///   1. <c>Database.ThumbnailBitmap</c> (file-level — set bởi SaveAs nếu file có thumbnail)
    ///   2. <c>BlockTableRecord.PreviewIcon</c> (block-level — AutoCAD tự generate khi định nghĩa block)
    ///   3. null → caller hiện placeholder
    /// Chuyển <see cref="Bitmap"/> sang <see cref="BitmapSource"/> để bind vào WPF Image.Source.
    /// </summary>
    public static class DwgThumbnailExtractor
    {
        private const string LOG_PREFIX = "[DwgThumbnailExtractor]";

        /// <summary>
        /// Trả về <see cref="BitmapSource"/> đã freeze (an toàn cross-thread).
        /// Null nếu file không tồn tại / không có thumbnail.
        /// </summary>
        public static BitmapSource Extract(string dwgPath, string preferredBlockName = null)
        {
            if (string.IsNullOrEmpty(dwgPath) || !File.Exists(dwgPath))
            {
                Debug.WriteLine($"{LOG_PREFIX} File không tồn tại: {dwgPath}");
                return null;
            }

            try
            {
                using (var db = new Database(false, true))
                {
                    db.ReadDwgFile(dwgPath, FileShare.Read, true, "");

                    // Priority 1: file-level thumbnail
                    Bitmap fileThumb = TryGetFileThumbnail(db);
                    if (fileThumb != null) return ToBitmapSource(fileThumb);

                    // Priority 2: block-level preview icon
                    if (!string.IsNullOrEmpty(preferredBlockName))
                    {
                        Bitmap blockIcon = TryGetBlockPreviewIcon(db, preferredBlockName);
                        if (blockIcon != null) return ToBitmapSource(blockIcon);
                    }

                    Debug.WriteLine($"{LOG_PREFIX} Không có thumbnail/icon: {dwgPath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI Extract({dwgPath}): {ex.Message}");
                return null;
            }
        }

        private static Bitmap TryGetFileThumbnail(Database db)
        {
            try { return db.ThumbnailBitmap; }
            catch { return null; }
        }

        private static Bitmap TryGetBlockPreviewIcon(Database db, string blockName)
        {
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    if (!bt.Has(blockName)) return null;
                    var btr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
                    Bitmap icon = btr.PreviewIcon;
                    tr.Commit();
                    return icon;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} TryGetBlockPreviewIcon LỖI: {ex.Message}");
                return null;
            }
        }

        /// <summary>Convert System.Drawing.Bitmap sang frozen WPF BitmapSource.</summary>
        private static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            if (bmp == null) return null;
            try
            {
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} ToBitmapSource LỖI: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Lấy <see cref="Bitmap"/> raw của <c>BlockTableRecord.PreviewIcon</c> trong DATABASE đang mở.
        /// Dùng khi Publish/Push Update để copy preview icon → <c>exportDb.ThumbnailBitmap</c>.
        /// </summary>
        public static Bitmap GetBlockPreviewIcon(Database db, ObjectId btrId)
        {
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
                    Bitmap icon = btr?.PreviewIcon;
                    tr.Commit();
                    return icon;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} GetBlockPreviewIcon LỖI: {ex.Message}");
                return null;
            }
        }
    }
}
