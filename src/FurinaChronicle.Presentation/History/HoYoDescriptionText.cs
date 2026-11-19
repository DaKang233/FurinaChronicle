// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Presentation.History;

/// <summary>Text-only projection of the same safe syntax used by rich hosts; saved evidence is untouched.</summary>
public static class HoYoDescriptionText
{
    public static string Plain(string value) => string.Concat(HoYoRichText.Parse(value).Select(run => run.Text));
}
