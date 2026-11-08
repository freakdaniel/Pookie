using System.Runtime.InteropServices;
using Pookie.Audio;

namespace Pookie.App.Diagnostics;

internal static class NativeWidevineVmpProbe
{
    // Public test PSSH from the Tears of Steel manifest used by the Castlabs VMP Lab.
    // https://storage.googleapis.com/wvmedia/cenc/h264/tears/tears.mpd
    private const string InitData = "AAAARHBzc2gAAAAA7e+LqXnWSs6jyCfc1R0h7QAAACQIARIBNRoNd2lkZXZpbmVfdGVzdCIKMjAxNV90ZWFycyoCU0Q=";

    internal static async Task RunAsync(bool production)
    {
        var backend = production ? "production" : "uat";
        var endpoint = new Uri(production ? "https://proxy.widevine.com/proxy" : "https://proxy.uat.widevine.com/proxy");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        string? status = null;
        Console.WriteLine($"NATIVE_WIDEVINE_VMP_PROBE: backend={backend}, os={RuntimeInformation.OSDescription}, arch={RuntimeInformation.ProcessArchitecture}, interface={CdmAbi.Version}");
        using var session = await WidevineSession.OpenAsync(http, Convert.FromBase64String(InitData),
            () => new HttpRequestMessage(HttpMethod.Post, endpoint), timeout.Token, reportVerification: value =>
            {
                status = value;
                Console.WriteLine($"NATIVE_WIDEVINE_VMP_STATUS: {value}");
            });
        if (status == null) Console.WriteLine("NATIVE_WIDEVINE_VMP_STATUS: not-reported");
        Console.WriteLine("NATIVE_WIDEVINE_VMP_LICENSE_OK: public test license accepted by the native CDM");
    }
}
