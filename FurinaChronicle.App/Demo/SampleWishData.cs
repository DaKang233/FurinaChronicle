using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.App.Demo
{
    internal static class SampleWishData
    {
        public const string Uid = "800000001";
        public const int SourceRecordCount = 5;

        public static Stream OpenStream()
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(Json));
        }
        private const string Json =
                    """
        {
          "uid": "800000001",
          "list": [
            {
              "id": "200000000000000001",
              "name": "芙宁娜",
              "rank_type": "5",
              "time": "2026-07-16T18:30:00+08:00"
            },
            {
              "id": "200000000000000002",
              "name": "夏洛蒂",
              "rank_type": "4",
              "time": "2026-07-16T18:29:00+08:00"
            },
            {
              "id": "200000000000000003",
              "name": "黎明神剑",
              "rank_type": "3",
              "time": "2026-07-16T18:28:00+08:00"
            },
            {
              "id": "200000000000000002",
              "name": "夏洛蒂",
              "rank_type": "4",
              "time": "2026-07-16T18:29:00+08:00"
            },
            {
              "id": "",
              "name": "无效记录",
              "rank_type": "4",
              "time": "2026-07-16T18:27:00+08:00"
            }
          ]
        }
        """;
    }
}
