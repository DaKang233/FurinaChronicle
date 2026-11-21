// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

public sealed class AiAttachmentReader
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public async Task<AiAttachment> ReadAsync(string name,Stream input,CancellationToken token=default,PreparedAiContext? context=null)
    {
        name=AiPrivacy.Sanitize(Path.GetFileName(name),maxLength:240);
        string extension=Path.GetExtension(name).ToLowerInvariant();
        bool image=extension is ".png" or ".jpg" or ".jpeg" or ".webp";
        if(!image && extension is not (".txt" or ".log" or ".json" or ".csv" or ".md" or ".markdown" or ".xml" or ".yaml" or ".yml"))
            throw new AiAnalysisException("首版附件支持文本、JSON、CSV、Markdown、XML/YAML 及 PNG/JPEG/WebP 图片。");
        using var memory=new MemoryStream(); byte[] buffer=new byte[16384]; int read;
        while((read=await input.ReadAsync(buffer,token))>0)
        { if(memory.Length+read>MaximumBytes) throw new AiAnalysisException("单个附件不得超过 2 MiB。"); await memory.WriteAsync(buffer.AsMemory(0,read),token); }
        byte[] data=memory.ToArray();
        if(image)
        {
            string mime;
            if(extension==".png") {data=StripPng(data);mime="image/png";}
            else if(extension==".webp") {data=StripWebp(data);mime="image/webp";}
            else {data=StripJpeg(data);mime="image/jpeg";}
            return new(Guid.NewGuid(),name,mime,null,Convert.ToBase64String(data),data.Length,Convert.ToHexString(SHA256.HashData(data)));
        }
        string text;
        try
        {
            if(data.AsSpan().StartsWith(new byte[]{255,254})) text=new UnicodeEncoding(false,true,true).GetString(data,2,data.Length-2);
            else if(data.AsSpan().StartsWith(new byte[]{254,255})) text=new UnicodeEncoding(true,true,true).GetString(data,2,data.Length-2);
            else text=new UTF8Encoding(false,true).GetString(data).TrimStart('\uFEFF');
        }
        catch(DecoderFallbackException) {throw new AiAnalysisException("文本附件须为 UTF-8 或带 BOM 的 UTF-16 编码。");}
        if(text.Contains('\0')) throw new AiAnalysisException("附件包含二进制数据，不能作为文本读取。");
        text=context?.SanitizeText(text,1_000_000)??AiPrivacy.Sanitize(text,maxLength:1_000_000);
        return new(Guid.NewGuid(),name,"text/plain",text,null,Encoding.UTF8.GetByteCount(text),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }
    private static AiAnalysisException Invalid() => new("图片格式或尺寸无效；请使用静态 PNG/JPEG/WebP 图片。");
    private static void Dimensions(uint width,uint height)
    {if(width==0||height==0||width>16384||height>16384||(ulong)width*height>25_000_000)throw Invalid();}
    private static byte[] StripPng(byte[] data)
    {
        if(data.Length<45 || !data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw Invalid();
        using var result=new MemoryStream();result.Write(data,0,8);int pos=8;bool header=false, pixels=false, end=false;
        while(pos+12<=data.Length)
        {
            uint size=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos,4));if(size>MaximumBytes||pos+12L+size>data.Length)throw Invalid();
            string tag=Encoding.ASCII.GetString(data,pos+4,4);int length=(int)size+12;
            if(tag=="IHDR") {if(header||size!=13)throw Invalid();Dimensions(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos+8,4)),BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos+12,4)));header=true;}
            if(tag=="acTL")throw Invalid();
            if(tag=="IDAT")pixels=true;
            if(tag is "IHDR" or "PLTE" or "IDAT" or "IEND" or "tRNS" or "sRGB" or "gAMA" or "cHRM")result.Write(data,pos,length);
            pos+=length;if(tag=="IEND"){end=true;break;}
        }
        if(!header||!pixels||!end)throw Invalid();return result.ToArray();
    }
    private static byte[] StripJpeg(byte[] data)
    {
        if(data.Length<8||data[0]!=255||data[1]!=216)throw Invalid();
        using var result=new MemoryStream();result.Write(data,0,2);int pos=2;bool dimensions=false;
        while(pos+4<=data.Length)
        {
            int start=pos;if(data[pos++]!=255)throw Invalid();while(pos<data.Length&&data[pos]==255)pos++;if(pos>=data.Length)throw Invalid();byte marker=data[pos++];
            int size=BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos,2));if(size<2||pos+size>data.Length)throw Invalid();
            if(marker is 192 or 193 or 194){if(size<8)throw Invalid();Dimensions(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos+5,2)),BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos+3,2)));dimensions=true;}
            if(marker==218)
            {
                if(!dimensions)throw Invalid();int end=data.Length-2;while(end>pos && !(data[end]==255&&data[end+1]==217))end--;
                if(end<=pos)throw Invalid();result.Write(data,start,end+2-start);return result.ToArray();
            }
            // APP14 Adobe color transform is not EXIF/XMP; keep it for CMYK decoding.
            if((marker<224||marker>239||marker==238&&size==14&&Encoding.ASCII.GetString(data,pos+2,5)=="Adobe")&&marker!=254) result.Write(data,start,pos+size-start);
            pos+=size;
        }
        throw Invalid();
    }
    private static byte[] StripWebp(byte[] data)
    {
        if(data.Length<20||Encoding.ASCII.GetString(data,0,4)!="RIFF"||Encoding.ASCII.GetString(data,8,4)!="WEBP")throw Invalid();
        using var result=new MemoryStream();result.Write(data,0,12);int pos=12;bool pixels=false;
        while(pos+8<=data.Length)
        {
            string tag=Encoding.ASCII.GetString(data,pos,4);uint size=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos+4,4));long total=8L+size+(size%2);
            if(size>MaximumBytes||pos+total>data.Length||tag is "ANIM" or "ANMF")throw Invalid();
            if(tag=="VP8X") {if(size!=10)throw Invalid();Dimensions(1u+data[pos+12]+((uint)data[pos+13]<<8)+((uint)data[pos+14]<<16),1u+data[pos+15]+((uint)data[pos+16]<<8)+((uint)data[pos+17]<<16));if((data[pos+8]&2)!=0)throw Invalid();data[pos+8]&=0x10;}
            if(tag is "VP8 " or "VP8L")pixels=true;
            if(tag is "VP8X" or "VP8 " or "VP8L" or "ALPH")result.Write(data,pos,(int)total);
            pos+=(int)total;
        }
        if(!pixels)throw Invalid();byte[] cleaned=result.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(cleaned.AsSpan(4,4),(uint)cleaned.Length-8);return cleaned;
    }
}
