using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Wishes
{
    public sealed record WishSaveResult(int InsertedCount, int DuplicateCount);
}
