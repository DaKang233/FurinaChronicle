using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Core.Archives
{
    public sealed record PlayerArchive(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
