// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text.Json;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Ai;
using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Microsoft.UI.Text;

namespace FurinaChronicle.Windows.Pages;
public sealed partial class AiPage : UserControl
{
    private readonly AiWorkspaceState state;
    private readonly INotificationCenter notifications;
    private readonly IFilePickerService files;
    private readonly AiAttachmentReader reader;
    private readonly Action settings;
    private bool rendering,entered,follow=true;
    private Guid renderedId;
    private int visible=40;
    private string renderedTurns="",renderedChats="",renderedPending="";
    private readonly Dictionary<Guid,(StackPanel Body,TextBlock Status,string Text)> bubbles=[];
    [DllImport("user32.dll")]private static extern short GetKeyState(int key);
    public AiPage(AiWorkspaceState state,INotificationCenter notifications,IFilePickerService files,AiAttachmentReader reader,Action settings)
    {
        this.state=state;this.notifications=notifications;this.files=files;this.reader=reader;this.settings=settings;
        InitializeComponent();state.Initialize();state.Changed+=(_,_)=>{if(DispatcherQueue.HasThreadAccess)Render();else DispatcherQueue.TryEnqueue(Render);};
    }
    public async Task EnterAsync(){entered=true;await state.EnterAsync();Render();}
    public void Leave()=>_=LeaveAsync();
    public async Task LeaveAsync(){if(!entered)return;entered=false;state.Leave();if(state.Session.IsInitialized)await UiAsync(()=>state.Session.SaveAsync());}
    private Button Button(string text,Func<Task> action)
    {var b=new Button{Content=text};b.Click+=async(_,_)=>await UiAsync(action);return b;}
    private static void Copy(string text){var data=new DataPackage();data.SetText(text);Clipboard.SetContent(data);}
    private async Task OpenAttachment(AiAttachment file)
    {
        object content;
        if(file.ImageBase64 is {} image)
        {using var stream=new MemoryStream(Convert.FromBase64String(image));var bitmap=new BitmapImage{DecodePixelHeight=600};await bitmap.SetSourceAsync(stream.AsRandomAccessStream());content=new Image{Source=bitmap,MaxHeight=500,Stretch=Stretch.Uniform};}
        else content=Preview(file.Text??"");
        await new ContentDialog{XamlRoot=XamlRoot,Title=file.Name,Content=content,CloseButtonText="关闭"}.ShowAsync();
    }
    private async void OnModel(object sender, RoutedEventArgs e)=>await UiAsync(async()=>
    {
        var session=state.Session;await session.LoadSettingsAsync();
        if(session.Providers.Providers.Count==0)throw new AiAnalysisException("请先在设置页添加供应商和模型。");
        var provider=new ComboBox{Header="供应商",ItemsSource=session.Providers.Providers,DisplayMemberPath="Label",HorizontalAlignment=HorizontalAlignment.Stretch};
        var model=new ComboBox{Header="模型",DisplayMemberPath="Label",HorizontalAlignment=HorizontalAlignment.Stretch};
        AutomationProperties.SetAutomationId(provider,"AiChatProvider");AutomationProperties.SetAutomationId(model,"AiChatModel");
        void Update(){var p=provider.SelectedItem as AiProviderProfile;model.ItemsSource=p?.Models;model.SelectedItem=p?.Models.FirstOrDefault(m=>m.Id==(p.Id==session.Settings.ProviderId?session.Settings.ModelProfileId:p.SelectedModelId))??p?.Models.FirstOrDefault();}
        provider.SelectionChanged+=(_,_)=>Update();provider.SelectedItem=session.Providers.Find(session.Settings.ProviderId)??session.Providers.Providers.FirstOrDefault();Update();
        var body=new StackPanel{Spacing=8};body.Children.Add(provider);body.Children.Add(model);body.Children.Add(new TextBlock{Text="仅切换配置，不发送资料。下一次发送将审查接收方与历史。",TextWrapping=TextWrapping.Wrap});
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="选择供应商与模型",Content=body,PrimaryButtonText="切换",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary&&provider.SelectedItem is AiProviderProfile p&&model.SelectedItem is AiModelProfile m)await session.SelectModelAsync(p.Id,m.Id);
    });
    private void Render()
    {
        if(rendering)return;rendering=true;
        try
        {
            var s=state.Session;TitleText.Text=s.Title;ModelText.Text=s.SelectionMissing?"原配置已删除，请重新选择":string.IsNullOrWhiteSpace(s.Settings.Model)?"请先在设置页配置模型":s.ProviderName+" · "+s.Settings.Model;ModelButton.IsEnabled=!state.IsBusy;
            StatusText.Text=state.Status+" "+s.StorageNotice;
            SendButton.IsEnabled=FileButton.IsEnabled=DataButton.IsEnabled=!state.IsBusy;CancelButton.IsEnabled=state.IsBusy;
            ContextText.Text=s.Context is {} c?$"已附加 {c.Evidence.Count} 项游戏证据 · "+(c.IncludesUids?"档案 UID 每次发送须确认":"不发送 UID"):"普通聊天 · 未附加档案数据";
            string chatKey=SearchInput.Text+string.Join("|",s.Conversations.Select(x=>$"{x.Id}:{x.Title}:{x.Pinned}:{x.Turns}"));
            if(chatKey!=renderedChats){renderedChats=chatKey;ChatList.ItemsSource=s.Conversations.Where(x=>x.Label.Contains(SearchInput.Text,StringComparison.OrdinalIgnoreCase)).ToArray();ChatList.SelectedItem=s.Conversations.FirstOrDefault(x=>x.Id==s.ConversationId);}
            string key=s.ConversationId+":"+string.Join(',',s.Turns.Select(x=>x.Id));
            if(key!=renderedTurns)
            {
                if(renderedId!=s.ConversationId){visible=40;renderedId=s.ConversationId;QuestionInput.Text=s.DraftText;follow=true;}
                renderedTurns=key;MessagesPanel.Children.Clear();bubbles.Clear();
                if(s.Turns.Count>visible)MessagesPanel.Children.Add(Button("加载更早消息",()=>{visible+=40;renderedTurns="";Render();return Task.CompletedTask;}));
                foreach(var turn in s.Turns.TakeLast(visible))
                {
                    var card=new StackPanel{Spacing=8};card.Children.Add(new TextBlock{Text="你 · "+turn.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"),FontWeight=FontWeights.SemiBold});
                    card.Children.Add(new TextBlock{Text=turn.Question,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true});
                    foreach(var file in turn.Attachments){var saved=file;card.Children.Add(Button("附件 · "+file.Label,()=>OpenAttachment(saved)));}
                    var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
                    actions.Children.Add(Button("复制问题",()=>{Copy(turn.Question);return Task.CompletedTask;}));
                    actions.Children.Add(Button("编辑/重新生成",async()=>{QuestionInput.Text=await s.ForkFromAsync(turn.Id);renderedTurns="";Render();}));card.Children.Add(actions);
                    card.Children.Add(new TextBlock{Text="助手 · "+turn.ModelLabel,FontWeight=FontWeights.SemiBold});var body=new StackPanel{Spacing=6};card.Children.Add(body);
                    var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Opacity=0.65};card.Children.Add(status);
                    card.Children.Add(Button("复制回答",()=>{Copy(turn.Answer);return Task.CompletedTask;}));bubbles[turn.Id]=(body,status,"");
                    MessagesPanel.Children.Add(new Border{Padding=new Thickness(14),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=(Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],Child=card});
                }
            }
            foreach(var turn in s.Turns.TakeLast(visible))if(bubbles.TryGetValue(turn.Id,out var bubble))
            {
                bubble.Status.Text=turn.Status;
                if(bubble.Text!=turn.Answer){FillMarkdown(bubble.Body,turn.Answer);bubbles[turn.Id]=(bubble.Body,bubble.Status,turn.Answer);if(follow)DispatcherQueue.TryEnqueue(()=>MessagesScroll.ChangeView(null,MessagesScroll.ScrollableHeight,null));}
            }
            string pending=string.Join(',',s.PendingAttachments.Select(x=>$"{x.Id}:{x.PrivacyReviewed}"));
            if(pending!=renderedPending)
            {
                renderedPending=pending;AttachmentsPanel.Children.Clear();
                foreach(var file in s.PendingAttachments)
                {
                    var row=new Grid{ColumnSpacing=6};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                    var open=Button(file.Label,()=>OpenAttachment(file));open.HorizontalAlignment=HorizontalAlignment.Stretch;row.Children.Add(open);
                    var remove=Button("移除",()=>{s.RemoveAttachment(file.Id);return Task.CompletedTask;});Grid.SetColumn(remove,1);row.Children.Add(remove);AttachmentsPanel.Children.Add(row);
                }
                ImageConsent.Visibility=s.PendingAttachments.Any(x=>x.IsImage)?Visibility.Visible:Visibility.Collapsed;
                ImageConsent.IsChecked=s.PendingAttachments.Where(x=>x.IsImage).All(x=>x.PrivacyReviewed);
            }
        }
        finally{rendering=false;}
    }
    private void FillMarkdown(StackPanel body,string answer)
    {
        body.Children.Clear();
        foreach(var block in AiMarkdown.Parse(answer))
        {
            if(block.Code){var code=Preview(block.Text);code.Height=double.NaN;code.MaxHeight=360;body.Children.Add(code);body.Children.Add(Button("复制代码",()=>{Copy(block.Text);return Task.CompletedTask;}));continue;}
            var text=new RichTextBlock{IsTextSelectionEnabled=true,TextWrapping=TextWrapping.Wrap,FontSize=block.Heading is >0 and <=6?20:14};var paragraph=new Paragraph();
            foreach(var token in AiMarkdown.Inline(block.Text))paragraph.Inlines.Add(new Run{Text=token.Text,FontWeight=token.Bold||block.Heading>0?FontWeights.SemiBold:FontWeights.Normal,FontFamily=token.Code?new FontFamily("Consolas"):FontFamily});
            text.Blocks.Add(paragraph);body.Children.Add(text);
        }
    }
    private void OnScrollChanged(object sender,ScrollViewerViewChangedEventArgs e){if(!e.IsIntermediate)follow=MessagesScroll.ScrollableHeight-MessagesScroll.VerticalOffset<80;}
    private void OnBottom(object sender,RoutedEventArgs e){follow=true;MessagesScroll.ChangeView(null,MessagesScroll.ScrollableHeight,null);}
    private void OnDraftChanged(object sender,TextChangedEventArgs e){if(!rendering)state.Session.UpdateDraft(QuestionInput.Text);}
    private void OnSearch(object sender,TextChangedEventArgs e){if(!rendering)Render();}
    private async void OnChatSelected(object sender,SelectionChangedEventArgs e){if(!rendering&&ChatList.SelectedItem is AiConversationSummary c&&c.Id!=state.Session.ConversationId)await UiAsync(async()=>{await state.Session.OpenConversationAsync(c.Id);Render();});}
    private async void OnNew(object sender,RoutedEventArgs e)=>await UiAsync(()=>state.Session.NewConversationAsync());
    private void OnSettings(object sender,RoutedEventArgs e)=>settings();
    private void OnStop(object sender,RoutedEventArgs e)=>state.Cancel();
    private void OnImageConsent(object sender,RoutedEventArgs e){if(!rendering)state.Session.ReviewImages(ImageConsent.IsChecked==true);}
    private async void OnRename(object sender,RoutedEventArgs e)=>await UiAsync(async()=>
    {var input=new TextBox{Text=state.Session.Title,MaxLength=120};if(await Dialog("重命名对话",input,"保存")==ContentDialogResult.Primary)await state.Session.RenameAsync(input.Text);});
    private async void OnPin(object sender,RoutedEventArgs e)=>await UiAsync(()=>state.Session.TogglePinAsync());
    private async void OnDelete(object sender,RoutedEventArgs e)=>await UiAsync(async()=>
    {if(await Dialog("删除本地对话？",new TextBlock{Text="将删除此会话及其中的附件副本。原始文件、档案数据和其他会话不受影响。",TextWrapping=TextWrapping.Wrap},"删除")==ContentDialogResult.Primary)await state.Session.DeleteCurrentAsync();});
    private async void OnFile(object sender,RoutedEventArgs e)=>await UiAsync(async()=>
    {var file=await files.PickOpenFileAsync("*");if(file is null)return;await using var stream=await file.OpenStreamForReadAsync();state.Session.AddAttachment(await reader.ReadAsync(file.Name,stream,context:state.Session.Context));});
    private async void OnData(object sender,RoutedEventArgs e)=>await UiAsync(async()=>
    {
        var archive=new ComboBox{Header="档案",ItemsSource=state.Archives,DisplayMemberPath="Label",HorizontalAlignment=HorizontalAlignment.Stretch};archive.SelectedItem=state.Archives.FirstOrDefault(x=>x.Id==state.SelectedArchiveId);
        var account=new ComboBox{Header="账号/整个档案",ItemsSource=state.Accounts,DisplayMemberPath="Label",HorizontalAlignment=HorizontalAlignment.Stretch};account.SelectedItem=state.Accounts.FirstOrDefault(x=>x.Id==state.SelectedAccountId);
        archive.SelectionChanged+=async(_,_)=>{if(archive.SelectedItem is HistoryChoice c){await state.SelectArchiveAsync(c.Id);account.ItemsSource=state.Accounts;account.SelectedItem=state.Accounts.FirstOrDefault(x=>x.Id==state.SelectedAccountId);}};
        var from=new TextBox{Header="UTC 起始日期（可空）",PlaceholderText="YYYY-MM-DD"};var to=new TextBox{Header="UTC 结束日期（含当日，可空）",PlaceholderText="YYYY-MM-DD"};
        var uids=new CheckBox{Content="整个档案允许 UID（每次发送仍须确认）",IsChecked=true};var categories=new[]{"抽卡","角色/武器","挑战","游戏历程"}.Select(x=>new CheckBox{Content=x,IsChecked=true}).ToArray();
        var body=new StackPanel{Spacing=6};body.Children.Add(new TextBlock{Text="附加或更换游戏资料会另开对话并保留原对话，避免不同证据混用。",TextWrapping=TextWrapping.Wrap});
        foreach(var view in new UIElement[]{archive,account,from,to,uids})body.Children.Add(view);foreach(var box in categories)body.Children.Add(box);
        if(await Dialog("附加档案资料",body,"准备并附加")!=ContentDialogResult.Primary)return;
        if(account.SelectedItem is AiAccountChoice a)state.SelectAccount(a.Id);AiDatasets data=AiDatasets.None;for(int i=0;i<4;i++)if(categories[i].IsChecked==true)data|=(AiDatasets)(1<<i);
        await state.PrepareAsync(Date(from.Text),Date(to.Text),data,uids.IsChecked==true);renderedTurns="";
    });
    private async void OnEvidence(object sender,RoutedEventArgs e)=>await UiAsync(async()=>
    {
        if(state.Session.Context is not {} c){await Dialog("资料","当前未附加游戏数据。","确定");return;}
        var list=new ListView{ItemsSource=c.Evidence,DisplayMemberPath="DisplayLabel",MaxHeight=220};var body=new StackPanel{Spacing=8};body.Children.Add(Preview(c.Json));body.Children.Add(list);
        if(await Dialog("本地资料和证据",body,"查看选中证据")==ContentDialogResult.Primary&&list.SelectedItem is AiLocalEvidence reference)
        {var detail=await state.ReadEvidenceAsync(reference);await Dialog("证据 "+reference.Fact.Reference,Preview(detail.Availability+"\n"+JsonSerializer.Serialize(detail.Fact,AiJson.Options)),"确定");}
    });
    private async void OnSend(object sender,RoutedEventArgs e)=>await UiAsync(SendAsync);
    private async Task SendAsync()
    {
        await state.Session.LoadSettingsAsync();
        for(int attempt=0;attempt<2;attempt++)
        {
            var draft=state.CreateDraft(QuestionInput.Text);var body=new StackPanel{Spacing=8};
            body.Children.Add(new TextBlock{Text=$"接收方：{draft.Settings.Endpoint}\n模型：{draft.Settings.Model}\n本次省略旧轮数：{draft.OmittedTurns}（仍保存在本地）。"+(draft.IncludesUids?"\n此次包含整个档案 UID，必须明确同意。":"\n此次游戏资料不包含 UID。"),TextWrapping=TextWrapping.Wrap});
            foreach(var file in draft.Attachments.Where(x=>x.IsImage))
            {using var stream=new MemoryStream(Convert.FromBase64String(file.ImageBase64!));var bitmap=new BitmapImage{DecodePixelHeight=180};await bitmap.SetSourceAsync(stream.AsRandomAccessStream());body.Children.Add(new Image{Source=bitmap,MaxHeight=140,Stretch=Stretch.Uniform});}
            body.Children.Add(Preview(draft.Preview));
            var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="确认本次发送",Content=new ScrollViewer{Content=body,MaxHeight=540},PrimaryButtonText=draft.IncludesUids?"同意携带 UID 并发送":"确认发送",SecondaryButtonText=draft.IncludesUids?"改用编号":"",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close};AutomationProperties.SetAutomationId(dialog,"AiSendReview");
            var result=await dialog.ShowAsync();
            if(result==ContentDialogResult.Secondary)
            {
                await state.Session.UseAnonymousContextAsync();continue;
            }
            if(result==ContentDialogResult.Primary){follow=true;await state.GenerateAsync(draft,true,draft.IncludesUids);QuestionInput.Text=state.Session.DraftText;}return;
        }
    }
    private void OnComposerKey(object sender,Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {if(e.Key==VirtualKey.Enter&&(GetKeyState(0x10)&0x8000)==0){e.Handled=true;_=UiAsync(SendAsync);}}
    private async void OnExport(object sender,RoutedEventArgs e)=>await UiAsync(async()=>
    {var file=await files.PickSaveFileAsync($"furina-chat-{DateTime.Now:yyyyMMdd-HHmmss}","Markdown",".md");if(file is not null)await FileIO.WriteTextAsync(file,state.Session.CreateMarkdownReport());});
    private Task<ContentDialogResult> Dialog(string title,object body,string primary)=>new ContentDialog{XamlRoot=XamlRoot,Title=title,Content=body,PrimaryButtonText=primary,CloseButtonText="取消",DefaultButton=ContentDialogButton.Close}.ShowAsync().AsTask();
    private static TextBox Preview(string value)=>new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=300,MinWidth=400,Text=value};
    private static DateOnly? Date(string value)=>string.IsNullOrWhiteSpace(value)?null:DateOnly.TryParseExact(value,"yyyy-MM-dd",out var date)?date:throw new AiAnalysisException("日期格式须为 YYYY-MM-DD。");
    private async Task UiAsync(Func<Task> work)
    {try{await work();}catch(OperationCanceledException){}catch(Exception e){notifications.Publish(new(NotificationSeverity.Warning,NotificationCategory.System,"AI 聊天",e is AiAnalysisException?e.Message:"聊天操作未完成，请重试。"));}finally{Render();}}
}
