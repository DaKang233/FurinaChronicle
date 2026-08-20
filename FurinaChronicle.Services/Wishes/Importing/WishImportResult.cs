using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Wishes.Importing
{
    public sealed record WishImportResult(int TotalCount, int ImportedCount, int DuplicateCount, int InvalidCount);
}
