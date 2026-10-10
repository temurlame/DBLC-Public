using System.Net.Http;
using System.Text.Json;

namespace DBLC;

public sealed class LlamaModelInfoClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    public async Task<int?> GetContextLimitAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var baseUrl = endpoint.Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) baseUrl = baseUrl[..^3];
        try
        {
            using var response = await Http.GetAsync(baseUrl + "/slots", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() > 0 && json.RootElement[0].TryGetProperty("n_ctx", out var slotLimit) && slotLimit.TryGetInt32(out var slotValue)) return slotValue;
            }
        }
        catch { }
        try
        {
            using var response = await Http.GetAsync(baseUrl + "/props", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.TryGetProperty("default_generation_settings", out var defaults) && defaults.TryGetProperty("n_ctx", out var limit) && limit.TryGetInt32(out var value) ? value : null;
        }
        catch { return null; }
    }

    public async Task<string?> GetModelIdAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var baseUrl = endpoint.Trim().TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) baseUrl += "/v1";
        using var response = await Http.GetAsync(baseUrl + "/models", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 && data[0].TryGetProperty("id", out var id)
            ? id.GetString() : null;
    }
}
