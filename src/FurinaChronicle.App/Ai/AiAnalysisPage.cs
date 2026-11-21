// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using FurinaChronicle.App.Exporting;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.App.Ai;
public sealed class AiAnalysisPage : ContentPage
{
    private readonly AiWorkspaceState state;
    private readonly INotificationCenter notifications;
    private readonly AiAttachmentReader reader;
    private readonly Label title=new(){FontSize=22,FontAttributes=FontAttributes.Bold},status=new(){FontSize=12,AutomationId="AiStatus"},contextLabel=new(){FontSize=12};
    private readonly Editor composer=new(){Placeholder="输入消息或附加文件/档案资料",HeightRequest=90,AutomationId="AiQuestion"};
    private readonly ScrollView scroll=new(){AutomationId="AiMessages"};
    private readonly VerticalStackLayout messages=new(){Spacing=14},attachments=new(){Spacing=4};
    private readonly Button send=new(){Text="发送",AutomationId="AiSend"},stop=new(){Text="停止",AutomationId="AiCancel"},file=new(){Text="文件/图片",AutomationId="AiAttachFile"},data=new(){Text="档案资料",AutomationId="AiPrepare"};
    private readonly CheckBox imageConsent=new();
    private readonly View imageConsentRow;
    private bool rendering,presentingReview,entered,follow=true;
    private Guid renderedId;
    private int visible=30;
    private string renderKey="",pendingKey="";
    private readonly Dictionary<Guid,(VerticalStackLayout Body,Label Status,string Text)> bubbles=[];
    public AiAnalysisPage(AiWorkspaceState state,INotificationCenter notifications,AiAttachmentReader reader)
    {
        this.state=state;this.notifications=notifications;this.reader=reader;state.Initialize();Title="AI 聊天";
        var root=new Grid{Padding=12,RowSpacing=8,RowDefinitions={new(GridLength.Auto),new(GridLength.Auto),new(GridLength.Star),new(GridLength.Auto),new(GridLength.Auto),new(GridLength.Auto)}};
        var header=new Grid{ColumnDefinitions={new(GridLength.Star),new(GridLength.Auto)}};header.Add(title);
        var menu=Button("⋯",MenuAsync);Grid.SetColumn(menu,1);header.Add(menu);root.Add(header);
        var top=new HorizontalStackLayout{Spacing=6};top.Add(Button("对话",ChatsAsync));top.Add(Button("新建",()=>state.Session.NewConversationAsync()));top.Add(Button("模型",ModelsAsync));top.Add(Button("设置",async()=>{if(Shell.Current is AppShell app)await app.ShowSettingsPageAsync();}));top.Add(Button("↓",async()=>{follow=true;await scroll.ScrollToAsync(0,messages.Height,false);}));Grid.SetRow(top,1);root.Add(top);
        scroll.Content=messages;Grid.SetRow(scroll,2);root.Add(scroll);scroll.Scrolled+=(_,e)=>follow=messages.Height-scroll.Height-e.ScrollY<80;
        var extra=new VerticalStackLayout{Spacing=4};extra.Add(contextLabel);extra.Add(new ScrollView{Content=attachments,MaximumHeightRequest=120});
        imageConsentRow=Check(imageConsent,"已检查图片，移除了 UID、凭据和个人标识");extra.Add(imageConsentRow);extra.Add(status);Grid.SetRow(extra,3);root.Add(extra);
        Grid.SetRow(composer,4);root.Add(composer);composer.TextChanged+=(_,_)=>{if(!rendering)state.Session.UpdateDraft(composer.Text??"");};
        var actions=new Grid{ColumnSpacing=4,ColumnDefinitions={new(GridLength.Star),new(GridLength.Star),new(GridLength.Star),new(GridLength.Auto)}};
        foreach(var pair in new[]{(file,0),(data,1),(send,2),(stop,3)}){Grid.SetColumn(pair.Item1,pair.Item2);actions.Add(pair.Item1);}Grid.SetRow(actions,5);root.Add(actions);Content=root;
        file.Clicked+=async(_,_)=>await UiAsync(FileAsync);data.Clicked+=async(_,_)=>await UiAsync(DataAsync);send.Clicked+=async(_,_)=>await UiAsync(SendAsync);stop.Clicked+=(_,_)=>state.Cancel();
        imageConsent.CheckedChanged+=(_,_)=>{if(!rendering)state.Session.ReviewImages(imageConsent.IsChecked);};
        state.Changed+=(_,_)=>MainThread.BeginInvokeOnMainThread(Render);
    }
    protected override async void OnAppearing(){base.OnAppearing();if(presentingReview)return;entered=true;await state.EnterAsync();Render();}
    protected override async void OnDisappearing(){if(!presentingReview&&entered){entered=false;state.Leave();if(state.Session.IsInitialized)await UiAsync(()=>state.Session.SaveAsync());}base.OnDisappearing();}
    private Button Button(string text,Func<Task> action){var button=new Button{Text=text,Padding=new Thickness(10,5)};button.Clicked+=async(_,_)=>await UiAsync(action);return button;}
    private async Task ModelsAsync()
    {
        if(state.IsBusy)throw new AiAnalysisException("请先停止或等待生成完成。");
        var session=state.Session;await session.LoadSettingsAsync();
        if(session.Providers.Providers.Count==0)throw new AiAnalysisException("请先在设置页添加供应商和模型。");
        var provider=new Picker{Title="供应商",ItemsSource=session.Providers.Providers.ToArray(),ItemDisplayBinding=new Binding("Label"),AutomationId="AiChatProvider"};
        var model=new Picker{Title="模型",ItemDisplayBinding=new Binding("Label"),AutomationId="AiChatModel"};
        void Update(){var p=provider.SelectedItem as AiProviderProfile;model.ItemsSource=p?.Models.ToArray();model.SelectedItem=p?.Models.FirstOrDefault(m=>m.Id==(p.Id==session.Settings.ProviderId?session.Settings.ModelProfileId:p.SelectedModelId))??p?.Models.FirstOrDefault();}
        provider.SelectedIndexChanged+=(_,_)=>Update();provider.SelectedItem=session.Providers.Find(session.Settings.ProviderId)??session.Providers.Providers.FirstOrDefault();Update();
        var body=new VerticalStackLayout{Spacing=12};body.Add(provider);body.Add(model);body.Add(new Label{Text="仅切换配置，不发送资料；下次发送仍需审查接收方与历史。"});
        if(await Review("选择供应商与模型",body,"切换")==AiReviewChoice.Send)
        {
            if(provider.SelectedItem is not AiProviderProfile p||model.SelectedItem is not AiModelProfile m)throw new AiAnalysisException("请选择有效供应商和模型。");
            await session.SelectModelAsync(p.Id,m.Id);
        }
    }
    private void Render()
    {
        if(rendering)return;rendering=true;
        try
        {
            var s=state.Session;title.Text=s.Title;status.Text=state.Status+" "+s.StorageNotice;
            contextLabel.Text=(s.SelectionMissing?"原配置已删除，请重新选择":string.IsNullOrWhiteSpace(s.Settings.Model)?"请在设置页配置模型":s.ProviderName+" · "+s.Settings.Model)+" · "+(s.Context is {} c?$"{c.Evidence.Count} 项游戏证据" :"普通聊天");
            file.IsEnabled=data.IsEnabled=send.IsEnabled=!state.IsBusy;stop.IsEnabled=state.IsBusy;
            string key=s.ConversationId+":"+string.Join(',',s.Turns.Select(x=>x.Id));
            if(key!=renderKey)
            {
                if(renderedId!=s.ConversationId){renderedId=s.ConversationId;visible=30;composer.Text=s.DraftText;follow=true;}
                renderKey=key;messages.Clear();bubbles.Clear();
                if(s.Turns.Count>visible)messages.Add(Button("加载更早消息",()=>{visible+=30;renderKey="";Render();return Task.CompletedTask;}));
                foreach(var turn in s.Turns.TakeLast(visible))
                {
                    var card=new VerticalStackLayout{Spacing=6,Padding=10};card.Add(new Label{Text="你 · "+turn.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"),FontAttributes=FontAttributes.Bold});card.Add(new Label{Text=turn.Question});
                    foreach(var saved in turn.Attachments)card.Add(Button("附件 · "+saved.Label,()=>ViewAttachment(saved)));
                    var buttons=new HorizontalStackLayout{Spacing=6};buttons.Add(Button("复制问题",()=>Clipboard.Default.SetTextAsync(turn.Question)));buttons.Add(Button("编辑/重试",async()=>{composer.Text=await s.ForkFromAsync(turn.Id);renderKey="";Render();}));card.Add(buttons);
                    card.Add(new Label{Text="助手 · "+turn.ModelLabel,FontAttributes=FontAttributes.Bold});var answer=new VerticalStackLayout{Spacing=6};card.Add(answer);var info=new Label{FontSize=11};card.Add(info);card.Add(Button("复制回答",()=>Clipboard.Default.SetTextAsync(turn.Answer)));
                    bubbles[turn.Id]=(answer,info,"");messages.Add(new Border{Padding=0,Content=card,StrokeShape=new Microsoft.Maui.Controls.Shapes.RoundRectangle{CornerRadius=8}});
                }
            }
            foreach(var turn in s.Turns.TakeLast(visible))if(bubbles.TryGetValue(turn.Id,out var bubble))
            {
                bubble.Status.Text=turn.Status;
                if(bubble.Text!=turn.Answer){FillMarkdown(bubble.Body,turn.Answer);bubbles[turn.Id]=(bubble.Body,bubble.Status,turn.Answer);if(follow)Dispatcher.Dispatch(async()=>await scroll.ScrollToAsync(0,messages.Height,false));}
            }
            string pending=string.Join(',',s.PendingAttachments.Select(x=>$"{x.Id}:{x.PrivacyReviewed}"));
            if(pending!=pendingKey)
            {
                pendingKey=pending;attachments.Clear();foreach(var saved in s.PendingAttachments)
                {var row=new Grid{ColumnSpacing=4,ColumnDefinitions={new(GridLength.Star),new(GridLength.Auto)}};row.Add(Button(saved.Label,()=>ViewAttachment(saved)));var remove=Button("移除",()=>{s.RemoveAttachment(saved.Id);return Task.CompletedTask;});Grid.SetColumn(remove,1);row.Add(remove);attachments.Add(row);}
            }
            imageConsentRow.IsVisible=s.PendingAttachments.Any(x=>x.IsImage);imageConsent.IsChecked=s.PendingAttachments.Where(x=>x.IsImage).All(x=>x.PrivacyReviewed);
        }
        finally{rendering=false;}
    }
    private void FillMarkdown(VerticalStackLayout body,string text)
    {
        body.Clear();foreach(var block in AiMarkdown.Parse(text))
        {
            if(block.Code){body.Add(new Label{Text=block.Text,FontFamily="monospace",FontSize=12});body.Add(Button("复制代码",()=>Clipboard.Default.SetTextAsync(block.Text)));continue;}
            var label=new Label{FontSize=block.Heading is >0 and <=6?20:14};var formatted=new FormattedString();
            foreach(var part in AiMarkdown.Inline(block.Text))formatted.Spans.Add(new Span{Text=part.Text,FontAttributes=part.Bold||block.Heading>0?FontAttributes.Bold:FontAttributes.None,FontFamily=part.Code?"monospace":null});label.FormattedText=formatted;body.Add(label);
        }
    }
    private async Task FileAsync()
    {
        presentingReview=true;
        try{var result=await FilePicker.Default.PickAsync(new PickOptions{PickerTitle="附加文本或图片"});if(result is null)return;await using var stream=await result.OpenReadAsync();state.Session.AddAttachment(await reader.ReadAsync(result.FileName,stream,context:state.Session.Context));}
        finally{presentingReview=false;}
    }
    private async Task ViewAttachment(AiAttachment attachment)
    {
        View view=attachment.ImageBase64 is {} image?new Image{Source=ImageSource.FromStream(()=>new MemoryStream(Convert.FromBase64String(image))),HeightRequest=400,Aspect=Aspect.AspectFit}:TextView(attachment.Text??"");
        await Review(attachment.Name,view,"关闭");
    }
    private async Task ChatsAsync()
    {
        await state.Session.RefreshConversationsAsync();var search=new SearchBar{Placeholder="搜索标题或本地 UID"};var list=new CollectionView{SelectionMode=SelectionMode.Single,HeightRequest=420};
        list.ItemTemplate=new DataTemplate(()=>{var label=new Label{Padding=10};label.SetBinding(Label.TextProperty,"Label");return label;});
        void Filter()=>list.ItemsSource=state.Session.Conversations.Where(x=>x.Label.Contains(search.Text??"",StringComparison.OrdinalIgnoreCase)).ToArray();
        search.TextChanged+=(_,_)=>Filter();Filter();var body=new VerticalStackLayout();body.Add(search);body.Add(list);
        if(await Review("本地对话",body,"打开选中")==AiReviewChoice.Send&&list.SelectedItem is AiConversationSummary selected)await state.Session.OpenConversationAsync(selected.Id);
    }
    private async Task MenuAsync()
    {
        string? choice=await DisplayActionSheetAsync("对话操作","取消",null,"重命名","置顶/取消置顶","删除会话","查看资料与证据","导出 Markdown","复制全部");
        if(choice=="重命名"){string? value=await DisplayPromptAsync("重命名对话","标题",initialValue:state.Session.Title,maxLength:120);if(value is not null)await state.Session.RenameAsync(value);}
        else if(choice=="置顶/取消置顶")await state.Session.TogglePinAsync();
        else if(choice=="删除会话"){if(await DisplayAlertAsync("删除本地会话","只删除此会话及其中的附件副本，原始文件和档案数据不受影响。","删除","取消"))await state.Session.DeleteCurrentAsync();}
        else if(choice=="查看资料与证据")await EvidenceAsync();
        else if(choice=="复制全部")await Clipboard.Default.SetTextAsync(state.Session.CreateMarkdownReport());
        else if(choice=="导出 Markdown")
        {await using var stream=new MemoryStream(Encoding.UTF8.GetBytes(state.Session.CreateMarkdownReport()));var result=await PlatformExportFileSaver.Default.SaveAsync($"furina-chat-{DateTime.Now:yyyyMMdd-HHmmss}.md",stream);if(!result.IsSuccessful&&!result.IsCanceled)throw new AiAnalysisException("导出未完成。");}
    }
    private async Task DataAsync()
    {
        var archive=new Picker{Title="档案",ItemsSource=state.Archives.ToArray(),ItemDisplayBinding=new Binding("Label")};archive.SelectedItem=state.Archives.FirstOrDefault(x=>x.Id==state.SelectedArchiveId);
        var account=new Picker{Title="账号/整个档案",ItemsSource=state.Accounts.ToArray(),ItemDisplayBinding=new Binding("Label")};account.SelectedItem=state.Accounts.FirstOrDefault(x=>x.Id==state.SelectedAccountId);
        archive.SelectedIndexChanged+=async(_,_)=>{if(archive.SelectedItem is HistoryChoice a){await state.SelectArchiveAsync(a.Id);account.ItemsSource=state.Accounts.ToArray();account.SelectedItem=state.Accounts.FirstOrDefault(x=>x.Id==state.SelectedAccountId);}};
        var from=new Entry{Placeholder="UTC 起始 YYYY-MM-DD（可空）"};var to=new Entry{Placeholder="UTC 结束 YYYY-MM-DD（含当日，可空）"};var uids=new CheckBox{IsChecked=true};
        var categories=new[]{new CheckBox{IsChecked=true},new CheckBox{IsChecked=true},new CheckBox{IsChecked=true},new CheckBox{IsChecked=true}};string[] labels=["抽卡","角色/武器","挑战","游戏历程"];
        var body=new VerticalStackLayout{Spacing=6};body.Add(new Label{Text="附加/更换资料将另开对话并保留原对话。"});body.Add(archive);body.Add(account);body.Add(from);body.Add(to);body.Add(Check(uids,"整个档案允许 UID；每次发送仍须确认"));for(int i=0;i<4;i++)body.Add(Check(categories[i],labels[i]));
        if(await Review("附加档案资料",body,"准备并附加")!=AiReviewChoice.Send)return;
        if(account.SelectedItem is AiAccountChoice selected)state.SelectAccount(selected.Id);AiDatasets data=AiDatasets.None;for(int i=0;i<4;i++)if(categories[i].IsChecked)data|=(AiDatasets)(1<<i);
        await state.PrepareAsync(Date(from.Text),Date(to.Text),data,uids.IsChecked);renderKey="";
    }
    private async Task EvidenceAsync()
    {
        if(state.Session.Context is not {} context){await DisplayAlertAsync("资料","当前未附加档案资料。","确定");return;}
        var list=new CollectionView{ItemsSource=context.Evidence,SelectionMode=SelectionMode.Single,HeightRequest=220,ItemTemplate=new DataTemplate(()=>{var l=new Label{Padding=6};l.SetBinding(Label.TextProperty,"DisplayLabel");return l;})};
        var body=new VerticalStackLayout();body.Add(TextView(context.Json));body.Add(list);
        if(await Review("资料与本地证据",body,"查看选中证据")==AiReviewChoice.Send&&list.SelectedItem is AiLocalEvidence item)
        {var detail=await state.ReadEvidenceAsync(item);await Review("证据 "+item.Fact.Reference,TextView(detail.Availability+"\n"+JsonSerializer.Serialize(detail.Fact,AiJson.Options)),"关闭");}
    }
    private async Task SendAsync()
    {
        await state.Session.LoadSettingsAsync();
        for(int attempt=0;attempt<2;attempt++)
        {
            var draft=state.CreateDraft(composer.Text??"");var body=new VerticalStackLayout{Spacing=6};
            body.Add(new Label{Text=$"接收方：{draft.Settings.Endpoint}\n模型：{draft.Settings.Model}\n本次省略旧轮数 {draft.OmittedTurns}（本地仍保留）。"+(draft.IncludesUids?"\n此次携带档案 UID，须明确同意。":"\n游戏资料不发送 UID。")});
            foreach(var image in draft.Attachments.Where(x=>x.IsImage))body.Add(new Image{Source=ImageSource.FromStream(()=>new MemoryStream(Convert.FromBase64String(image.ImageBase64!))),HeightRequest=140});body.Add(TextView(draft.Preview));
            var choice=await Review("确认本次发送",body,draft.IncludesUids?"同意携带 UID 并发送":"确认发送",draft.IncludesUids?"改用编号":null);
            if(choice==AiReviewChoice.Anonymous){await state.Session.UseAnonymousContextAsync();continue;}
            if(choice==AiReviewChoice.Send){follow=true;await state.GenerateAsync(draft,true,draft.IncludesUids);composer.Text=state.Session.DraftText;}return;
        }
    }
    private async Task<AiReviewChoice> Review(string title,View content,string primary,string? secondary=null)
    {
        presentingReview=true;try{var page=new AiReviewPage(title,content,primary,secondary);await Navigation.PushModalAsync(page);return await page.Completion;}finally{presentingReview=false;}
    }
    private static Editor TextView(string text)=>new(){Text=text,IsReadOnly=true,HeightRequest=300,AutomationId="AiSendReview"};
    private static View Check(CheckBox box,string text){var row=new Grid{ColumnDefinitions={new(GridLength.Auto),new(GridLength.Star)}};row.Add(box);var label=new Label{Text=text,VerticalOptions=LayoutOptions.Center};Grid.SetColumn(label,1);row.Add(label);return row;}
    private static DateOnly? Date(string? value)=>string.IsNullOrWhiteSpace(value)?null:DateOnly.TryParseExact(value,"yyyy-MM-dd",out var date)?date:throw new AiAnalysisException("日期须为 YYYY-MM-DD。");
    private async Task UiAsync(Func<Task> action){try{await action();}catch(OperationCanceledException){}catch(Exception e){notifications.Publish(new(NotificationSeverity.Warning,NotificationCategory.System,"AI 聊天",e is AiAnalysisException?e.Message:"聊天操作未完成，请重试。"));}finally{Render();}}
}
internal enum AiReviewChoice {Cancel,Send,Anonymous}
internal sealed class AiReviewPage : ContentPage
{
    private readonly TaskCompletionSource<AiReviewChoice> result=new(TaskCreationOptions.RunContinuationsAsynchronously);private bool closing;
    public Task<AiReviewChoice> Completion=>result.Task;
    public AiReviewPage(string title,View content,string primary,string? secondary)
    {
        Title=title;var grid=new Grid{Padding=16,RowSpacing=8,RowDefinitions={new(GridLength.Auto),new(GridLength.Star),new(GridLength.Auto)}};grid.Add(new Label{Text=title,FontSize=22});var scroll=new ScrollView{Content=content};Grid.SetRow(scroll,1);grid.Add(scroll);var buttons=new VerticalStackLayout{Spacing=6};
        void Add(string text,AiReviewChoice choice){var b=new Button{Text=text};b.Clicked+=async(_,_)=>{if(closing)return;closing=true;await Navigation.PopModalAsync();result.TrySetResult(choice);};buttons.Add(b);}
        Add(primary,AiReviewChoice.Send);if(secondary is not null)Add(secondary,AiReviewChoice.Anonymous);Add("取消",AiReviewChoice.Cancel);Grid.SetRow(buttons,2);grid.Add(buttons);Content=grid;
    }
    protected override void OnDisappearing(){if(!closing)result.TrySetResult(AiReviewChoice.Cancel);base.OnDisappearing();}
}
