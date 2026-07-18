using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Services.Wishes.Importing
{
    public sealed class WishImportFormatException : Exception
    {
        public WishImportFormatException(string message) : base(message)
        {
        }

        public WishImportFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
