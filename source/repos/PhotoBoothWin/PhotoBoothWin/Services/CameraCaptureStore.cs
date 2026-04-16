using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBoothWin.Services
{
    public static class CameraCaptureStore
    {
        private static readonly object _lock = new();
        private static readonly List<string> _paths = new();

        public static void SetCapture(int index, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (_lock)
            {
                while (_paths.Count <= index) _paths.Add(string.Empty);
                _paths[index] = path;
            }
        }

        /// <summary>回傳僅有路徑的列表（不含空位），供舊版呼叫相容。</summary>
        public static IReadOnlyList<string> GetCaptures()
        {
            lock (_lock)
            {
                return _paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            }
        }

        /// <summary>依 index 回傳所有格位，空位為空字串；供「補拍回填」與合成對齊格號。</summary>
        public static IReadOnlyList<string> GetCapturesByIndex()
        {
            lock (_lock)
            {
                return _paths.Select(p => p ?? "").ToList();
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _paths.Clear();
            }
        }
    }
}
