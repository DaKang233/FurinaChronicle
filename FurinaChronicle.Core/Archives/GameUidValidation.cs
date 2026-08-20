using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Core.Archives
{
    public static class GameUidValidation
    {
        public static bool IsValidUid(string? uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                return false;
            }
            if (!uid.All(char.IsAsciiDigit))
            {
                return false;
            }
            var uidLength = uid.Length;
            if (uidLength < 9 || uidLength > 10)
            {
                return false;
            }
            if (GameServerRegionResolver.Resolve(uid) == GameServerRegion.Unknown)
            {
                return false;
            }
            return true;
        }
    }
}
