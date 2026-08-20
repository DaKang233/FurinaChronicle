using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Archives
{
    public sealed record ArchiveSelection(Guid PlayerArchiveId, Guid GameAccountId);
}
