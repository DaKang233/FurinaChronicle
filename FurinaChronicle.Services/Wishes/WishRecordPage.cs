using FurinaChronicle.Core.Wishes;

namespace FurinaChronicle.Services.Wishes;

public sealed record WishRecordPage(
    IReadOnlyList<WishRecord> Records,
    int TotalCount,
    int PageNumber,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
}
