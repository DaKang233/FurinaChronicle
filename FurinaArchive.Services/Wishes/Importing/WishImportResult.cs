using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Services.Wishes.Importing
{
    public sealed record WishImportResult(int TotalCount, int ImportedCount, int DuplicateCount, int InvalidCount);
}
