using FurinaArchive.Core.Wishes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Services.Wishes.Importing
{
    public sealed record WishReadResult(IReadOnlyList<WishRecord> Records, IReadOnlyList<WishImportError> Errors);
}
