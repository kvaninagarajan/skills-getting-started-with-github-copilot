using System.Net.Http.Json;
using System.Text.Json;
using AddressMapper.Model;

namespace AddressMapper.Api;

public sealed class Geocoder : IDisposable
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset nextRequest = DateTimeOffset.MinValue;
    private readonly bool configured;

    public Geocoder(IConfiguration configuration)
    {
        var contact = configuration["Geocoding:ContactEmail"];
        configured = System.Net.Mail.MailAddress.TryCreate(contact, out _);
        if (configured)
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"AddressMapper/1.0 (+mailto:{contact})");
    }

    public async Task LocateAsync(AddressLocation row, CancellationToken ct)
    {
        row.Status = "geocodingUnavailable";
        if (!configured)
            return;
        await gate.WaitAsync(ct);
        try
        {
            var delay = nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
            try
            {
                using var response = await client.GetAsync(
                    $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q={Uri.EscapeDataString(row.Address)}", ct);
                response.EnsureSuccessStatusCode();
                var results = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (results.GetArrayLength() == 0)
                {
                    row.Status = "notFound";
                    return;
                }
                var result = results[0];
                if (double.TryParse(result.GetProperty("lat").GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(result.GetProperty("lon").GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lon)
                    && double.IsFinite(lat) && double.IsFinite(lon) && lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
                {
                    row.Latitude = lat;
                    row.Longitude = lon;
                    row.Status = "located";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                or KeyNotFoundException || ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                row.Status = "geocodingUnavailable";
            }
            finally
            {
                nextRequest = DateTimeOffset.UtcNow.AddSeconds(1.1);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        client.Dispose();
        gate.Dispose();
    }
}
