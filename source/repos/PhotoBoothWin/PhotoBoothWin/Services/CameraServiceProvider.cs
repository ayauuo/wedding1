using System;

namespace PhotoBoothWin.Services
{
    public static class CameraServiceProvider
    {
        private static readonly Lazy<ICameraService> _current =
            new Lazy<ICameraService>(() => new CanonEdsdkCameraService());

        public static ICameraService Current => _current.Value;
    }
}
