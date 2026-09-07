using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.App;

internal static class LoginNavigation
{
    public static async Task GoBackAsync(Page page)
    {
        if (page.Navigation.NavigationStack.Count > 1)
        {
            await page.Navigation.PopAsync(animated: true);
            return;
        }

        if (page.Navigation.ModalStack.Count > 0)
        {
            await page.Navigation.PopModalAsync(animated: true);
            return;
        }

        if (Shell.Current is AppShell shell)
        {
            await shell.ShowUserPageAsync();
        }
    }

    public static async Task CompleteAsync(
        Page page,
        UserPageViewModel userPageViewModel,
        PassportAccount account)
    {
        await userPageViewModel.ReloadAsync(account.Id);
        if (page.Navigation.ModalStack.Count > 0)
        {
            await page.Navigation.PopModalAsync(animated: true);
        }

        if (Shell.Current is AppShell shell)
        {
            await shell.ShowUserPageAsync();
        }
        else
        {
            await page.Navigation.PopToRootAsync(animated: true);
        }
    }
}
