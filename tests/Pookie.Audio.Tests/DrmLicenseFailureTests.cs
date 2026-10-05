using System.Net;
using System.Text;
using Xunit;

namespace Pookie.Audio.Tests;

public sealed class DrmLicenseFailureTests
{
    [Theory]
    [InlineData("Forbidden", "unspecified")]
    [InlineData("VMP", "unspecified")]
    [InlineData("VMP validation failed", "host-verification")]
    [InlineData("HOST_VERIFICATION_FAILED", "host-verification")]
    [InlineData("token_expired", "expired-authorization")]
    [InlineData("invalid token", "invalid-authorization")]
    [InlineData("captcha required", "browser-verification")]
    public void GenericRejectionDoesNotInventAHostVerificationFailure(string body, string category) =>
        Assert.Equal(category, DrmLicenseFailure.Classify(Encoding.UTF8.GetBytes(body)));

    [Theory]
    [InlineData("{\"code\":403}", "403")]
    [InlineData("{\"error_code\":1001}", "1001")]
    [InlineData("{\"errorCode\":-2}", "-2")]
    [InlineData("{\"status\":403}", "403")]
    [InlineData("{\"code\":\"secret-token\"}", "none")]
    [InlineData("{\"code\":2147483648}", "none")]
    [InlineData("{\"code\":1.25}", "none")]
    [InlineData("{\"error\":{\"code\":403}}", "none")]
    [InlineData("[403]", "none")]
    [InlineData("Forbidden", "none")]
    public void DiagnosticCodeAllowsOnlyTopLevelIntegers(string body, string code) =>
        Assert.Equal(code, DrmLicenseFailure.NumericCode(Encoding.UTF8.GetBytes(body)));

    [Fact]
    public async Task ErrorDetailsNeverReturnCredentialsOrTheResponseBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"message\":\"token expired\",\"token\":\"secret-token\",\"url\":\"https://example.test/?token=secret-token\"}")
        };
        var detail = await DrmLicenseFailure.ReadAsync(response, CancellationToken.None);
        Assert.Contains("истечении авторизации", detail);
        Assert.DoesNotContain("secret-token", detail);
        Assert.DoesNotContain("https://", detail);
    }

    [Fact]
    public async Task InspectionStopsAt16KiBWithoutReadingTheRemainingBody()
    {
        var prefix = new string('x', 16 * 1024);
        using var stream = new ObservedStream(Encoding.UTF8.GetBytes(prefix + "host verification failed"));
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StreamContent(stream) };
        Assert.Equal("", await DrmLicenseFailure.ReadAsync(response, CancellationToken.None));
        Assert.Equal(16 * 1024, stream.BytesRead);
    }

    [Fact]
    public async Task InspectionHonorsCancellation()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Forbidden") };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DrmLicenseFailure.ReadAsync(response, cancellation.Token));
    }

    private sealed class ObservedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }
}
