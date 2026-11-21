// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Services.Ai;
namespace FurinaChronicle.App.Ai;
public sealed class AiSettingsView : ContentView
{
    private readonly AiSettingsState state;
    private readonly Picker protocol=new(){Title="接口类型",ItemsSource=new[]{"OpenAI 兼容","Ollama"}};
    private readonly Entry address=new(){Placeholder="API 基址",AutomationId="AiBaseAddress"},model=new(){Placeholder="模型",AutomationId="AiModel"},key=new(){Placeholder="密钥（留空保留）",IsPassword=true,IsTextPredictionEnabled=false};
    private readonly Entry budget=new(){Keyboard=Keyboard.Numeric},output=new(){Keyboard=Keyboard.Numeric},context=new(){Keyboard=Keyboard.Numeric};
    private readonly CheckBox images=new(),thinking=new(),cpu=new();
    private readonly Label status=new();
    private readonly Button save=new(){Text="保存 AI 设置",AutomationId="AiSaveSettings"},models=new(){Text="获取模型列表"},clear=new(){Text="移除此服务密钥"};
    public AiSettingsView(AiSettingsState state)
    {
        this.state=state;var body=new VerticalStackLayout{Spacing=8};Content=body;
        body.Add(new Label{Text="AI 服务与聊天",FontSize=20,FontAttributes=FontAttributes.Bold});
        foreach(var view in new View[]{protocol,address,model,key,new Label{Text="上下文字符额度 / 输出 token 额度"},budget,output,Check(images,"此模型支持图片输入"),Check(thinking,"关闭兼容服务思考（仅支持 thinking 参数的服务）"),new Label{Text="Ollama 上下文 token"},context,Check(cpu,"Ollama 仅 CPU"),save,models,clear,status})body.Add(view);
        save.Clicked+=async(_,_)=>await Run(async()=>{await state.SaveAsync(Inputs(),string.IsNullOrWhiteSpace(key.Text)?null:key.Text);key.Text="";status.Text="AI 设置已保存；聊天记录保留。";});
        clear.Clicked+=async(_,_)=>await Run(async()=>{await state.SaveAsync(Inputs(),null,true);key.Text="";status.Text="密钥已移除。";});
        models.Clicked+=async(_,_)=>await Run(async()=>{var choices=await state.ModelsAsync(Inputs(),string.IsNullOrWhiteSpace(key.Text)?null:key.Text);string? selected=await (Window?.Page??throw new AiAnalysisException("页面尚未就绪。")).DisplayActionSheetAsync("模型（不发送聊天）","取消",null,choices.ToArray());if(choices.Contains(selected))model.Text=selected;});
    }
    public async Task LoadAsync()=>await Run(async()=>{await state.LoadAsync();var s=state.Settings;protocol.SelectedIndex=(int)s.Protocol;address.Text=s.BaseAddress;model.Text=s.Model;budget.Text=s.ContextCharacters.ToString();output.Text=s.MaxOutputTokens.ToString();context.Text=s.OllamaContextTokens.ToString();cpu.IsChecked=s.CpuOnly;thinking.IsChecked=s.DisableCompatibleThinking;images.IsChecked=s.SupportsImages;key.Text="";});
    private AiServiceSettings Inputs()=>new((AiProtocol)Math.Max(0,protocol.SelectedIndex),address.Text?.Trim()??"",model.Text?.Trim()??"",Number(budget.Text),Number(output.Text),Number(context.Text),cpu.IsChecked,thinking.IsChecked,images.IsChecked);
    private static int Number(string? text)=>int.TryParse(text,out int n)?n:throw new AiAnalysisException("请输入有效整数。");
    private static View Check(CheckBox box,string text){var g=new Grid{ColumnDefinitions={new(GridLength.Auto),new(GridLength.Star)}};g.Add(box);var l=new Label{Text=text,VerticalOptions=LayoutOptions.Center};Grid.SetColumn(l,1);g.Add(l);return g;}
    private async Task Run(Func<Task> action){save.IsEnabled=models.IsEnabled=clear.IsEnabled=false;try{await action();}catch(Exception e){status.Text=e is AiAnalysisException?e.Message:"AI 设置未能完成。";}finally{save.IsEnabled=models.IsEnabled=clear.IsEnabled=true;}}
}
