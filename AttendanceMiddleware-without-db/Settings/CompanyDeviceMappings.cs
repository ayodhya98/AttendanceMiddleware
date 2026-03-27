namespace AttendanceMiddleware_without_db.Settings
{

    public class CompanyDeviceMappings
    {
        public static readonly List<CompanyDeviceMapping> All = new()
    {
        new CompanyDeviceMapping
        {
            DeviceId    = "VGU6253800053",
            CompanyName = "OceanPick",
            HrmBaseUrl  = "https://oceanpickapi.antlerfoundry.app/api/"
        },
        // Add next company like this:
        // new CompanyDeviceMapping
        // {
        //     DeviceId    = "ABC1234567890",
        //     CompanyName = "CompanyB",
        //     HrmBaseUrl  = "https://hrm.companyb.com/api/"
        // },
    };

        // Quick lookup by DeviceID — used by publisher to route message
        public static CompanyDeviceMapping GetByDeviceId(string deviceId) => All.FirstOrDefault(m =>m.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
    }
}
