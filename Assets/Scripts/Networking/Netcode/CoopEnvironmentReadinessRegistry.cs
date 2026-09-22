using System;

namespace FPS.Networking.Netcode
{
    /// <summary>
    /// Keeps the headless networking assembly independent from CityNew's
    /// concrete NavMesh implementation while still enforcing that authority
    /// never starts before the server-side world is ready.
    /// </summary>
    public static class CoopEnvironmentReadinessRegistry
    {
        private static Action ensureCityNew;
        private static Func<bool> isCityNewReady;
        private static Action isolateCityNewLegacyContent;

        public static bool HasCityNewEnvironment =>
            ensureCityNew != null && isCityNewReady != null;

        public static bool IsCityNewReady =>
            isCityNewReady != null && isCityNewReady();

        public static void RegisterCityNewEnvironment(
            Action ensure,
            Func<bool> isReady)
        {
            ensureCityNew = ensure ?? throw new ArgumentNullException(
                nameof(ensure));
            isCityNewReady = isReady ?? throw new ArgumentNullException(
                nameof(isReady));
        }

        public static void RegisterCityNewContentIsolation(Action isolate)
        {
            isolateCityNewLegacyContent = isolate ??
                throw new ArgumentNullException(nameof(isolate));
        }

        public static void EnsureCityNewEnvironment()
        {
            if (ensureCityNew == null)
                throw new InvalidOperationException(
                    "CityNew 服务器环境适配器尚未注册。");
            ensureCityNew();
        }

        public static void IsolateCityNewLegacyContent()
        {
            isolateCityNewLegacyContent?.Invoke();
        }
    }
}
