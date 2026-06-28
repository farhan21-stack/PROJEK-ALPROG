using System.IO;
using System.Threading;

#if WINDOWS
using OpenCvSharp;
using OpenCvSharp.Face;
#endif

namespace FACE
{
    public partial class FaceVerificationPage : ContentPage
    {
        private readonly string _username;
        private readonly string _role;
        private readonly string _dbPhotoPath;
        private readonly string _normalizedPhotoPath;

        private CancellationTokenSource? _scanCts;
        private bool _isNavigating;

        private const double MatchThreshold = 85.0;
        private const int RequiredSuccessFrames = 4;

        public FaceVerificationPage(string username, string role)
        {
            InitializeComponent();

            _username = username;
            _role = role;
            _dbPhotoPath = Path.Combine(FileSystem.AppDataDirectory, $"{username}_face.png");
            _normalizedPhotoPath = Path.Combine(FileSystem.AppDataDirectory, $"{username}_face_normalized.png");

            LabelRole.Text = $"Pengguna: {username} | Hak akses: {role}";
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartLiveFaceScan();
        }

        protected override void OnDisappearing()
        {
            StopLiveFaceScan();
            base.OnDisappearing();
        }

        private void OnStartScanClicked(object sender, EventArgs e)
        {
            StartLiveFaceScan();
        }

        private void StartLiveFaceScan()
        {
            StopLiveFaceScan();

            _isNavigating = false;
            _scanCts = new CancellationTokenSource();

            ScanIndicator.IsVisible = true;
            ScanIndicator.IsRunning = true;
            LabelSimilarity.Text = "Status: membuka kamera laptop...";
            LabelStatus.Text = "Pemindaian berjalan otomatis. Tetap berada di depan kamera hingga proses selesai.";

#if WINDOWS
            _ = Task.Run(() => RunWindowsLiveScanAsync(_scanCts.Token));
#else
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                ScanIndicator.IsRunning = false;
                ScanIndicator.IsVisible = false;
                LabelSimilarity.Text = "Live scan kamera laptop hanya aktif untuk Windows.";

                await DisplayAlert(
                    "Target Belum Didukung",
                    "Mode live face lock ini dibuat untuk Windows laptop. Jalankan project dengan target Windows Machine.",
                    "OK");
            });
#endif
        }

        private void StopLiveFaceScan()
        {
            try
            {
                _scanCts?.Cancel();
                _scanCts?.Dispose();
                _scanCts = null;
            }
            catch
            {
                // Abaikan error saat kamera sedang ditutup.
            }
        }

#if WINDOWS
        private async Task RunWindowsLiveScanAsync(CancellationToken token)
        {
            var referencePath = File.Exists(_normalizedPhotoPath)
                ? _normalizedPhotoPath
                : _dbPhotoPath;

            if (!File.Exists(referencePath))
            {
                await ShowMessageOnUiAsync(
                    "Error Database",
                    "Foto wajah untuk user ini tidak ditemukan. Silakan register ulang dan ambil foto wajah.");
                return;
            }

            var cascadePath = await EnsureCascadeFileAsync();
            using var faceCascade = new CascadeClassifier(cascadePath);

            if (faceCascade.Empty())
            {
                await ShowMessageOnUiAsync("Error", "File Haar Cascade tidak bisa dibaca.");
                return;
            }

            using var referenceFace = LoadReferenceFace(referencePath, faceCascade);

            if (referenceFace.Empty())
            {
                await ShowMessageOnUiAsync(
                    "Wajah Tidak Terdeteksi",
                    "Foto registrasi tidak memiliki wajah yang jelas. Silakan register ulang dengan posisi wajah lurus dan pencahayaan cukup.");
                return;
            }

            using var recognizer = LBPHFaceRecognizer.Create(radius: 2, neighbors: 8, gridX: 8, gridY: 8);
            var trainingFaces = CreateAugmentedTrainingFaces(referenceFace);

            try
            {
                recognizer.Train(
                    trainingFaces.ToArray(),
                    Enumerable.Repeat(1, trainingFaces.Count).ToArray());
            }
            finally
            {
                foreach (var trainingFace in trainingFaces)
                {
                    trainingFace.Dispose();
                }
            }

            using var capture = new VideoCapture(0);

            if (!capture.IsOpened())
            {
                await ShowMessageOnUiAsync(
                    "Kamera Tidak Terbuka",
                    "Kamera laptop tidak bisa dibuka. Pastikan kamera tidak sedang dipakai aplikasi lain dan izin kamera aktif.");
                return;
            }

            var successFrames = 0;

            using var frame = new Mat();
            using var gray = new Mat();

            while (!token.IsCancellationRequested && !_isNavigating)
            {
                capture.Read(frame);

                if (frame.Empty())
                {
                    await Task.Delay(80, token);
                    continue;
                }

                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.EqualizeHist(gray, gray);

                var faces = faceCascade.DetectMultiScale(
                    gray,
                    scaleFactor: 1.1,
                    minNeighbors: 5,
                    flags: HaarDetectionTypes.ScaleImage,
                    minSize: new OpenCvSharp.Size(90, 90));

                var statusText = "Status: wajah belum terdeteksi";

                if (faces.Length > 0)
                {
                    var faceRect = faces
                        .OrderByDescending(rect => rect.Width * rect.Height)
                        .First();

                    if (faceRect.Width < 120 || faceRect.Height < 120)
                    {
                        successFrames = 0;
                        Cv2.Rectangle(frame, faceRect, Scalar.Orange, 2);

                        await UpdatePreviewOnUiAsync(
                            frame,
                            "Wajah terlalu jauh. Dekatkan wajah ke kamera.");

                        await Task.Delay(60, token);
                        continue;
                    }

                    Cv2.Rectangle(frame, faceRect, Scalar.LimeGreen, 2);

                    using var liveFace = NormalizeFaceFromGray(gray, faceRect);

                    recognizer.Predict(liveFace, out var label, out var confidence);

                    var isMatch = label == 1 && confidence > 0 && confidence <= MatchThreshold;
                    successFrames = isMatch ? successFrames + 1 : 0;

                    statusText = isMatch
                        ? $"Wajah cocok {successFrames}/{RequiredSuccessFrames} | confidence: {confidence:F2}"
                        : $"Wajah tidak cocok | confidence: {confidence:F2}";

                    if (successFrames >= RequiredSuccessFrames)
                    {
                        _isNavigating = true;
                        await NavigateToDashboardAsync();
                        break;
                    }
                }
                else
                {
                    successFrames = 0;
                }

                await UpdatePreviewOnUiAsync(frame, statusText);
                await Task.Delay(60, token);
            }
        }

        private static Mat LoadReferenceFace(string referencePath, CascadeClassifier faceCascade)
        {
            using var referenceGray = Cv2.ImRead(referencePath, ImreadModes.Grayscale);

            if (!referenceGray.Empty() && referenceGray.Width == 224 && referenceGray.Height == 224)
            {
                var normalized = referenceGray.Clone();
                Cv2.EqualizeHist(normalized, normalized);
                return normalized;
            }

            using var referenceImage = Cv2.ImRead(referencePath, ImreadModes.Color);

            if (referenceImage.Empty())
            {
                return new Mat();
            }

            return ExtractNormalizedFace(referenceImage, faceCascade);
        }

        private static Mat ExtractNormalizedFace(Mat image, CascadeClassifier faceCascade)
        {
            using var gray = new Mat();

            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.EqualizeHist(gray, gray);

            var faces = faceCascade.DetectMultiScale(
                gray,
                scaleFactor: 1.1,
                minNeighbors: 5,
                flags: HaarDetectionTypes.ScaleImage,
                minSize: new OpenCvSharp.Size(90, 90));

            if (faces.Length == 0)
            {
                return new Mat();
            }

            var faceRect = faces
                .OrderByDescending(rect => rect.Width * rect.Height)
                .First();

            return NormalizeFaceFromGray(gray, faceRect);
        }

        private static Mat NormalizeFaceFromGray(Mat gray, OpenCvSharp.Rect faceRect)
        {
            using var face = new Mat(gray, faceRect);
            var resized = new Mat();

            Cv2.Resize(face, resized, new OpenCvSharp.Size(224, 224));

            using var clahe = Cv2.CreateCLAHE(
                clipLimit: 2.0,
                tileGridSize: new OpenCvSharp.Size(8, 8));

            clahe.Apply(resized, resized);
            Cv2.EqualizeHist(resized, resized);

            return resized;
        }

        private static List<Mat> CreateAugmentedTrainingFaces(Mat referenceFace)
        {
            var faces = new List<Mat>
            {
                referenceFace.Clone()
            };

            var flipped = new Mat();
            Cv2.Flip(referenceFace, flipped, FlipMode.Y);
            faces.Add(flipped);

            faces.Add(AdjustBrightnessContrast(referenceFace, alpha: 0.95, beta: -8));
            faces.Add(AdjustBrightnessContrast(referenceFace, alpha: 1.05, beta: 8));

            return faces;
        }

        private static Mat AdjustBrightnessContrast(Mat source, double alpha, double beta)
        {
            var adjusted = new Mat();
            source.ConvertTo(adjusted, source.Type(), alpha, beta);
            return adjusted;
        }

        private static async Task<string> EnsureCascadeFileAsync()
        {
            var targetPath = Path.Combine(
                FileSystem.AppDataDirectory,
                "haarcascade_frontalface_default.xml");

            if (File.Exists(targetPath))
            {
                return targetPath;
            }

            await using var input =
                await FileSystem.OpenAppPackageFileAsync("haarcascade_frontalface_default.xml");

            await using var output = File.Create(targetPath);
            await input.CopyToAsync(output);

            return targetPath;
        }

        private Task UpdatePreviewOnUiAsync(Mat frame, string statusText)
        {
            Cv2.ImEncode(".jpg", frame, out var imageBytes);

            return MainThread.InvokeOnMainThreadAsync(() =>
            {
                LabelSimilarity.Text = statusText;
                CameraPreview.Source = ImageSource.FromStream(() => new MemoryStream(imageBytes));
            });
        }
#endif

        private Task ShowMessageOnUiAsync(string title, string message)
        {
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                ScanIndicator.IsRunning = false;
                ScanIndicator.IsVisible = false;
                LabelSimilarity.Text = message;

                await DisplayAlert(title, message, "OK");
            });
        }

        private Task NavigateToDashboardAsync()
        {
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                StopLiveFaceScan();

                ScanIndicator.IsRunning = false;
                ScanIndicator.IsVisible = false;
                LabelSimilarity.Text = "Verifikasi berhasil. Mengalihkan ke dashboard...";

                await DisplayAlert(
                    "AKSES DITERIMA",
                    "Wajah cocok. Anda akan masuk ke dashboard.",
                    "OK");

                Application.Current!.MainPage = new MainDashboardPage(_username, _role);
            });
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            StopLiveFaceScan();
            Application.Current!.MainPage = new MainPage();
        }
    }
}
