using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MCGCadPlugin.Models.CheckList
{
    // Đại diện cho 1 dòng (1 câu hỏi) trong Checklist
    public class ChecklistItem : INotifyPropertyChanged
    {
        #region Backing fields
        private string _id;
        private string _content;
        private bool _isChecked;
        private bool _isCustom;
        private bool _isNotApplicable;
        #endregion

        #region Properties

        public string Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        /// <summary>Nội dung câu hỏi</summary>
        public string Content
        {
            get => _content;
            set => SetField(ref _content, value);
        }

        /// <summary>Trạng thái Tick box — TRUE nếu user đã check</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();

                // Mutual exclusion: check rồi thì bỏ N/A
                if (value && _isNotApplicable)
                {
                    _isNotApplicable = false;
                    OnPropertyChanged(nameof(IsNotApplicable));
                }
            }
        }

        /// <summary>True = Kỹ sư tự thêm (cho xóa + cho N/A) | False = Mặc định (Khóa)</summary>
        public bool IsCustom
        {
            get => _isCustom;
            set => SetField(ref _isCustom, value);
        }

        /// <summary>
        /// True = Không áp dụng cho bản vẽ này (bỏ qua yêu cầu hoàn thành).
        /// Chỉ có ý nghĩa khi IsCustom = true. Fixed items không được phép N/A.
        /// </summary>
        public bool IsNotApplicable
        {
            get => _isNotApplicable;
            set
            {
                if (_isNotApplicable == value) return;
                _isNotApplicable = value;
                OnPropertyChanged();

                // Mutual exclusion: bật N/A thì bỏ check
                if (value && _isChecked)
                {
                    _isChecked = false;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

        #endregion

        #region Constructors
        public ChecklistItem()
        {
            _id = Guid.NewGuid().ToString();
            _isChecked = false;
            _isCustom = false;
            _isNotApplicable = false;
        }

        public ChecklistItem(string content, bool isCustom = false) : this()
        {
            _content = content;
            _isCustom = isCustom;
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(propertyName);
        }
        #endregion
    }

    // Đại diện cho toàn bộ Hồ sơ Checklist nhúng vào bản vẽ
    public class ChecklistDocument
    {
        public string Discipline { get; set; } // Bộ môn (Structure, Mech, Layout...)
        public string Status { get; set; } = "PENDING"; // Trạng thái: PENDING hoặc APPROVED

        public string ApprovedBy { get; set; } // Tên User Windows đã ký duyệt
        public string ApprovedDate { get; set; } // Ngày giờ ký duyệt

        public List<ChecklistItem> Items { get; set; } // Danh sách các câu hỏi

        public ChecklistDocument()
        {
            Items = new List<ChecklistItem>();
        }
    }
}
