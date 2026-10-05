// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Core.Archives
{
    public static class GameUidValidation
    {
        public static bool IsStructurallyValidUid(string? uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                return false;
            }

            string normalizedUid = uid.Trim();
            return normalizedUid.Length is >= 9 and <= 10 &&
                normalizedUid.All(char.IsAsciiDigit);
        }

        public static bool IsValidUid(string? uid)
        {
            if (!IsStructurallyValidUid(uid))
            {
                return false;
            }

            if (!string.Equals(uid, uid!.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            if (GameServerRegionResolver.Resolve(uid!) == GameServerRegion.Unknown)
            {
                return false;
            }
            return true;
        }
    }
}
