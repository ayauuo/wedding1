using System;
using System.Collections.Generic;
using System.Text;

namespace PhotoBoothWin.Models
{
    public class BoothSession
    {
        public string TemplateId { get; set; } = "";
        public string[] ShotPaths { get; } = new string[4];

        // 每張最多一次重拍
        public bool[] Retaken { get; } = new bool[4];

        // 目前選中的照片（0~3）
        public int ActiveIndex { get; set; } = 0;

        // 每張照片各自選的濾鏡，空字串 = 無濾鏡
        public string[] PhotoFilters { get; } = new string[4];
    }
}

