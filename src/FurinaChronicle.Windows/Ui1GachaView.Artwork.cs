// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private IReadOnlyList<GachaReadOnlyRow>? artworkRows;
    private GachaArtworkConverter Artwork => (GachaArtworkConverter)Resources["GachaArtwork"];

    private void UpdateArtworkScope(IReadOnlyList<GachaReadOnlyRow> rows)
    {
        if (ReferenceEquals(artworkRows, rows)) return;
        artworkRows = rows;
        Artwork.BeginScope();
    }
}
