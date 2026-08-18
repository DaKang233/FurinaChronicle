using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks.Sources;

namespace FurinaChronicle.Core.Archives
{
    public static class GameServerRegionResolver
    {
        public static GameServerRegion Resolve(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                return GameServerRegion.Unknown;
            }

            string normalizedUid = uid.Trim();

            if (!normalizedUid.All(char.IsAsciiDigit))
            {
                return GameServerRegion.Unknown;
            }

            return ResolveNormalizedLocally(normalizedUid);
        }

        private static GameServerRegion ResolveNormalizedLocally(string uid)
        {
            var CelestiaMark = uid.StartsWith("1") || uid.StartsWith("2") || uid.StartsWith("3");
            var IrminsulMark = uid.StartsWith("5");
            var AmericaMark = uid.StartsWith("6");
            var EuropeMark = uid.StartsWith("7");
            var AsiaMark = uid.StartsWith("8") || uid.StartsWith("18");
            var SARMark = uid.StartsWith("9");

            if (CelestiaMark) return GameServerRegion.ChinaOfficial;
            if (IrminsulMark) return GameServerRegion.ChinaBilibili;
            if (AmericaMark) return GameServerRegion.America;
            if (EuropeMark) return GameServerRegion.Europe;
            if (SARMark) return GameServerRegion.TaiwanHongKongMacao;

            return GameServerRegion.Unknown;
        }
    }
}
