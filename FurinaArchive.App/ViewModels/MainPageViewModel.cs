using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaArchive.App.Demo;
using FurinaArchive.Core.Wishes;
using FurinaArchive.Services.Wishes;
using FurinaArchive.Services.Wishes.Importing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace FurinaArchive.App.ViewModels
{
    public partial class MainPageViewModel : ObservableObject
    {
        private readonly GetRecentWishRecords getRecentWishRecords;
        private readonly ImportWishRecords importWishRecords;

        public MainPageViewModel(GetRecentWishRecords getRecentWishRecords, ImportWishRecords importWishRecords)
        {
            this.getRecentWishRecords = getRecentWishRecords;
            this.importWishRecords = importWishRecords;
        }
        
        public ObservableCollection<WishRecord> WishRecords { get; } = [];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
        [NotifyCanExecuteChangedFor(nameof(ImportSampleCommand))]
        private bool isBusy;

        [ObservableProperty]
        private string? errorMessage;

        [ObservableProperty]
        private string? importSummary;

        private bool CanRun() { return !IsBusy; }

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task LoadAsync()
        {
            await ExecuteBusyAsync(async()  => { await ReloadRecordsAsync(); });
        }

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task ImportSampleAsync()
        {
            await ExecuteBusyAsync(async () =>
            {
                await using Stream stream = SampleWishData.OpenStream();
                WishImportResult result = await importWishRecords.ExecuteAsync(stream, SampleWishData.GameAccountId);
                ImportSummary = $"共读取 {result.TotalCount} 条，" +
                    $"导入 {result.ImportedCount} 条，" +
                    $"重复 {result.DuplicateCount} 条，" +
                    $"无效 {result.InvalidCount} 条。";
                await ReloadRecordsAsync();
            });
        }

        private async Task ReloadRecordsAsync()
        {
            IReadOnlyList<WishRecord> records = await getRecentWishRecords.ExecuteAsync(count: 20);
            WishRecords.Clear();
            foreach (WishRecord record in records)
            {
                WishRecords.Add(record);
            }
        }

        private async Task ExecuteBusyAsync(Func<Task> operation)
        {
            if (IsBusy) return;
            try
            {
                IsBusy = true;
                ErrorMessage = null;
                await operation();
            }
            catch (OperationCanceledException) { ErrorMessage = "操作已取消。"; }
            catch (Exception ex) { ErrorMessage = ex.Message; }
            finally { IsBusy = false; }
        }
    }
}
