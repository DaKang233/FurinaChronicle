using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaArchive.Core.Wishes;
using FurinaArchive.Services.Wishes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace FurinaArchive.App.ViewModels
{
    public partial class MainPageViewModel(
        GetRecentWishRecords getRecentWishRecords)
        : ObservableObject
    {
        public ObservableCollection<WishRecord> WishRecords { get; } = [];

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string? errorMessage;

        [RelayCommand]
        private async Task LoadAsync()
        {
            if (IsLoading)
            {
                return;
            }

            try
            {
                IsLoading = true;
                ErrorMessage = null;

                IReadOnlyList<WishRecord> records =
                    await getRecentWishRecords.ExecuteAsync();

                WishRecords.Clear();

                foreach (WishRecord record in records)
                {
                    WishRecords.Add(record);
                }
            }
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
