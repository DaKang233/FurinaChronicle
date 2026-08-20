using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    public sealed class SqliteDatabaseOptions
    {
        public SqliteDatabaseOptions(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("数据库路径不能为空。", nameof(databasePath));
            }
            DatabasePath = Path.GetFullPath(databasePath);
        }

        public string DatabasePath { get; }
    }
}
