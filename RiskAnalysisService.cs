namespace FACE;

public sealed record RiskAnalysisResult(
    string Status,
    double FuzzyRisk,
    double NeuralRisk,
    double CombinedRisk,
    string Explanation,
    string FuzzyExplanation,
    string NeuralExplanation,
    string Recommendation);

public sealed class RiskAnalysisService
{
    private readonly FeedForwardRiskNetwork _network = new();

    public RiskAnalysisService()
    {
        _network.TrainDefaultSamples();
    }

    public RiskAnalysisResult Analyze(double waterHumidity, double vibrationLevel, int citizenReportCount)
    {
        var fuzzyState = CalculateFuzzyRisk(waterHumidity, vibrationLevel, citizenReportCount);
        var fuzzyRisk = fuzzyState.Risk;
        var neuralRisk = _network.Predict(
            waterHumidity / 100.0,
            Math.Clamp(vibrationLevel / 10.0, 0, 1),
            Math.Min(citizenReportCount, 5) / 5.0);
        var combinedRisk = fuzzyRisk * 0.65 + neuralRisk * 0.35;
        var status = combinedRisk switch
        {
            >= 0.72 => "Bahaya",
            >= 0.45 => "Siaga",
            _ => "Normal"
        };

        var explanation = BuildPlainExplanation(status, waterHumidity, vibrationLevel, citizenReportCount, combinedRisk);
        var recommendation = BuildRecommendation(status);
        var fuzzyExplanation =
            $"Fuzzy membaca kelembapan sebagai rendah {fuzzyState.LowHumidity:P0}, sedang {fuzzyState.MediumHumidity:P0}, tinggi {fuzzyState.HighHumidity:P0}. " +
            $"Getaran terbaca tenang {fuzzyState.LowVibration:P0}, meningkat {fuzzyState.MediumVibration:P0}, kuat {fuzzyState.HighVibration:P0}. " +
            $"Laporan warga terbaca sedikit {fuzzyState.FewReports:P0} dan banyak {fuzzyState.ManyReports:P0}.";

        var neuralExplanation =
            $"Neural network feed-forward memberi skor {neuralRisk:P0} dari pola kelembapan, getaran, dan jumlah laporan. " +
            "Bobotnya dilatih dengan backpropagation dari contoh kondisi normal, siaga, dan bahaya.";

        return new RiskAnalysisResult(
            status,
            fuzzyRisk,
            neuralRisk,
            combinedRisk,
            explanation,
            fuzzyExplanation,
            neuralExplanation,
            recommendation);
    }

    private static FuzzyRiskState CalculateFuzzyRisk(double humidity, double vibration, int reports)
    {
        var lowHumidity = Down(humidity, 35, 55);
        var mediumHumidity = Triangle(humidity, 40, 65, 82);
        var highHumidity = Up(humidity, 70, 88);

        var lowVibration = Down(vibration, 0.7, 2.0);
        var mediumVibration = Triangle(vibration, 1.2, 3.5, 5.5);
        var highVibration = Up(vibration, 4.5, 7.0);

        var fewReports = Down(reports, 0, 2);
        var manyReports = Up(reports, 1, 4);

        var safeRule = Math.Min(Math.Min(lowHumidity, lowVibration), fewReports) * 0.2;
        var alertRule = Math.Max(
            Math.Max(mediumHumidity, mediumVibration),
            Math.Min(highHumidity, fewReports)) * 0.6;
        var dangerRule = Math.Max(
            Math.Max(highHumidity, highVibration),
            Math.Max(
                Math.Min(mediumHumidity, manyReports),
                Math.Min(mediumVibration, manyReports))) * 0.9;
        var weight =
            lowHumidity + mediumHumidity + highHumidity +
            lowVibration + mediumVibration + highVibration + 0.001;
        var risk = Math.Clamp((safeRule + alertRule + dangerRule) / weight, 0, 1);

        return new FuzzyRiskState(
            risk,
            lowHumidity,
            mediumHumidity,
            highHumidity,
            lowVibration,
            mediumVibration,
            highVibration,
            fewReports,
            manyReports);
    }

    private static string BuildPlainExplanation(string status, double humidity, double vibration, int reports, double combinedRisk)
    {
        return status switch
        {
            "Bahaya" => $"Risiko tinggi ({combinedRisk:P0}). Kelembapan air {humidity:F0}%, getaran {vibration:F1} mm/s, dan {reports} laporan warga menunjukkan lereng perlu tindakan cepat.",
            "Siaga" => $"Risiko sedang ({combinedRisk:P0}). Kelembapan air {humidity:F0}% dan getaran {vibration:F1} mm/s mulai perlu diperhatikan, apalagi ada {reports} laporan warga.",
            _ => $"Risiko rendah ({combinedRisk:P0}). Kelembapan air {humidity:F0}% dan getaran {vibration:F1} mm/s masih relatif aman, tetapi pemantauan tetap dilakukan."
        };
    }

    private static string BuildRecommendation(string status)
    {
        return status switch
        {
            "Bahaya" => "Saran: operator segera cek lokasi, warga menjauhi lereng, dan jalur evakuasi disiapkan.",
            "Siaga" => "Saran: tingkatkan pemantauan, minta warga melaporkan retakan/tanah basah, dan cek sensor berkala.",
            _ => "Saran: lanjutkan pemantauan rutin dan edukasi warga agar segera melapor bila ada perubahan tanah."
        };
    }

    private static double Down(double x, double start, double end)
    {
        if (x <= start) return 1;
        if (x >= end) return 0;
        return (end - x) / (end - start);
    }

    private static double Up(double x, double start, double end)
    {
        if (x <= start) return 0;
        if (x >= end) return 1;
        return (x - start) / (end - start);
    }

    private static double Triangle(double x, double left, double center, double right)
    {
        if (x <= left || x >= right) return 0;
        if (Math.Abs(x - center) < 0.001) return 1;
        return x < center
            ? (x - left) / (center - left)
            : (right - x) / (right - center);
    }
}

public sealed record FuzzyRiskState(
    double Risk,
    double LowHumidity,
    double MediumHumidity,
    double HighHumidity,
    double LowVibration,
    double MediumVibration,
    double HighVibration,
    double FewReports,
    double ManyReports);

public sealed class FeedForwardRiskNetwork
{
    private readonly double[,] _inputHidden =
    {
        { 0.35, 0.25, -0.15 },
        { 0.28, 0.45, 0.35 },
        { 0.20, 0.40, 0.30 }
    };

    private readonly double[] _hiddenBias = { -0.25, -0.30, 0.10 };
    private readonly double[] _hiddenOutput = { 0.45, 0.50, 0.35 };
    private double _outputBias = -0.25;

    public double Predict(double normalizedHumidity, double normalizedVibration, double normalizedReports)
    {
        var hidden = CalculateHidden(normalizedHumidity, normalizedVibration, normalizedReports);
        var output = _outputBias;

        for (var i = 0; i < hidden.Length; i++)
        {
            output += hidden[i] * _hiddenOutput[i];
        }

        return Sigmoid(output);
    }

    public void TrainDefaultSamples()
    {
        var samples = new[]
        {
            (Humidity: 0.25, Vibration: 0.05, Reports: 0.00, Target: 0.10),
            (Humidity: 0.55, Vibration: 0.18, Reports: 0.20, Target: 0.38),
            (Humidity: 0.76, Vibration: 0.35, Reports: 0.60, Target: 0.70),
            (Humidity: 0.68, Vibration: 0.65, Reports: 0.30, Target: 0.78),
            (Humidity: 0.90, Vibration: 0.80, Reports: 0.80, Target: 0.94)
        };

        for (var epoch = 0; epoch < 220; epoch++)
        {
            foreach (var sample in samples)
            {
                Train(sample.Humidity, sample.Vibration, sample.Reports, sample.Target, learningRate: 0.08);
            }
        }
    }

    private void Train(double humidity, double vibration, double reports, double target, double learningRate)
    {
        var hidden = CalculateHidden(humidity, vibration, reports);
        var output = Predict(humidity, vibration, reports);
        var outputDelta = (target - output) * output * (1 - output);
        var previousHiddenOutput = _hiddenOutput.ToArray();

        for (var i = 0; i < _hiddenOutput.Length; i++)
        {
            _hiddenOutput[i] += learningRate * outputDelta * hidden[i];
        }

        _outputBias += learningRate * outputDelta;

        for (var i = 0; i < hidden.Length; i++)
        {
            var hiddenDelta = hidden[i] * (1 - hidden[i]) * previousHiddenOutput[i] * outputDelta;

            _inputHidden[0, i] += learningRate * hiddenDelta * humidity;
            _inputHidden[1, i] += learningRate * hiddenDelta * vibration;
            _inputHidden[2, i] += learningRate * hiddenDelta * reports;
            _hiddenBias[i] += learningRate * hiddenDelta;
        }
    }

    private double[] CalculateHidden(double humidity, double vibration, double reports)
    {
        var hidden = new double[_hiddenBias.Length];

        for (var i = 0; i < hidden.Length; i++)
        {
            hidden[i] = Sigmoid(
                humidity * _inputHidden[0, i] +
                vibration * _inputHidden[1, i] +
                reports * _inputHidden[2, i] +
                _hiddenBias[i]);
        }

        return hidden;
    }

    private static double Sigmoid(double x)
    {
        return 1.0 / (1.0 + Math.Exp(-x));
    }
}
