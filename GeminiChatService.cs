using System.Text;
using System.Text.Json;

namespace FACE;

public sealed class GeminiChatService
{
    private static readonly HttpClient HttpClient = new();

    public async Task<string> AskAsync(
        string question,
        SensorReading sensor,
        IReadOnlyCollection<CitizenReport> reports,
        RiskAnalysisResult risk,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BuildLocalAnswer(question, sensor, reports, risk);
        }

        var model = Preferences.Get("gemini_model", "gemini-2.0-flash");
        var prompt = BuildPrompt(question, sensor, reports, risk);
        var requestUrl =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 220
                }
            }),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return BuildLocalAnswer(question, sensor, reports, risk);
            }

            using var document = JsonDocument.Parse(content);
            var candidates = document.RootElement.GetProperty("candidates");

            if (candidates.GetArrayLength() == 0)
            {
                return BuildLocalAnswer(question, sensor, reports, risk);
            }

            return candidates[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? BuildLocalAnswer(question, sensor, reports, risk);
        }
        catch
        {
            return BuildLocalAnswer(question, sensor, reports, risk);
        }
    }

    private static string ResolveApiKey()
    {
        var savedKey = Preferences.Get("gemini_api_key", string.Empty);

        if (!string.IsNullOrWhiteSpace(savedKey))
        {
            return savedKey;
        }

        return Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
            ?? string.Empty;
    }

    private static string BuildPrompt(
        string question,
        SensorReading sensor,
        IReadOnlyCollection<CitizenReport> reports,
        RiskAnalysisResult risk)
    {
        return $"""
        Kamu adalah chatbot AI untuk warga pada aplikasi Early Warning Landslide.
        Jawab dengan bahasa Indonesia sederhana, tenang, dan mudah dipahami orang awam.
        Jangan membuat kepastian palsu. Jika kondisi tampak berisiko, sarankan warga menghindari lereng dan mengikuti arahan operator.

        Data sistem saat ini:
        - Kelembapan air: {sensor.WaterHumidity:F0}%
        - Getaran lereng: {sensor.VibrationLevel:F1} mm/s
        - Status risiko: {risk.Status}
        - Skor risiko gabungan: {risk.CombinedRisk:P0}
        - Jumlah laporan warga terbaru: {reports.Count}
        - Rekomendasi sistem: {risk.Recommendation}

        Pertanyaan warga:
        {question}

        Jawab maksimal 4 kalimat. Beri langkah praktis jika pertanyaannya meminta saran.
        """;
    }

    private static string BuildLocalAnswer(
        string question,
        SensorReading sensor,
        IReadOnlyCollection<CitizenReport> reports,
        RiskAnalysisResult risk)
    {
        var lowerQuestion = question.ToLowerInvariant();

        if (lowerQuestion.Contains("apa yang harus") ||
            lowerQuestion.Contains("saran") ||
            lowerQuestion.Contains("lakukan") ||
            lowerQuestion.Contains("evakuasi"))
        {
            return $"{risk.Recommendation} Saat ini status {risk.Status}, kelembapan {sensor.WaterHumidity:F0}%, getaran {sensor.VibrationLevel:F1} mm/s, dan ada {reports.Count} laporan warga. Ikuti arahan operator dan hindari area lereng bila melihat retakan, tanah basah, atau aliran air baru.";
        }

        if (lowerQuestion.Contains("status") ||
            lowerQuestion.Contains("bahaya") ||
            lowerQuestion.Contains("siaga") ||
            lowerQuestion.Contains("normal"))
        {
            return $"Status saat ini adalah {risk.Status} dengan skor risiko {risk.CombinedRisk:P0}. Sistem menilai dari kelembapan air, getaran lereng, dan laporan warga. {risk.Explanation}";
        }

        if (lowerQuestion.Contains("kelembapan") || lowerQuestion.Contains("air"))
        {
            return $"Kelembapan air saat ini {sensor.WaterHumidity:F0}%. Semakin tinggi kelembapan, tanah dapat menjadi lebih mudah bergerak, terutama bila disertai getaran atau laporan retakan dari warga.";
        }

        if (lowerQuestion.Contains("getaran") || lowerQuestion.Contains("vibration"))
        {
            return $"Getaran lereng saat ini {sensor.VibrationLevel:F1} mm/s. Getaran yang meningkat bisa menjadi tanda tanah bergerak atau ada gangguan di lereng, sehingga perlu dipantau bersama kelembapan dan laporan warga.";
        }

        if (lowerQuestion.Contains("laporan"))
        {
            return $"Saat ini ada {reports.Count} laporan warga yang dipakai sebagai bahan analisis. Laporan seperti retakan tanah, air mengalir deras, atau tanah basah membantu operator memahami kondisi lapangan.";
        }

        return $"Saya bisa membantu menjelaskan status longsor, kelembapan, getaran, laporan warga, dan tindakan aman. Saat ini status sistem adalah {risk.Status}; {risk.Recommendation}";
    }
}
