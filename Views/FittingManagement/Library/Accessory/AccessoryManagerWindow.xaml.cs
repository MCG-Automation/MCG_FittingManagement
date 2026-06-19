using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Newtonsoft.Json;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    public partial class AccessoryManagerWindow : Window
    {
        private readonly IMasterLibraryService _masterService;
        private CatalogItem _parentItem;
        private List<CatalogItem> _fullCatalog;
        private ObservableCollection<AccessoryItem> _localAccessories;

        public class ComboItem
        {
            public string PartNumber { get; set; }
            public string DisplayLabel { get; set; }
        }

        public AccessoryManagerWindow(IMasterLibraryService masterService, CatalogItem parentItem)
        {
            InitializeComponent();
            _masterService = masterService;
            _parentItem = parentItem;

            if (_parentItem.Accessories == null) _parentItem.Accessories = new List<AccessoryItem>();
            _localAccessories = new ObservableCollection<AccessoryItem>(_parentItem.Accessories);
            GridAccessories.ItemsSource = _localAccessories;

            TxtParentName.Text = $"[{_parentItem.PartNumber}] - {_parentItem.Description}";
            LoadMasterCatalog();
        }

        private void LoadMasterCatalog()
        {
            _fullCatalog = _masterService.GetMasterCatalogItems();
            var distinctCatalog = _fullCatalog
                .Where(x => x.PartNumber != _parentItem.PartNumber && !string.IsNullOrEmpty(x.PartNumber))
                .GroupBy(x => x.PartNumber).Select(g => g.First()).ToList();

            var comboList = new List<ComboItem>();
            foreach (var item in distinctCatalog)
            {
                string desc = string.IsNullOrEmpty(item.Description) ? item.Title : item.Description;
                comboList.Add(new ComboItem { PartNumber = item.PartNumber, DisplayLabel = $"[{item.PartNumber}] - {desc}" });
            }
            CboAccessories.ItemsSource = comboList.OrderBy(x => x.PartNumber).ToList();
        }

        private void CboAccessories_KeyUp(object sender, KeyEventArgs e)
        {
            var cmb = sender as ComboBox;
            if (cmb == null || cmb.ItemsSource == null) return;
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Escape) return;

            string searchText = cmb.Text;
            var view = CollectionViewSource.GetDefaultView(cmb.ItemsSource);
            
            if (string.IsNullOrEmpty(searchText)) view.Filter = null;
            else
            {
                view.Filter = item => ((ComboItem)item).DisplayLabel.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                cmb.IsDropDownOpen = true; 
            }
        }

        private void BtnCreateNew_Click(object sender, RoutedEventArgs e)
        {
            NewAccessoryWindow newAccWin = new NewAccessoryWindow(_masterService) { Owner = this };
            if (newAccWin.ShowDialog() == true)
            {
                LoadMasterCatalog();
                CboAccessories.SelectedValue = newAccWin.CreatedPartId;
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            string selectedPartId = (CboAccessories.SelectedValue != null) ? CboAccessories.SelectedValue.ToString() : "";
            if (string.IsNullOrEmpty(selectedPartId) && !string.IsNullOrWhiteSpace(CboAccessories.Text))
            {
                var match = System.Text.RegularExpressions.Regex.Match(CboAccessories.Text, @"\[(.*?)\]");
                selectedPartId = match.Success ? match.Groups[1].Value : CboAccessories.Text.Trim();
            }

            if (string.IsNullOrEmpty(selectedPartId) || !int.TryParse(TxtQty.Text, out int qty) || qty <= 0) return;

            var existingAcc = _localAccessories.FirstOrDefault(a => a.PartId.Equals(selectedPartId, StringComparison.OrdinalIgnoreCase));
            if (existingAcc != null) { existingAcc.Quantity += qty; GridAccessories.Items.Refresh(); }
            else _localAccessories.Add(new AccessoryItem { PartId = selectedPartId, Quantity = qty });

            TxtQty.Text = "1"; 
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = GridAccessories.SelectedItems.Cast<AccessoryItem>().ToList();
            foreach (var item in selectedItems) _localAccessories.Remove(item);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _parentItem.Accessories = _localAccessories.ToList();
            try
            {
                _masterService.MergeIntoMaster(new List<CatalogItem> { _parentItem });
                this.DialogResult = true; this.Close();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; this.Close();
        }
    }
}