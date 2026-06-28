namespace FACE;

public sealed class NlpInsightService
{
    private static readonly HttpClient HttpClient = new();

    // === LANGSUNG HARDCODE DI SINI ===
    private const string API_KEY = "ISI_API_KEY_KAMU_DI_SINI";
    private const string ENDPOINT = "https://api.openai.com/v1/chat/completions";
    private const string MODEL = "gpt-4o-mini";

    public async Task<string> GeneratePlainLanguageSummaryAsync(
        SensorReading sensor,
        IReadOnlyCollection<CitizenReport> reports,
        RiskAnalysisResult risk,
        CancellationToken cancellationToken = default)
    {
        // tidak perlu Preferences lagi
        if (string.IsNullOrWhiteSpace(API_KEY))
        {
            return risk.Explanation;
        }

        var prompt = $"""
        Jelaskan status early warning landslide untuk warga awam dalam 2 kalimat.
        Data:
        - Kelembapan air: {sensor.WaterHumidity:F0}%
        - Status: {risk.Status}
        - Nilai fuzzy: {risk.FuzzyRisk:F2}
        - Nilai neural: {risk.NeuralRisk:F2}
        - Jumlah laporan: {reports.Count}
        Beri saran tindakan singkat.
        """;

        using var request = new HttpRequestMessage(HttpMethod.Post, ENDPOINT);

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", API_KEY);

        request.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                model = MODEL,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Kamu adalah asisten early warning bencana yang menjelaskan risiko secara sederhana."
                    },
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                temperature = 0.2
            }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return risk.Explanation;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(content);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?? risk.Explanation;
    }
}