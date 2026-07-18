using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Services.Wishes.Importing
{
    public sealed record WishImportError(int RecordIndex, string Code, string Message);
}
