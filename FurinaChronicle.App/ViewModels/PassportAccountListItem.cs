using CommunityToolkit.Mvvm.ComponentModel;
using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.App.ViewModels;

public partial class PassportAccountListItem(PassportAccount account)
    : ObservableObject
{
    public PassportAccount Account { get; } = account;

    public Guid Id => Account.Id;

    public string Aid => Account.Aid;

    public string RealmDisplayName => Account.Realm == PassportRealm.Oversea
        ? "HoYoLAB · 国际服"
        : "米游社 · 国服";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerDisplayName))]
    public partial string DisplayName { get; set; } =
        account.DisplayName ?? (account.Realm == PassportRealm.Oversea
            ? $"HoYoLAB 用户 {account.Aid}"
            : $"米游社用户 {account.Aid}");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    public partial Uri? AvatarUrl { get; set; }

    public bool HasAvatar => AvatarUrl is not null;

    public string PickerDisplayName => $"{DisplayName} · {RealmDisplayName}";
}
