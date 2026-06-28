using Microsoft.Maui.Controls.Shapes;

namespace FACE
{
    public partial class MainDashboardPage : ContentPage
    {
        private readonly string _username;
        private readonly string _role;
        private readonly AppDatabase _database = new();
        private readonly RiskAnalysisService _riskAnalysis = new();
        private readonly NlpInsightService _nlpInsight = new();
        private readonly GeminiChatService _geminiChat = new();

        public MainDashboardPage()
            : this(
                Preferences.Get("session_username", "Pengguna"),
                Preferences.Get("session_role", "Warga"))
        {
        }

        public MainDashboardPage(string username, string role)
        {
            InitializeComponent();

            _username = username;
            _role = NormalizeRole(role);

            ApplySession();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadDashboardAsync();
        }

        private void ApplySession()
        {
            LabelUserRole.Text = $"Masuk sebagai {_username} | Role: {_role}";
            LabelDatabasePath.Text = $"Database: {AppDatabase.DatabasePath}";

            var isOperator = _role == "Operator";

            LabelReportSummary.Text = isOperator
                ? "Operator dapat memantau dan menindaklanjuti laporan warga."
                : "Warga dapat melihat ringkasan laporan sekitar dan menambahkan laporan baru.";

            BtnReportAction.Text = isOperator
                ? "Tinjau Laporan"
                : "Tambah Laporan";

            LabelNotificationTitle.Text = isOperator
                ? "Pemberitahuan Operator"
                : "Penerimaan Pemberitahuan";

            LabelNotificationSummary.Text = isOperator
                ? "Operator dapat mengirim pemberitahuan kondisi lereng kepada warga."
                : "Warga menerima pemberitahuan dari operator dan menandai setelah dibaca.";

            BtnNotificationAction.IsVisible = isOperator;
            GeminiChatPanel.IsVisible = !isOperator;
        }

        private async Task LoadDashboardAsync()
        {
            var sensor = await _database.GetLatestSensorReadingAsync();
            var reports = await _database.GetLatestReportsAsync();
            var notifications = await _database.GetLatestNotificationsAsync(_role);
            var risk = _riskAnalysis.Analyze(sensor.WaterHumidity, sensor.VibrationLevel, reports.Count);
            var nlpSummary = await _nlpInsight.GeneratePlainLanguageSummaryAsync(sensor, reports, risk);

            LabelWaterHumidity.Text = $"{sensor.WaterHumidity:F0}%";
            LabelVibrationLevel.Text = $"{sensor.VibrationLevel:F1} mm/s";
            LabelAlertStatus.Text = risk.Status;
            LabelStatusDescription.Text = $"Fuzzy: {risk.FuzzyRisk:P0} | NN: {risk.NeuralRisk:P0}";
            LabelAiExplanation.Text = nlpSummary;
            LabelRiskScore.Text = $"Skor risiko gabungan: {risk.CombinedRisk:P0}";
            LabelFuzzyExplanation.Text = risk.FuzzyExplanation;
            LabelNeuralExplanation.Text = risk.NeuralExplanation;
            LabelRecommendation.Text = risk.Recommendation;

            RenderNotifications(notifications);
            RenderReports(reports);
        }

        private void RenderNotifications(IEnumerable<AlertNotification> notifications)
        {
            NotificationsList.Clear();

            foreach (var notification in notifications)
            {
                var acceptedText = notification.IsAccepted
                    ? $"Diterima oleh {notification.AcceptedBy} - {notification.AcceptedAt?.ToLocalTime():HH:mm}"
                    : "Belum ditandai diterima";

                var content = new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Label
                        {
                            Text = notification.Message,
                            FontSize = 15,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#123026")
                        },
                        new Label
                        {
                            Text = $"Dikirim {notification.Sender} - {notification.CreatedAt.ToLocalTime():HH:mm} - {acceptedText}",
                            FontSize = 13,
                            TextColor = Color.FromArgb("#5B6B63")
                        }
                    }
                };

                if (_role == "Warga" && !notification.IsAccepted)
                {
                    var acceptButton = new Button
                    {
                        Text = "Tandai Diterima",
                        BackgroundColor = Color.FromArgb("#17663A"),
                        TextColor = Colors.White,
                        CornerRadius = 8,
                        HorizontalOptions = LayoutOptions.Start
                    };

                    acceptButton.Clicked += async (_, _) =>
                    {
                        await _database.MarkNotificationAcceptedAsync(notification.Id, _username);
                        await LoadDashboardAsync();
                    };

                    content.Children.Add(acceptButton);
                }

                NotificationsList.Add(new Border
                {
                    BackgroundColor = notification.IsAccepted
                        ? Color.FromArgb("#F8FAF9")
                        : Color.FromArgb("#EFF6FF"),
                    Stroke = notification.IsAccepted
                        ? Color.FromArgb("#DCE5DF")
                        : Color.FromArgb("#93C5FD"),
                    StrokeThickness = 1,
                    Padding = 16,
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    Content = content
                });
            }
        }

        private void RenderReports(IEnumerable<CitizenReport> reports)
        {
            ReportsList.Clear();

            foreach (var report in reports)
            {
                ReportsList.Add(new Border
                {
                    BackgroundColor = Color.FromArgb("#F8FAF9"),
                    Stroke = Color.FromArgb("#DCE5DF"),
                    StrokeThickness = 1,
                    Padding = 16,
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    Content = new VerticalStackLayout
                    {
                        Spacing = 4,
                        Children =
                        {
                            new Label
                            {
                                Text = report.Message,
                                FontSize = 15,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#123026")
                            },
                            new Label
                            {
                                Text = $"Dilaporkan {report.Reporter} - {report.CreatedAt.ToLocalTime():HH:mm} - Status: {report.Status}",
                                FontSize = 13,
                                TextColor = Color.FromArgb("#5B6B63")
                            }
                        }
                    }
                });
            }
        }

        private static string NormalizeRole(string role)
        {
            return role.Trim().Equals("Operator", StringComparison.OrdinalIgnoreCase)
                ? "Operator"
                : "Warga";
        }

        private async void OnReportActionClicked(object sender, EventArgs e)
        {
            if (_role == "Operator")
            {
                await DisplayAlert("Laporan Warga", "Operator dapat meninjau laporan yang masuk pada daftar ini.", "OK");
                return;
            }

            var message = await DisplayPromptAsync(
                "Tambah Laporan",
                "Tuliskan kondisi yang terlihat di sekitar lokasi.",
                "Simpan",
                "Batal",
                "Contoh: tanah retak di dekat rumah");

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            await _database.AddReportAsync(_username, message.Trim());
            await LoadDashboardAsync();
        }

        private async void OnNotificationActionClicked(object sender, EventArgs e)
        {
            if (_role != "Operator")
            {
                return;
            }

            var message = await DisplayPromptAsync(
                "Kirim Pemberitahuan",
                "Tuliskan pemberitahuan yang akan diterima warga.",
                "Kirim",
                "Batal",
                "Contoh: Warga diminta menjauhi area lereng sampai pemeriksaan selesai.");

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            await _database.AddNotificationAsync(_username, message.Trim());
            await LoadDashboardAsync();
        }

        private async void OnGeminiAskClicked(object sender, EventArgs e)
        {
            if (_role != "Warga")
            {
                return;
            }

            var question = EntryGeminiQuestion.Text?.Trim();

            if (string.IsNullOrWhiteSpace(question))
            {
                await DisplayAlert("Pertanyaan Kosong", "Tuliskan pertanyaan terlebih dahulu.", "OK");
                return;
            }

            EntryGeminiQuestion.Text = string.Empty;
            AddChatBubble(_username, question, true);
            AddChatBubble("Gemini", "Sedang menyiapkan jawaban...", false);

            var sensor = await _database.GetLatestSensorReadingAsync();
            var reports = await _database.GetLatestReportsAsync();
            var risk = _riskAnalysis.Analyze(sensor.WaterHumidity, sensor.VibrationLevel, reports.Count);
            var answer = await _geminiChat.AskAsync(question, sensor, reports, risk);

            GeminiChatMessages.Children.RemoveAt(GeminiChatMessages.Children.Count - 1);
            AddChatBubble("Gemini", answer, false);
        }

        private void AddChatBubble(string sender, string message, bool isUser)
        {
            GeminiChatMessages.Add(new Border
            {
                BackgroundColor = isUser
                    ? Color.FromArgb("#DCFCE7")
                    : Color.FromArgb("#EEF2FF"),
                Stroke = isUser
                    ? Color.FromArgb("#86EFAC")
                    : Color.FromArgb("#A5B4FC"),
                StrokeThickness = 1,
                Padding = 14,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                HorizontalOptions = isUser ? LayoutOptions.End : LayoutOptions.Start,
                MaximumWidthRequest = 760,
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label
                        {
                            Text = sender,
                            FontSize = 12,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#123026")
                        },
                        new Label
                        {
                            Text = message,
                            FontSize = 14,
                            TextColor = Color.FromArgb("#1F2937")
                        }
                    }
                }
            });
        }

        private void OnLogoutClicked(object sender, EventArgs e)
        {
            Preferences.Remove("session_username");
            Preferences.Remove("session_role");
            Application.Current!.MainPage = new MainPage();
        }
    }
}
