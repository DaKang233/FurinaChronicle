// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Tests.Ai;
public sealed class AiAttachmentProtocolTests
{
    private const string Png="iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAFXRFWHRDb21tZW50AEdQUy0xMDAwMDAwMDH2CXbNAAAADUlEQVR4nGP4zwAE/wEHAAH/4iOeWQAAAABJRU5ErkJggg==";
    [Fact]
    public async Task PngTextMetadataIsRemovedWithoutLosingPixelChunks()
    {
        var file=await new AiAttachmentReader().ReadAsync("synthetic.png",new MemoryStream(Convert.FromBase64String(Png)));
        var cleaned=Convert.FromBase64String(file.ImageBase64!);Assert.DoesNotContain("GPS-100000001",Encoding.ASCII.GetString(cleaned));Assert.Contains("IDAT",Encoding.ASCII.GetString(cleaned));Assert.False(file.PrivacyReviewed);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>new AiAttachmentReader().ReadAsync("bad.png",new MemoryStream([1,2,3])));
        await Assert.ThrowsAsync<AiAnalysisException>(()=>new AiAttachmentReader().ReadAsync("large.txt",new MemoryStream(new byte[AiAttachmentReader.MaximumBytes+1])));
    }
    [Theory]
    [InlineData(AiProtocol.OpenAiCompatible)]
    [InlineData(AiProtocol.Ollama)]
    public async Task ImageProtocolsUseEmbeddedBytesWithoutFileNamesOrPaths(AiProtocol protocol)
    {
        string? sent=null;using var http=new HttpClient(new Handler(async request=>
        {sent=await request.Content!.ReadAsStringAsync();return new(HttpStatusCode.OK){Content=new StringContent(protocol==AiProtocol.Ollama?"{\"message\":{\"content\":\"完成\"},\"done\":true}\n":"data: {\"choices\":[{\"delta\":{\"content\":\"完成\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")};}));
        using var client=new HttpAiChatClient(http);var settings=new AiServiceSettings(protocol,"https://example.invalid/v1","vision",SupportsImages:true);
        await foreach(var delta in client.StreamAsync(new(settings,[new("user","分析图片",[new("image/png",Png,"hash")])],null))){}
        using var json=JsonDocument.Parse(sent!);var message=json.RootElement.GetProperty("messages")[0];
        if(protocol==AiProtocol.Ollama)Assert.Equal(Png,message.GetProperty("images")[0].GetString());
        else Assert.StartsWith("data:image/png;base64,",message.GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.DoesNotContain("synthetic.png",sent);
    }
    [Theory]
    [InlineData("{\"CookieToken\":\"synthetic-secret\"}")]
    [InlineData("{\"stoken_v2\":\"v2_synthetic-secret\"}")]
    [InlineData("{\"password\":\"synthetic-secret\"}")]
    [InlineData(".example.invalid\tTRUE\t/\tFALSE\t0\tstoken\tv2_synthetic-secret")]
    [InlineData("login_ticket=synthetic-secret")]
    public async Task CredentialFilesAreRejectedAcrossJsonAndNetscapeCookieFormats(string body)
    {
        var exception=await Assert.ThrowsAsync<AiAnalysisException>(()=>new AiAttachmentReader().ReadAsync("sensitive.txt",new MemoryStream(Encoding.UTF8.GetBytes(body))));
        Assert.DoesNotContain("synthetic-secret",exception.Message);
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> callback):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>callback(request);}
}
