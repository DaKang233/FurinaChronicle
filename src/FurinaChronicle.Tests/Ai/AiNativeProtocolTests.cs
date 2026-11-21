// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Tests.Ai;

public sealed class AiNativeProtocolTests
{
    [Theory]
    [InlineData(AiProtocol.OpenAiCompatible)]
    [InlineData(AiProtocol.OpenAiResponses)]
    [InlineData(AiProtocol.AnthropicMessages)]
    public async Task NativeTextImagesAndHeadersKeepSystemAndAuthenticationInCorrectLocations(AiProtocol protocol)
    {
        string body="";Uri? uri=null;string? bearer=null,apiKey=null,version=null;
        using var http=new HttpClient(new Handler(async (request,_)=>
        {
            body=await request.Content!.ReadAsStringAsync();uri=request.RequestUri;bearer=request.Headers.Authorization?.ToString();
            apiKey=request.Headers.TryGetValues("x-api-key",out var k)?k.Single():null;version=request.Headers.TryGetValues("anthropic-version",out var v)?v.Single():null;
            Assert.False(request.Headers.Contains("Cookie"));return Response(Final(protocol,"你好"));
        }));
        using var client=new HttpAiChatClient(http);var deltas=new List<AiChatDelta>();
        await foreach(var d in client.StreamAsync(new(new(protocol,"https://native.example/v1","test",SupportsImages:true),
            [new("system","fixed system"),new("user","question",[new("image/png","aGVsbG8=","hash")]),new("assistant","prior"),new("user","followup")],"synthetic-key")))deltas.Add(d);
        Assert.Equal("你好",string.Concat(deltas.Select(d=>d.Text)));Assert.True(deltas[^1].Done);Assert.DoesNotContain("synthetic-key",body);
        using var document=JsonDocument.Parse(body);var root=document.RootElement;
        if(protocol==AiProtocol.AnthropicMessages)
        {
            Assert.EndsWith("/v1/messages",uri!.AbsolutePath);Assert.Null(bearer);Assert.Equal("synthetic-key",apiKey);Assert.Equal("2023-06-01",version);
            Assert.Equal("fixed system",root.GetProperty("system")[0].GetProperty("text").GetString());
            var messages=root.GetProperty("messages");Assert.DoesNotContain(messages.EnumerateArray(),m=>m.GetProperty("role").GetString()=="system");
            Assert.Equal("base64",messages[0].GetProperty("content")[1].GetProperty("source").GetProperty("type").GetString());Assert.False(root.TryGetProperty("temperature",out _));
        }
        else
        {
            Assert.Equal("Bearer synthetic-key",bearer);Assert.Null(apiKey);Assert.Null(version);
            if(protocol==AiProtocol.OpenAiResponses)
            {
                Assert.EndsWith("/v1/responses",uri!.AbsolutePath);Assert.False(root.GetProperty("store").GetBoolean());Assert.False(root.TryGetProperty("messages",out _));
                Assert.Equal("input_image",root.GetProperty("input")[1].GetProperty("content")[1].GetProperty("type").GetString());Assert.True(root.TryGetProperty("max_output_tokens",out _));
                Assert.False(root.TryGetProperty("previous_response_id",out _));
            }
            else Assert.EndsWith("/v1/chat/completions",uri!.AbsolutePath);
        }
    }
    [Theory]
    [InlineData(AiProtocol.OpenAiResponses)]
    [InlineData(AiProtocol.AnthropicMessages)]
    public async Task NativeTruncationIsNotACompletedAnswer(AiProtocol p)
    {
        using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(Response(Final(p,"partial",true)))));using var client=new HttpAiChatClient(http);
        var deltas=await Read(client,new(new(p,"https://native.example/v1","test"),[new("user","q")],null));
        Assert.True(deltas[^1].Done);Assert.True(deltas[^1].Truncated);
    }
    [Theory]
    [InlineData(AiProtocol.OpenAiResponses,"data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n")]
    [InlineData(AiProtocol.OpenAiResponses,"data: [DONE]\n\n")]
    [InlineData(AiProtocol.OpenAiResponses,"data: {\"type\":\"response.failed\",\"response\":{\"error\":\"sensitive-provider-body\"}}\n\n")]
    [InlineData(AiProtocol.AnthropicMessages,"data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"partial\"}}\n\n")]
    [InlineData(AiProtocol.AnthropicMessages,"data: {\"type\":\"message_stop\"}\n\n")]
    [InlineData(AiProtocol.AnthropicMessages,"data: {\"type\":\"error\",\"error\":{\"message\":\"sensitive-provider-body\"}}\n\n")]
    public async Task MissingTerminalMalformedOrServerErrorDoesNotBecomeSuccess(AiProtocol p,string stream)
    {
        using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(Response(stream))));using var client=new HttpAiChatClient(http);
        var error=await Assert.ThrowsAsync<AiAnalysisException>(()=>Read(client,new(new(p,"https://native.example/v1","m"),[new("user","q")],null)));
        Assert.DoesNotContain("sensitive-provider-body",error.ToString());
    }
    [Theory]
    [InlineData(AiProtocol.OpenAiCompatible)]
    [InlineData(AiProtocol.OpenAiResponses)]
    [InlineData(AiProtocol.AnthropicMessages)]
    public async Task FunctionCallsNeedExplicitDeclarationAndReassembleArguments(AiProtocol p)
    {
        using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(Response(Tool(p)))));using var client=new HttpAiChatClient(http);
        var deltas=await Read(client,new(new(p,"https://native.example/v1","m"),[new("user","q")],null,AiArchiveTools.Definitions));
        var call=Assert.Single(deltas,d=>d.ToolCall is not null).ToolCall!;
        Assert.Equal("call1",call.Id);Assert.Equal("get_gacha_statistics",call.Name);Assert.Equal("{}",call.Arguments);Assert.True(deltas[^1].Done);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>Read(client,new(new(p,"https://native.example/v1","m"),[new("user","q")],null)));
        await Assert.ThrowsAsync<AiAnalysisException>(()=>Read(client,new(new(p,"https://native.example/v1","m"),[new("user","q")],null,AiArchiveTools.Definitions,true)));
    }
    [Theory]
    [InlineData(AiProtocol.OpenAiCompatible)]
    [InlineData(AiProtocol.OpenAiResponses)]
    [InlineData(AiProtocol.AnthropicMessages)]
    public async Task ToolResultsAndOpaqueContinuationsRoundTripWithoutLeakingIntoVisibleReply(AiProtocol p)
    {
        int round=0;string second="";
        using var http=new HttpClient(new Handler(async (request,_)=>{round++;if(round==2)second=await request.Content!.ReadAsStringAsync();return Response(round==1?Tool(p,true):Final(p,"answer"));}));
        using var client=new HttpAiChatClient(http);var settings=new AiServiceSettings(p,"https://native.example/v1","m");
        var first=await Read(client,new(settings,[new("user","q")],null,AiArchiveTools.Definitions));
        Assert.DoesNotContain("OPAQUE_ONLY",string.Concat(first.Select(d=>d.Text)));
        var call=Assert.Single(first,d=>d.ToolCall is not null).ToolCall!;
        var continuation=first.Where(d=>d.Continuation is not null).Select(d=>d.Continuation!).ToArray();Assert.NotEmpty(continuation);
        await Read(client,new(settings,[new("user","q"),new("assistant","",ToolCalls:[call],Continuation:continuation),new("tool","safe result",ToolCallId:call.Id)],null,AiArchiveTools.Definitions,true));
        using var doc=JsonDocument.Parse(second);var root=doc.RootElement;
        Assert.Contains("OPAQUE_ONLY",second);Assert.Contains("safe result",second);
        if(p==AiProtocol.OpenAiResponses)
        {Assert.False(root.GetProperty("store").GetBoolean());Assert.Equal("reason1",root.GetProperty("input").EnumerateArray().First(i=>i.TryGetProperty("type",out var t)&&t.GetString()=="reasoning").GetProperty("id").GetString());Assert.Contains(root.GetProperty("input").EnumerateArray(),i=>i.TryGetProperty("type",out var t)&&t.GetString()=="function_call_output");Assert.Equal("none",root.GetProperty("tool_choice").GetString());}
        else if(p==AiProtocol.AnthropicMessages)
        {Assert.Equal("thinking",root.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("type").GetString());Assert.Equal("none",root.GetProperty("tool_choice").GetProperty("type").GetString());}
        else Assert.Equal("call1",root.GetProperty("messages")[2].GetProperty("tool_call_id").GetString());
    }
    [Fact]
    public async Task AnthropicPaginationKeepsRecipientAndNativeHeaders()
    {
        var uris=new List<Uri>();using var http=new HttpClient(new Handler((request,_)=>
        {
            uris.Add(request.RequestUri!);Assert.Null(request.Headers.Authorization);Assert.True(request.Headers.Contains("x-api-key"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(uris.Count==1?
                "{\"data\":[{\"id\":\"one\"}],\"has_more\":true,\"last_id\":\"https://foreign.invalid/path\"}":"{\"data\":[{\"id\":\"two\"}],\"has_more\":false}")});
        }));using var client=new HttpAiChatClient(http);var models=await client.ListModelsAsync(new(AiProtocol.AnthropicMessages,"https://native.example/v1"),"synthetic-key");
        Assert.Equal(new[]{"one","two"},models);Assert.All(uris,u=>Assert.Equal("native.example",u.Host));Assert.Contains("after_id=",uris[1].Query);
    }
    [Fact]
    public void ProtocolChoicesAndPersistedValuesDoNotRebindLegacySecrets()
    {
        Assert.Equal(0,(int)AiProtocol.OpenAiCompatible);Assert.Equal(1,(int)AiProtocol.Ollama);Assert.Equal("OpenAiCompatible",AiProtocol.OpenAiCompatible.ToString());
        Assert.Equal(new[]{AiProtocol.OpenAiCompatible,AiProtocol.OpenAiResponses,AiProtocol.AnthropicMessages},AiProtocols.Choices(AiProtocol.OpenAiCompatible).Select(c=>c.Value));
        Assert.Contains(AiProtocols.Choices(AiProtocol.Ollama),c=>c.Value==AiProtocol.Ollama);
    }
    [Fact]
    public void SharedDefaultAuthenticationIsRejected()
    {
        using var http=new HttpClient();http.DefaultRequestHeaders.Authorization=new("Bearer","synthetic-key");Assert.Throws<AiAnalysisException>(()=>new HttpAiChatClient(http));
    }
    internal static string Sse(object value)=>"event: "+(value is string s?s:"message")+"\ndata: "+JsonSerializer.Serialize(value)+"\n\n";
    internal static string Final(AiProtocol p,string text,bool truncated=false)=>p switch
    {
        AiProtocol.OpenAiResponses=>Sse(new{type="response.output_text.delta",delta=text})+Sse(new{type=truncated?"response.incomplete":"response.completed",response=new{status=truncated?"incomplete":"completed",output=Array.Empty<object>()}}),
        AiProtocol.AnthropicMessages=>Sse(new{type="content_block_delta",index=0,delta=new{type="text_delta",text}})+Sse(new{type="message_delta",delta=new{stop_reason=truncated?"max_tokens":"end_turn"}})+Sse(new{type="message_stop"}),
        _=>Sse(new{choices=new[]{new{delta=new{content=text},finish_reason=truncated?"length":"stop"}}})+"data: [DONE]\n\n"
    };
    internal static string Tool(AiProtocol p,bool opaque=false)
    {
        const string name="get_gacha_statistics";string prefix="";
        if(p==AiProtocol.OpenAiResponses)
        {
            if(opaque)prefix=Sse(new{type="response.output_item.done",item=new{type="reasoning",id="reason1",encrypted_content="OPAQUE_ONLY",summary=Array.Empty<object>()}});
            return prefix+Sse(new{type="response.output_item.added",item=new{type="function_call",id="fc1",call_id="call1",name,arguments=""}})+
                Sse(new{type="response.function_call_arguments.delta",item_id="fc1",delta="{"})+Sse(new{type="response.function_call_arguments.delta",item_id="fc1",delta="}"})+
                Sse(new{type="response.completed",response=new{status="completed",output=new[]{new{type="function_call",id="fc1",call_id="call1",name,arguments="{}"}}}});
        }
        if(p==AiProtocol.AnthropicMessages)
        {
            if(opaque)prefix=Sse(new{type="content_block_start",index=0,content_block=new{type="thinking",thinking="",signature=""}})+
                Sse(new{type="content_block_delta",index=0,delta=new{type="thinking_delta",thinking="OPAQUE_ONLY"}})+Sse(new{type="content_block_delta",index=0,delta=new{type="signature_delta",signature="signed-only"}});
            return prefix+Sse(new{type="content_block_start",index=1,content_block=new{type="tool_use",id="call1",name,input=new{}}})+
                Sse(new{type="content_block_delta",index=1,delta=new{type="input_json_delta",partial_json="{"}})+Sse(new{type="content_block_delta",index=1,delta=new{type="input_json_delta",partial_json="}"}})+
                Sse(new{type="message_delta",delta=new{stop_reason="tool_use"}})+Sse(new{type="message_stop"});
        }
        if(opaque)prefix=Sse(new{choices=new[]{new{delta=new{reasoning_content="OPAQUE_ONLY"}}}});
        return prefix+Sse(new{choices=new[]{new{delta=new{tool_calls=new[]{new{index=0,id="call1",type="function",function=new{name,arguments="{"}}}}}}})+
            Sse(new{choices=new[]{new{delta=new{tool_calls=new[]{new{index=0,function=new{arguments="}"}}}},finish_reason="tool_calls"}}})+"data: [DONE]\n\n";
    }
    internal static HttpResponseMessage Response(string stream)=>new(HttpStatusCode.OK){Content=new StreamContent(new TinyStream(Encoding.UTF8.GetBytes(stream)))};
    internal static async Task<List<AiChatDelta>> Read(IAiChatClient client,AiChatRequest request){var result=new List<AiChatDelta>();await foreach(var d in client.StreamAsync(request))result.Add(d);return result;}
    internal sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>action(request,token);}
    private sealed class TinyStream(byte[] bytes):MemoryStream(bytes)
    {public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)=>base.ReadAsync(buffer[..Math.Min(2,buffer.Length)],token);}
}
