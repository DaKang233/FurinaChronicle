// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Services.Ai;
namespace FurinaChronicle.App.Ai;
public sealed class AiSettingsView : ContentView
{
    private readonly AiSettingsState state;
    private bool filling;
    private readonly Picker providers=new(){Title="供应商",ItemDisplayBinding=new Binding("Label"),AutomationId="AiProviderProfiles"},profiles=new(){Title="已保存模型",ItemDisplayBinding=new Binding("Label"),AutomationId="AiModelProfiles"};
    private readonly Entry name=new(){Placeholder="供应商名称",AutomationId="AiProviderName"},alias=new(){Placeholder="模型显示名称（可选）",AutomationId="AiModelName"};
    private readonly Picker protocol=new(){Title="接口类型",AutomationId="AiProtocol",ItemDisplayBinding=new Binding("Label")};
    private readonly Entry userName=new(){Placeholder="用户称呼（仅标记本地消息）",AutomationId="AiUserDisplayName"};
    private readonly Editor systemPrompt=new(){Placeholder="系统提示词",HeightRequest=140,AutomationId="AiSystemPrompt"};
    private readonly CheckBox allowTools=new();
    private readonly Entry maxTools=new(){Keyboard=Keyboard.Numeric,AutomationId="AiMaxToolCalls"};
    private readonly Entry address=new(){Placeholder="API 基址",AutomationId="AiBaseAddress"},model=new(){Placeholder="模型标识",AutomationId="AiModel"},key=new(){Placeholder="此供应商密钥（留空保留）",IsPassword=true,IsTextPredictionEnabled=false,AutomationId="AiApiKey"};
    private readonly Entry budget=new(){Keyboard=Keyboard.Numeric},output=new(){Keyboard=Keyboard.Numeric},context=new(){Keyboard=Keyboard.Numeric};
    private readonly CheckBox images=new(),thinking=new(),cpu=new();
    private View? thinkingRow,cpuRow;private readonly Label ollamaLabel=new(){Text="Ollama 上下文 token"};
    private readonly Label status=new();
    public AiSettingsView(AiSettingsState state)
    {
        this.state=state;var body=new VerticalStackLayout{Spacing=8};Content=body;
        body.Add(new Label{Text="全局聊天配置",FontSize=20,FontAttributes=FontAttributes.Bold});
        body.Add(userName);body.Add(systemPrompt);allowTools.AutomationId="AiAllowLocalTools";body.Add(Check(allowTools,"允许查询当前附加范围的本地档案（只读）"));
        body.Add(new Label{Text="每个用户消息的最大工具调用次数（0～500）；0 关闭。查询仅限已附加证据/统计快照，结果发给本轮服务。"});body.Add(maxTools);
        body.Add(Command("保存全局配置",async()=>{await state.SavePreferencesAsync(new(userName.Text?.Trim()??"",systemPrompt.Text??"",allowTools.IsChecked,Number(maxTools.Text)));FillPreferences();status.Text="全局聊天配置已保存。";},"AiSaveChatPreferences"));
        body.Add(new Label{Text="AI 供应商与模型",FontSize=20,FontAttributes=FontAttributes.Bold});
        body.Add(new Label{Text="先保存当前编辑内容，再切换编辑项。保存后设为新对话默认模型；已有对话保留各自选择。更换地址或协议不携带旧密钥。"});
        body.Add(providers);body.Add(Row(Command("添加供应商",()=>{state.NewProvider();Fill();return Task.CompletedTask;},"AiAddProvider"),Command("删除供应商",DeleteProviderAsync,"AiDeleteProvider")));
        foreach(var v in new View[]{name,protocol,address,key,profiles})body.Add(v);
        body.Add(Row(Command("添加模型",()=>{state.NewModel();Fill();return Task.CompletedTask;},"AiAddModel"),Command("删除模型",DeleteModelAsync,"AiDeleteModel")));
        foreach(var v in new View[]{alias,model,new Label{Text="上下文字符额度 / 输出 token 额度"},budget,output,Check(images,"此模型支持图片输入"),thinkingRow=Check(thinking,"关闭兼容服务思考（仅支持 thinking 参数的服务）"),ollamaLabel,context,cpuRow=Check(cpu,"Ollama 仅 CPU")})body.Add(v);
        body.Add(Command("保存 AI 设置",async()=>{await state.SaveProfileAsync(name.Text??"",alias.Text??"",Inputs(),string.IsNullOrWhiteSpace(key.Text)?null:key.Text);Fill();status.Text="已保存并设为新对话默认模型。";},"AiSaveSettings"));
        body.Add(Command("获取模型列表",DiscoverAsync,"AiDiscoverModels"));body.Add(Command("移除此供应商密钥",async()=>{await state.ClearKeyAsync(Inputs());key.Text="";status.Text="已移除此供应商当前地址的密钥。";},"AiClearKey"));body.Add(status);
        protocol.SelectedIndexChanged+=(_,_)=>UpdateProtocolFields();
        providers.SelectedIndexChanged+=(_,_)=>{if(!filling&&providers.SelectedItem is AiProviderProfile p){state.EditProvider(p.Id);Fill();}};
        profiles.SelectedIndexChanged+=(_,_)=>{if(!filling&&profiles.SelectedItem is AiModelProfile m){state.EditModel(m.Id);Fill();}};
    }
    private Page Host
    {
        get
        {
            for(Element? element=this; element is not null; element=element.Parent)
                if(element is Page page) return page;
            throw new AiAnalysisException("页面尚未就绪。");
        }
    }
    private static View Row(params View[] views){var g=new Grid{ColumnSpacing=6,ColumnDefinitions={new(GridLength.Star),new(GridLength.Star)}};for(int i=0;i<views.Length;i++){Grid.SetColumn(views[i],i);g.Add(views[i]);}return g;}
    private Button Command(string label,Func<Task> action,string id){var b=new Button{Text=label,AutomationId=id};b.Clicked+=async(_,_)=>await Run(action);return b;}
    public async Task LoadAsync()=>await Run(async()=>{await state.LoadAsync();Fill();FillPreferences();});
    private void Fill()
    {
        filling=true;
        try
        {
            var p=state.EditingProvider;var s=state.Settings;
            providers.ItemsSource=state.Catalog.Providers.ToArray();providers.SelectedItem=state.Catalog.Find(p?.Id);
            profiles.ItemsSource=p?.Models.ToArray();profiles.SelectedItem=p?.Models.FirstOrDefault(m=>m.Id==state.EditingModelId);
            name.Text=p?.Name??"";alias.Text=p?.Models.FirstOrDefault(m=>m.Id==state.EditingModelId)?.Name??"";
            var choices=AiProtocols.Choices(s.Protocol).ToArray();protocol.ItemsSource=choices;protocol.SelectedItem=choices.First(x=>x.Value==s.Protocol);address.Text=s.BaseAddress;model.Text=s.Model;budget.Text=s.ContextCharacters.ToString();output.Text=s.MaxOutputTokens.ToString();context.Text=s.OllamaContextTokens.ToString();cpu.IsChecked=s.CpuOnly;thinking.IsChecked=s.DisableCompatibleThinking;images.IsChecked=s.SupportsImages;key.Text="";
        }
        finally{filling=false;}
    }
    private void FillPreferences(){var p=state.Catalog.Preferences;userName.Text=p.UserDisplayName;systemPrompt.Text=p.SystemPrompt;allowTools.IsChecked=p.AllowLocalTools;maxTools.Text=p.MaxToolCalls.ToString();}
    private void UpdateProtocolFields(){var p=(protocol.SelectedItem as AiProtocolChoice)?.Value;if(thinkingRow is not null)thinkingRow.IsVisible=p==AiProtocol.OpenAiCompatible;ollamaLabel.IsVisible=context.IsVisible=p==AiProtocol.Ollama;if(cpuRow is not null)cpuRow.IsVisible=p==AiProtocol.Ollama;}
    private AiServiceSettings Inputs()=>state.Settings with {Protocol=(protocol.SelectedItem as AiProtocolChoice)?.Value??AiProtocol.OpenAiCompatible,BaseAddress=address.Text?.Trim()??"",Model=model.Text?.Trim()??"",ContextCharacters=Number(budget.Text),MaxOutputTokens=Number(output.Text),OllamaContextTokens=Number(context.Text),CpuOnly=cpu.IsChecked,DisableCompatibleThinking=thinking.IsChecked,SupportsImages=images.IsChecked};
    private async Task DiscoverAsync()
    {
        var choices=await state.ModelsAsync(Inputs(),string.IsNullOrWhiteSpace(key.Text)?null:key.Text);
        string? selected=await Host.DisplayActionSheetAsync("模型（不发送聊天）","取消",null,choices.ToArray());
        if(choices.Contains(selected)){model.Text=selected;if(string.IsNullOrWhiteSpace(alias.Text))alias.Text=selected;status.Text="已填入模型，请保存。图片能力需自行确认。";}
    }
    private async Task DeleteProviderAsync(){if(!await Host.DisplayAlertAsync("删除供应商？","删除配置及当前地址密钥，聊天保留；相关会话发送前须重新选择。","删除","取消"))return;await state.DeleteProviderAsync();Fill();status.Text="已删除供应商，聊天保留。";}
    private async Task DeleteModelAsync(){if(!await Host.DisplayAlertAsync("删除模型配置？","历史回答保留，相关会话发送前须重新选择。","删除","取消"))return;await state.DeleteModelAsync();Fill();}
    private static int Number(string? text)=>int.TryParse(text,out int n)?n:throw new AiAnalysisException("请输入有效整数。");
    private static View Check(CheckBox box,string text){var g=new Grid{ColumnDefinitions={new(GridLength.Auto),new(GridLength.Star)}};g.Add(box);var l=new Label{Text=text,VerticalOptions=LayoutOptions.Center};Grid.SetColumn(l,1);g.Add(l);return g;}
    private async Task Run(Func<Task> action){IsEnabled=false;try{await action();}catch(Exception e){status.Text=e is AiAnalysisException?e.Message:"AI 设置未能完成。";}finally{IsEnabled=true;}}
}
