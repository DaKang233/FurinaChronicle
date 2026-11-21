// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Services.Ai;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace FurinaChronicle.Windows.Pages;
public sealed class AiSettingsPanel : StackPanel
{
    private readonly AiSettingsState state;
    private bool filling;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> showDialog;
    public bool IsBusy { get; private set; }
    private readonly ComboBox providers=new(){Header="供应商",DisplayMemberPath="Label"},profiles=new(){Header="已保存模型",DisplayMemberPath="Label"};
    private readonly TextBox name=new(){Header="供应商名称"},alias=new(){Header="模型显示名称（可选）"};
    private readonly ComboBox protocol=new(){Header="接口类型",DisplayMemberPath="Label"};
    private readonly TextBox userName=new(){Header="用户称呼（仅标记本地消息）"},systemPrompt=new(){Header="系统提示词",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=180};
    private readonly CheckBox allowTools=new(){Content="允许查询当前附加范围内的本地档案（只读）"};
    private readonly NumberBox maxTools=new(){Header="每个用户消息的最大工具调用次数（0～500）",Minimum=0,Maximum=500};
    private readonly TextBox address=new(){Header="服务 API 基址"},model=new(){Header="模型标识"};
    private readonly PasswordBox key=new(){Header="此供应商密钥（留空保留；更换地址或协议不携带旧密钥）"};
    private readonly NumberBox budget=new(){Header="上下文额度（字符）",Minimum=4000,Maximum=1_000_000},output=new(){Header="输出额度（token）",Minimum=128,Maximum=8192},context=new(){Header="Ollama 上下文（token）",Minimum=2048,Maximum=131072};
    private readonly CheckBox images=new(){Content="此模型支持图片输入"},thinking=new(){Content="关闭兼容服务思考模式（仅支持 thinking 参数的服务）"},cpu=new(){Content="Ollama 仅 CPU"};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    public AiSettingsPanel(AiSettingsState state, Func<ContentDialog, Task<ContentDialogResult>> showDialog)
    {
        this.state=state;this.showDialog=showDialog;Spacing=8;AutomationProperties.SetAutomationId(this,"AiSettingsPanel");
        Children.Add(new TextBlock{Text="全局聊天配置",FontSize=20});
        foreach(var control in new UIElement[]{userName,systemPrompt,allowTools,maxTools})Children.Add(control);
        Children.Add(new TextBlock{Text="工具默认关闭；开启后只查询本次已附加的证据/统计快照，结果发送给本轮服务。无资料时不启用，0 次也表示关闭。",TextWrapping=TextWrapping.Wrap});
        Children.Add(Command("保存全局配置",async()=>{await state.SavePreferencesAsync(new(userName.Text.Trim(),systemPrompt.Text,allowTools.IsChecked==true,(int)maxTools.Value));FillPreferences();status.Text="全局聊天配置已保存。";},"AiSaveChatPreferences"));
        AutomationProperties.SetAutomationId(userName,"AiUserDisplayName");AutomationProperties.SetAutomationId(systemPrompt,"AiSystemPrompt");AutomationProperties.SetAutomationId(allowTools,"AiAllowLocalTools");AutomationProperties.SetAutomationId(maxTools,"AiMaxToolCalls");
        Children.Add(new TextBlock{Text="AI 供应商与模型",FontSize=20,Margin=new Thickness(0,16,0,0)});
        Children.Add(new TextBlock{Text="先保存当前编辑内容，再切换编辑项。保存后设为新对话的默认模型；已有对话保留自己的模型选择。",TextWrapping=TextWrapping.Wrap});
        Children.Add(providers);Children.Add(Row(Command("添加供应商",()=>{state.NewProvider();Fill();return Task.CompletedTask;},"AiAddProvider"),Command("删除供应商",DeleteProviderAsync,"AiDeleteProvider")));
        foreach(var c in new UIElement[]{name,protocol,address,key,profiles})Children.Add(c);
        Children.Add(Row(Command("添加模型",()=>{state.NewModel();Fill();return Task.CompletedTask;},"AiAddModel"),Command("删除模型",DeleteModelAsync,"AiDeleteModel")));
        foreach(var c in new UIElement[]{alias,model,budget,output,images,thinking,context,cpu})Children.Add(c);
        Children.Add(Row(Command("保存 AI 设置",async()=>{await state.SaveProfileAsync(name.Text,alias.Text,Inputs(),string.IsNullOrWhiteSpace(key.Password)?null:key.Password);Fill();status.Text="已保存并设为新对话默认模型；已有会话选择保留。";},"AiSaveSettings"),Command("获取模型列表",DiscoverAsync,"AiDiscoverModels"),Command("移除此供应商密钥",async()=>{await state.ClearKeyAsync(Inputs());key.Password="";status.Text="已移除此供应商当前地址的密钥。";},"AiClearKey")));
        Children.Add(status);
        AutomationProperties.SetAutomationId(providers,"AiProviderProfiles");AutomationProperties.SetAutomationId(profiles,"AiModelProfiles");AutomationProperties.SetAutomationId(name,"AiProviderName");AutomationProperties.SetAutomationId(alias,"AiModelName");
        AutomationProperties.SetAutomationId(protocol,"AiProtocol");AutomationProperties.SetAutomationId(address,"AiBaseAddress");AutomationProperties.SetAutomationId(model,"AiModel");AutomationProperties.SetAutomationId(key,"AiApiKey");
        protocol.SelectionChanged+=(_,_)=>UpdateProtocolFields();
        providers.SelectionChanged+=(_,_)=>{if(!filling&&providers.SelectedItem is AiProviderProfile p){state.EditProvider(p.Id);Fill();}};
        profiles.SelectionChanged+=(_,_)=>{if(!filling&&profiles.SelectedItem is AiModelProfile m){state.EditModel(m.Id);Fill();}};
    }
    private static StackPanel Row(params UIElement[] elements){var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};foreach(var e in elements)row.Children.Add(e);return row;}
    private Button Command(string label,Func<Task> action,string id){var b=new Button{Content=label};AutomationProperties.SetAutomationId(b,id);b.Click+=async(_,_)=>await Run(action);return b;}
    public async Task LoadAsync()=>await Run(async()=>{await state.LoadAsync();Fill();FillPreferences();});
    private void Fill()
    {
        filling=true;
        try
        {
            var p=state.EditingProvider;var s=state.Settings;
            providers.ItemsSource=state.Catalog.Providers;providers.SelectedItem=state.Catalog.Find(p?.Id);
            profiles.ItemsSource=p?.Models;profiles.SelectedItem=p?.Models.FirstOrDefault(m=>m.Id==state.EditingModelId);
            name.Text=p?.Name??"";alias.Text=p?.Models.FirstOrDefault(m=>m.Id==state.EditingModelId)?.Name??"";
            var choices=AiProtocols.Choices(s.Protocol);protocol.ItemsSource=choices;protocol.SelectedItem=choices.First(x=>x.Value==s.Protocol);address.Text=s.BaseAddress;model.Text=s.Model;
            budget.Value=s.ContextCharacters;output.Value=s.MaxOutputTokens;context.Value=s.OllamaContextTokens;
            images.IsChecked=s.SupportsImages;thinking.IsChecked=s.DisableCompatibleThinking;cpu.IsChecked=s.CpuOnly;key.Password="";
        }
        finally{filling=false;}
    }
    private void FillPreferences(){var p=state.Catalog.Preferences;userName.Text=p.UserDisplayName;systemPrompt.Text=p.SystemPrompt;allowTools.IsChecked=p.AllowLocalTools;maxTools.Value=p.MaxToolCalls;}
    private void UpdateProtocolFields()
    {var p=(protocol.SelectedItem as AiProtocolChoice)?.Value;thinking.Visibility=p==AiProtocol.OpenAiCompatible?Visibility.Visible:Visibility.Collapsed;context.Visibility=cpu.Visibility=p==AiProtocol.Ollama?Visibility.Visible:Visibility.Collapsed;}
    private AiServiceSettings Inputs()=>state.Settings with {Protocol=(protocol.SelectedItem as AiProtocolChoice)?.Value??AiProtocol.OpenAiCompatible,BaseAddress=address.Text.Trim(),Model=model.Text.Trim(),ContextCharacters=(int)budget.Value,MaxOutputTokens=(int)output.Value,OllamaContextTokens=(int)context.Value,CpuOnly=cpu.IsChecked==true,DisableCompatibleThinking=thinking.IsChecked==true,SupportsImages=images.IsChecked==true};
    private async Task DiscoverAsync()
    {
        var list=new ListView{ItemsSource=await state.ModelsAsync(Inputs(),string.IsNullOrWhiteSpace(key.Password)?null:key.Password),MaxHeight=360};
        var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="模型列表（不发送聊天或档案）",Content=list,PrimaryButtonText="填入所选模型",CloseButtonText="取消"};
        if(await showDialog(dialog)==ContentDialogResult.Primary&&list.SelectedItem is string value){model.Text=value;if(string.IsNullOrWhiteSpace(alias.Text))alias.Text=value;status.Text="已填入模型，请保存。图片能力需自行确认。";}
    }
    private async Task DeleteProviderAsync()
    {
        if(await showDialog(new ContentDialog{XamlRoot=XamlRoot,Title="删除供应商？",Content="删除配置及当前地址密钥，保留所有聊天；使用该配置的会话须重新选择模型。",PrimaryButtonText="删除",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close})!=ContentDialogResult.Primary)return;
        await state.DeleteProviderAsync();Fill();status.Text="供应商已删除，聊天记录保留。";
    }
    private async Task DeleteModelAsync()
    {
        if(await showDialog(new ContentDialog{XamlRoot=XamlRoot,Title="删除模型配置？",Content="保留历史回答，使用该模型配置的会话发送前须重新选择。",PrimaryButtonText="删除",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close})!=ContentDialogResult.Primary)return;
        await state.DeleteModelAsync();Fill();
    }
    private static void EnableControls(Panel panel,bool enabled)
    {foreach(var child in panel.Children){if(child is Control control)control.IsEnabled=enabled;else if(child is Panel nested)EnableControls(nested,enabled);}}
    private async Task Run(Func<Task> action)
    {if(IsBusy)return;IsBusy=true;EnableControls(this,false);try{await action();}catch(Exception e){status.Text=e is AiAnalysisException?e.Message:"AI 设置操作未完成，请检查本地存储或服务。";}finally{IsBusy=false;EnableControls(this,true);}}
}
