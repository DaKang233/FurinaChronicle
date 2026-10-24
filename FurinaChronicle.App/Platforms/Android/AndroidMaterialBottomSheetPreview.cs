// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidX.Core.View;
using AndroidX.Core.Widget;
using Google.Android.Material.BottomSheet;
using Google.Android.Material.Button;
using Microsoft.Maui.ApplicationModel;

namespace FurinaChronicle.App.UI.TechnicalPreview;

internal sealed class AndroidMaterialBottomSheetPreview
    : IMaterialBottomSheetPreview
{
    private Task? activeTask;

    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(
                () => ShowAsync(cancellationToken));
            return;
        }

        if (activeTask is not null)
        {
            await activeTask;
            return;
        }

        MainActivity activity =
            Platform.CurrentActivity as MainActivity
            ?? throw new InvalidOperationException(
                "The current Android activity is unavailable.");
        activeTask = ShowCoreAsync(activity, cancellationToken);
        try
        {
            await activeTask;
        }
        finally
        {
            activeTask = null;
        }
    }

    private static async Task ShowCoreAsync(
        MainActivity activity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new BottomSheetDialog(activity);
        SheetContent content = CreateContent(activity, dialog);
        dialog.SetContentView(content.Root);
        dialog.SetCancelable(true);
        dialog.SetCanceledOnTouchOutside(true);

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? dismissed = null;
        dismissed = (_, _) => completion.TrySetResult();
        dialog.DismissEvent += dismissed;

        using CancellationTokenRegistration registration =
            cancellationToken.Register(() =>
            {
                completion.TrySetCanceled(cancellationToken);
                activity.RunOnUiThread(dialog.Dismiss);
            });

        try
        {
            dialog.Show();
            dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize);
            ConfigureBehavior(dialog);
            ViewCompat.RequestApplyInsets(content.Root);
            content.FirstAction.Post(() =>
            {
                _ = content.FirstAction.RequestFocus();
            });
            await completion.Task;
        }
        finally
        {
            dialog.DismissEvent -= dismissed;
            if (dialog.IsShowing)
            {
                dialog.Dismiss();
            }
        }
    }

    private static void ConfigureBehavior(BottomSheetDialog dialog)
    {
        FrameLayout? sheet =
            dialog.FindViewById<FrameLayout>(Resource.Id.design_bottom_sheet);
        if (sheet is null)
        {
            return;
        }

        BottomSheetBehavior behavior = BottomSheetBehavior.From(sheet);
        behavior.Hideable = true;
        behavior.SkipCollapsed = true;
        behavior.State = BottomSheetBehavior.StateExpanded;
    }

    private static SheetContent CreateContent(
        Activity activity,
        BottomSheetDialog dialog)
    {
        int horizontalPadding = ToPixels(activity, 24);
        int baseBottomPadding = ToPixels(activity, 16);
        var root = new LinearLayout(activity)
        {
            Orientation = Orientation.Vertical
        };
        root.SetPadding(
            horizontalPadding,
            ToPixels(activity, 10),
            horizontalPadding,
            baseBottomPadding);

        var handle = new Android.Views.View(activity)
        {
            Background = new Android.Graphics.Drawables.ColorDrawable(
                Android.Graphics.Color.Gray),
            LayoutParameters = new LinearLayout.LayoutParams(
                ToPixels(activity, 36),
                ToPixels(activity, 4))
            {
                Gravity = GravityFlags.CenterHorizontal,
                BottomMargin = ToPixels(activity, 8)
            }
        };
        root.AddView(handle);

        var headingRow = new LinearLayout(activity)
        {
            Orientation = Orientation.Horizontal
        };
        headingRow.SetGravity(GravityFlags.CenterVertical);
        var heading = new TextView(activity)
        {
            Text = "数据操作 · UI-0",
            TextSize = 20,
            Typeface = Typeface.DefaultBold,
            LayoutParameters = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.WrapContent,
                1)
        };
        headingRow.AddView(heading);

        var closeButton = new MaterialButton(activity)
        {
            Text = "关闭",
            ContentDescription = "关闭数据操作面板"
        };
        closeButton.SetMinHeight(ToPixels(activity, 48));
        closeButton.Click += (_, _) => dialog.Dismiss();
        headingRow.AddView(closeButton);
        root.AddView(headingRow);

        var status = new TextView(activity)
        {
            Text = "受控样例：不会读取数据库、文件或网络。",
            TextSize = 14
        };
        status.SetPadding(0, ToPixels(activity, 4), 0, ToPixels(activity, 10));
        root.AddView(status);

        var scroll = new NestedScrollView(activity)
        {
            FillViewport = true,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                GetScrollHeight(activity))
        };
        var actions = new LinearLayout(activity)
        {
            Orientation = Orientation.Vertical
        };
        scroll.AddView(actions);

        MaterialButton refresh = CreateActionButton(
            activity,
            "刷新记录",
            "模拟刷新：没有访问网络。",
            status);
        actions.AddView(refresh);
        actions.AddView(CreateActionButton(
            activity,
            "导入数据",
            "模拟导入：没有打开文件选择器。",
            status));
        actions.AddView(CreateActionButton(
            activity,
            "导出数据",
            "模拟导出：没有创建文件。",
            status));

        var inputLabel = new TextView(activity)
        {
            Text = "键盘与滚动验证",
            TextSize = 16,
            Typeface = Typeface.DefaultBold
        };
        inputLabel.SetPadding(0, ToPixels(activity, 14), 0, ToPixels(activity, 4));
        actions.AddView(inputLabel);

        var input = new EditText(activity)
        {
            Hint = "输入受控备注，观察 IME 与面板滚动",
            ContentDescription = "UI-0 受控备注输入框"
        };
        input.SetMinHeight(ToPixels(activity, 56));
        input.SetSingleLine(false);
        input.SetMaxLines(3);
        actions.AddView(input);

        for (int index = 1; index <= 6; index++)
        {
            var row = new TextView(activity)
            {
                Text = $"滚动测试项 {index} · 仅用于验证长内容可达",
                TextSize = 14,
                Gravity = GravityFlags.CenterVertical
            };
            row.SetMinHeight(ToPixels(activity, 48));
            actions.AddView(row);
        }

        root.AddView(scroll);
        ViewCompat.SetOnApplyWindowInsetsListener(
            root,
            new SheetInsetsListener(
                horizontalPadding,
                baseBottomPadding,
                status));
        return new SheetContent(root, refresh);
    }

    private static MaterialButton CreateActionButton(
        Activity activity,
        string text,
        string result,
        TextView status)
    {
        var button = new MaterialButton(activity)
        {
            Text = text,
            ContentDescription = text,
            Gravity = GravityFlags.Start | GravityFlags.CenterVertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                BottomMargin = ToPixels(activity, 4)
            }
        };
        button.SetMinHeight(ToPixels(activity, 48));
        button.Click += (_, _) => status.Text = result;
        return button;
    }

    private static int GetScrollHeight(Activity activity)
    {
        int availableHeight = activity.Resources?.DisplayMetrics?.HeightPixels ??
            ToPixels(activity, 640);
        return Math.Min(
            ToPixels(activity, 480),
            Math.Max(ToPixels(activity, 220), (int)(availableHeight * 0.62)));
    }

    private static int ToPixels(Activity activity, int dp)
    {
        float density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        return (int)MathF.Round(dp * density);
    }

    private sealed record SheetContent(
        LinearLayout Root,
        MaterialButton FirstAction);

    private sealed class SheetInsetsListener(
        int horizontalPadding,
        int baseBottomPadding,
        TextView status)
        : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(
            Android.Views.View? view,
            WindowInsetsCompat? insets)
        {
            if (view is null || insets is null)
            {
                return insets;
            }

            int mask =
                WindowInsetsCompat.Type.SystemBars() |
                WindowInsetsCompat.Type.Ime();
            AndroidX.Core.Graphics.Insets? applied = insets.GetInsets(mask);
            if (applied is null)
            {
                return insets;
            }
            int bottom = Math.Max(baseBottomPadding, applied.Bottom);
            view.SetPadding(
                horizontalPadding,
                view.PaddingTop,
                horizontalPadding,
                bottom);
            status.Text =
                $"受控样例 · 系统/键盘底部 inset：{applied.Bottom}px";
            return insets;
        }
    }
}
