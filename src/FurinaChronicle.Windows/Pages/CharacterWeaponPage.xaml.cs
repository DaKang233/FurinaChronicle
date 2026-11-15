// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class CharacterWeaponPage : UserControl
{
    private readonly CharacterWeaponHistoryState state;
    private bool rendering;
    private readonly WindowsHistoryCollection collection;
    private CancellationTokenSource? activeCollection;

    public CharacterWeaponPage(CharacterWeaponHistoryState state, WindowsHistoryCollection collection)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.collection = collection;
        InitializeComponent();
        KindPicker.ItemsSource = new[] { "角色", "武器" };
        AutomationProperties.SetName(this, "角色与武器");
    }

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
    }

    public void Leave()
    {
        activeCollection?.Cancel();
        state.Cancel();
    }

    private async void OnCollect(object sender, RoutedEventArgs e)
    {
        if (activeCollection is not null ||
            state.Snapshot.SelectedArchiveId is not Guid archive ||
            state.Snapshot.SelectedAccountId is not Guid account) return;

        using var cancellation = new CancellationTokenSource();
        activeCollection = cancellation;
        CollectButton.IsEnabled = false;
        CollectionStatus.Text = "正在准备采集…";
        try
        {
            string? result = await collection.RunAsync(XamlRoot, archive, account, null, cancellation.Token);
            CollectionStatus.Text = result ?? "已取消采集。";
            await state.ReloadAsync(cancellation.Token);
        }
        catch (OperationCanceledException) { CollectionStatus.Text = "采集已取消。"; }
        catch { CollectionStatus.Text = "采集未完成，请检查登录和网络后重试。"; }
        finally
        {
            activeCollection = null;
            Render();
        }
    }

    private void Render()
    {
        CharacterWeaponHistorySnapshot snapshot = state.Snapshot;
        rendering = true;
        try
        {
            ArchivePicker.ItemsSource = snapshot.Archives;
            ArchivePicker.SelectedIndex = IndexOf(
                snapshot.Archives,
                snapshot.SelectedArchiveId);
            AccountPicker.ItemsSource = snapshot.Accounts;
            AccountPicker.SelectedIndex = IndexOf(
                snapshot.Accounts,
                snapshot.SelectedAccountId);
            KindPicker.SelectedIndex =
                snapshot.Kind == InventoryItemKind.Weapon ? 1 : 0;
            ObservationList.ItemsSource = snapshot.Observations;
            ObservationList.SelectedItem = snapshot.SelectedObservation;
            StatusText.Text = snapshot.Status;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
            EmptyText.Text = snapshot.EmptyMessage;
            EmptyText.Visibility = string.IsNullOrWhiteSpace(snapshot.EmptyMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;
            PreviousButton.IsEnabled = snapshot.HasPrevious && !snapshot.IsBusy;
            NextButton.IsEnabled = snapshot.HasNext && !snapshot.IsBusy;
            ReloadButton.IsEnabled = !snapshot.IsBusy;
            CollectButton.IsEnabled = !snapshot.IsBusy && activeCollection is null && snapshot.SelectedAccountId is not null;
            PageText.Text = snapshot.PageText;
            AutomationProperties.SetName(
                StatusText,
                snapshot.IsBusy ? "角色与武器观察正在载入" : "角色与武器观察状态");
            RenderDetail();
        }
        finally
        {
            rendering = false;
        }
    }

    private void RenderDetail()
    {
        CharacterWeaponObservationView? view = state.Snapshot.SelectedObservation;
        DetailTitleText.Text = view?.Title ?? "选择一次观察查看详情";
        DetailBodyText.Text = view?.DetailText ?? string.Empty;
        AutomationProperties.SetName(
            DetailBodyText,
            view is null ? "观察详情" : $"观察详情：{view.Title}");
    }

    private async void OnArchiveSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        int index = ArchivePicker.SelectedIndex;
        if (index < 0 || index >= state.Snapshot.Archives.Count)
        {
            return;
        }
        await state.SelectArchiveAsync(state.Snapshot.Archives[index].Id);
        Render();
    }

    private async void OnAccountSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        int index = AccountPicker.SelectedIndex;
        if (index < 0 || index >= state.Snapshot.Accounts.Count)
        {
            return;
        }
        await state.SelectAccountAsync(state.Snapshot.Accounts[index].Id);
        Render();
    }

    private async void OnKindSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await state.SetKindAsync(
            KindPicker.SelectedIndex == 1
                ? InventoryItemKind.Weapon
                : InventoryItemKind.Character);
        Render();
    }

    private void OnObservationSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        state.SelectObservation(
            (ObservationList.SelectedItem as CharacterWeaponObservationView)?
                .ObservationId);
        RenderDetail();
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        await state.ReloadAsync();
        Render();
    }

    private async void OnPrevious(object sender, RoutedEventArgs e)
    {
        await state.PreviousPageAsync();
        Render();
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        await state.NextPageAsync();
        Render();
    }

    private static int IndexOf(
        IReadOnlyList<HistoryChoice> choices,
        Guid? id)
    {
        if (id is not Guid wanted)
        {
            return -1;
        }
        for (int index = 0; index < choices.Count; index++)
        {
            if (choices[index].Id == wanted)
            {
                return index;
            }
        }
        return -1;
    }
}
