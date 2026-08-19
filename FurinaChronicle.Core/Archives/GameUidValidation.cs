using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Core.Archives
{
    public static class GameUidValidation
    {
        public static bool IsValidUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                return false;
            }
            string normalizedUid = uid.Trim();
            if (!normalizedUid.All(char.IsAsciiDigit))
            {
                return false;
            }
            var uidLength = normalizedUid.Length;
            if (uidLength < 9 || uidLength > 10)
            {
                return false;
            }
            return true;
        }
    }
}
