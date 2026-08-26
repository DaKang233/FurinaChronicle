using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Wishes;

public sealed class GetWishRecordPage(IWishRecordRepository repository)
{
    public async Task<WishRecordPage> ExecuteAsync(
        Guid gameAccountId,
        int pageNumber,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "游戏账号 ID 不能为空。",
                nameof(gameAccountId));
        }

        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber),
                "页码必须大于零。");
        }

        if (pageSize is <= 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                "每页数量必须处于 1 到 200 之间。");
        }

        int totalCount = await repository.CountAsync(
            gameAccountId,
            cancellationToken);
        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalCount / pageSize));
        int normalizedPage = Math.Min(pageNumber, totalPages);
        int offset = checked((normalizedPage - 1) * pageSize);

        IReadOnlyList<WishRecord> records =
            await repository.GetPageAsync(
                gameAccountId,
                offset,
                pageSize,
                cancellationToken);

        return new WishRecordPage(
            records,
            totalCount,
            normalizedPage,
            pageSize);
    }
}
