using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Wishes.Importing
{
    public sealed record WishImportError(int RecordIndex, string Code, string Message);
}
