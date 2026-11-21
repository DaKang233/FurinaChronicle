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
    private readonly ComboBox protocol=new(){Header="接口类型",ItemsSource=new[]{"OpenAI 兼容","Ollama"}};
    private readonly TextBox address=new(){Header="服务 API 基址"},model=new(){Header="模型"};
    private readonly PasswordBox key=new(){Header="密钥（留空保留；仅保存到本机安全存储）"};
    private readonly NumberBox budget=new(){Header="上下文额度（字符）",Minimum=4000,Maximum=1_000_000},output=new(){Header="输出额度（token）",Minimum=128,Maximum=8192},context=new(){Header="Ollama 上下文（token）",Minimum=2048,Maximum=131072};
    private readonly CheckBox images=new(){Content="此模型支持图片输入"},thinking=new(){Content="关闭兼容服务思考模式（仅支持 thinking 参数的服务）"},cpu=new(){Content="Ollama 仅 CPU"};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Button save=new(){Content="保存 AI 设置"},models=new(){Content="获取模型列表"},clear=new(){Content="移除此服务密钥"};
    public AiSettingsPanel(AiSettingsState state)
    {
        this.state=state;Spacing=8;AutomationProperties.SetAutomationId(this,"AiSettingsPanel");
        Children.Add(new TextBlock{Text="AI 服务与聊天",FontSize=20});
        foreach(var c in new UIElement[]{protocol,address,model,key,budget,output,images,thinking,context,cpu,save,models,clear,status})Children.Add(c);
        AutomationProperties.SetAutomationId(address,"AiBaseAddress");AutomationProperties.SetAutomationId(model,"AiModel");AutomationProperties.SetAutomationId(key,"AiApiKey");AutomationProperties.SetAutomationId(save,"AiSaveSettings");
        save.Click+=async(_,_)=>await Run(async()=>{await state.SaveAsync(Inputs(),string.IsNullOrWhiteSpace(key.Password)?null:key.Password);key.Password="";status.Text="AI 设置已保存，聊天记录保留；下次发送将使用此配置。";});
        clear.Click+=async(_,_)=>await Run(async()=>{await state.SaveAsync(Inputs(),null,true);key.Password="";status.Text="已移除此服务密钥。";});
        models.Click+=async(_,_)=>await Run(async()=>
        {
            var list=new ListView{ItemsSource=await state.ModelsAsync(Inputs(),string.IsNullOrWhiteSpace(key.Password)?null:key.Password),MaxHeight=360};
            var dialog=new ContentDialog{XamlRoot=XamlRoot,Title="模型列表（不发送聊天或档案）",Content=list,PrimaryButtonText="使用所选模型",CloseButtonText="取消"};
            if(await dialog.ShowAsync()==ContentDialogResult.Primary&&list.SelectedItem is string name)model.Text=name;
        });
    }
    public async Task LoadAsync()=>await Run(async()=>
    {
        await state.LoadAsync();var s=state.Settings;protocol.SelectedIndex=(int)s.Protocol;address.Text=s.BaseAddress;model.Text=s.Model;
        budget.Value=s.ContextCharacters;output.Value=s.MaxOutputTokens;context.Value=s.OllamaContextTokens;images.IsChecked=s.SupportsImages;thinking.IsChecked=s.DisableCompatibleThinking;cpu.IsChecked=s.CpuOnly;key.Password="";
    });
    private AiServiceSettings Inputs()=>new((AiProtocol)Math.Max(0,protocol.SelectedIndex),address.Text.Trim(),model.Text.Trim(),(int)budget.Value,(int)output.Value,(int)context.Value,cpu.IsChecked==true,thinking.IsChecked==true,images.IsChecked==true);
    private async Task Run(Func<Task> action)
    {save.IsEnabled=models.IsEnabled=clear.IsEnabled=false;try{await action();}catch(Exception e){status.Text=e is AiAnalysisException?e.Message:"AI 设置操作未完成，请检查本地存储或服务。";}finally{save.IsEnabled=models.IsEnabled=clear.IsEnabled=true;}}
}
