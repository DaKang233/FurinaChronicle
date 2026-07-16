using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Core.Wishes
{
    public sealed record WishRecord(
        Guid GameAccountId,
        string ExternalRecordId,
        string ItemName,
        int RankType,
        DateTimeOffset Time);
}
