using CommunityToolkit.Mvvm.ComponentModel;
using FurinaChronicle.App.Startup;
using Microsoft.Extensions.Logging;

namespace FurinaChronicle.App.ViewModels;

public partial class StartupPageViewModel(ApplicationStartupService startupService, ILogger<StartupPageViewModel> logger) : ObservableObject
{
    private bool started;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }
        = "正在初始化角色和武器元数据……";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsInitializing { get; set; }

    [ObservableProperty]
    public partial bool IsFailed { get; set; }

    public async Task<bool> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (started)
        {
            return !IsFailed;
        }

        started = true;
        IsInitializing = true;
        IsFailed = false;
        ErrorMessage = null;

        try
        {
            await startupService.InitializeAsync(cancellationToken);

            StatusMessage = "初始化完成。";
            return true;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Application metadata initialization failed.");

            StatusMessage = "初始化失败";
            ErrorMessage =
                "无法下载角色和武器数据。请检查网络连接，"
                + "完全关闭应用后重新启动。";

            IsFailed = true;
            return false;
        }
        finally
        {
            IsInitializing = false;
        }
    }
}