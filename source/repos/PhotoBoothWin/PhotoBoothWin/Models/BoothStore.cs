using System;
using System.Collections.Generic;
using System.Text;

using PhotoBoothWin.Models;

namespace PhotoBoothWin
{
    public static class BoothStore
    {
        public static BoothSession Current { get; } = new BoothSession();

        public static void Reset()
        {
            Current.TemplateId = "";
            Current.ActiveIndex = 0;
            for (int i = 0; i < 4; i++)
            {
                Current.ShotPaths[i] = "";
                Current.Retaken[i] = false;
                Current.PhotoFilters[i] = "";
            }
        }

    }
}

