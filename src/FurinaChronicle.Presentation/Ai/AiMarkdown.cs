// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.RegularExpressions;
namespace FurinaChronicle.Presentation.Ai;
public sealed record AiMarkdownBlock(string Text,bool Code=false,int Heading=0);
public static class AiMarkdown
{
    public static IReadOnlyList<AiMarkdownBlock> Parse(string text)
    {
        var result=new List<AiMarkdownBlock>();var code=new StringBuilder();bool fenced=false;
        foreach(string line in text.Replace("\r","").Split('\n'))
        {
            if(line.TrimStart().StartsWith("```",StringComparison.Ordinal)){if(fenced){result.Add(new(code.ToString(),true));code.Clear();}fenced=!fenced;continue;}
            if(fenced){code.AppendLine(line);continue;}
            if(line.Length==0)continue;
            int heading=line.TakeWhile(c=>c=='#').Count();
            result.Add(new(heading is >0 and <=6?line[heading..].TrimStart():line.StartsWith("- ",StringComparison.Ordinal)?"• "+line[2..]:line,false,heading));
        }
        if(fenced)result.Add(new(code.ToString(),true));return result;
    }
    public static IEnumerable<(string Text,bool Bold,bool Code)> Inline(string text)
    {
        foreach(string part in Regex.Split(text,@"(\*\*[^*]+\*\*|`[^`]+`)",RegexOptions.NonBacktracking,TimeSpan.FromMilliseconds(200)))
            yield return part.StartsWith("**",StringComparison.Ordinal)&&part.EndsWith("**",StringComparison.Ordinal)&&part.Length>4?(part[2..^2],true,false):
                part.StartsWith('`')&&part.EndsWith('`')&&part.Length>2?(part[1..^1],false,true):(part,false,false);
    }
}
