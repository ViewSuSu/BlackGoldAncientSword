using System;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public sealed record HeyboxNativeClientProfile
    {
        public string Imei { get; init; } = HeyboxNativeHkey.NewDeviceId();

        public string DeviceInfo { get; init; } = "V1916A";

        public string OsVersion { get; init; } = "9";

        public string Version { get; init; } = "1.3.391";

        public string Build { get; init; } = "1112";

        public string Dw { get; init; } = "360";

        public string Channel { get; init; } = "heybox_xiaomi";

        public string TimeZone { get; init; } = "Asia/Shanghai";

        public string OsType { get; init; } = "Android";

        public string XClientType { get; init; } = "mobile";
    }
}
