using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Core.Archives
{
    public sealed record GameAccount(
        Guid Id, 
        Guid PlayerArchiveId, string Uid, 
        GameServerRegion ServerRegion, 
        string? DisplayName, 
        bool IsPlaceholder, 
        DateTimeOffset CreatedAt, 
        DateTimeOffset UpdatedAt);
}
