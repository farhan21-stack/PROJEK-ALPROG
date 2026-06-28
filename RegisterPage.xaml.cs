using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using System;
using System.IO;

#if WINDOWS
using OpenCvSharp;
#endif

namespace FACE
{
    public partial class RegisterPage : ContentPage
    {
        private byte[]? _rawFaceData;
        private byte[]? _normalizedFaceData;

        public RegisterPage()
        {
            InitializeComponent();
            SetupRolePicker();
        }

        private void SetupRolePicker()
        {
            try
            {
                PickerRole.Items.Clear();
                PickerRole.Items.Add("Warga");
                PickerRole.Items.Add("Operator");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error SetupRolePicker: {ex.Message}");
            }
        }

        private async void OnCaptureFaceClicked(object sender, EventArgs e)
        {
            try
            {
                var status = await Permissions.RequestAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    await DisplayAlert("Error", "Izin kamera diperlukan!", "OK");
                    return;
                }

                var photo = await MediaPicker.Default.CapturePhotoAsync();
                if (photo != null)
                {
                    using var stream = await photo.OpenReadAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    _rawFaceData = memoryStream.ToArray();

#if WINDOWS
                    _normalizedFaceData = await NormalizeCapturedFaceAsync(_rawFaceData);

                    if (_normalizedFaceData == null)
                    {
                        _rawFaceData = null;
                        CameraPreview.Source = null;
                        LabelFaceCaptureStatus.Text = "Wajah belum terdeteksi. Ambil ulang dengan wajah lurus dan pencahayaan cukup.";

                        await DisplayAlert(
                            "Wajah Tidak Terdeteksi",
                            "Foto belum memiliki wajah yang jelas. Dekatkan wajah ke kamera, lihat lurus, dan pastikan cahaya cukup.",
                            "OK");
                        return;
                    }

                    CameraPreview.Source = ImageSource.FromStream(() => new MemoryStream(_normalizedFaceData));
                    LabelFaceCaptureStatus.Text = "Wajah valid. Template wajah sudah dinormalisasi untuk login.";
#else
                    _normalizedFaceData = _rawFaceData;
                    CameraPreview.Source = ImageSource.FromStream(() => new MemoryStream(_rawFaceData));
                    LabelFaceCaptureStatus.Text = "Foto wajah berhasil diambil.";
#endif
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Gagal mengambil foto: {ex.Message}", "OK");
            }
        }

        private async void OnRegisterClicked(object sender, EventArgs e)
        {
            try
            {
                string user = EntryUsername?.Text?.Trim();
                string pass = EntryPassword?.Text;

                if (string.IsNullOrEmpty(user))
                {
                    await DisplayAlert("Gagal", "Username harus diisi!", "OK");
                    return;
                }

                if (string.IsNullOrEmpty(pass))
                {
                    await DisplayAlert("Gagal", "Password harus diisi!", "OK");
                    return;
                }

                if (PickerRole.SelectedIndex == -1)
                {
                    await DisplayAlert("Gagal", "Pilih hak akses terlebih dahulu!", "OK");
                    return;
                }

                if (_rawFaceData == null || _normalizedFaceData == null)
                {
                    await DisplayAlert("Gagal", "Foto wajah yang valid wajib diambil!", "OK");
                    return;
                }

                if (Preferences.ContainsKey($"user_pass_{user}"))
                {
                    await DisplayAlert("Gagal", "Username sudah terdaftar!", "OK");
                    return;
                }

                string selectedRole = PickerRole.SelectedItem.ToString();

                Preferences.Set($"user_pass_{user}", pass);
                Preferences.Set($"user_role_{user}", selectedRole);

                string imagePath = Path.Combine(FileSystem.AppDataDirectory, $"{user}_face.png");
                string normalizedImagePath = Path.Combine(FileSystem.AppDataDirectory, $"{user}_face_normalized.png");

                File.WriteAllBytes(imagePath, _rawFaceData);
                File.WriteAllBytes(normalizedImagePath, _normalizedFaceData);

                await new AppDatabase().SaveUserAsync(user, pass, selectedRole, imagePath);

                await DisplayAlert("Sukses", $"User '{user}' berhasil didaftarkan!\nRole: {selectedRole}", "OK");
                Application.Current.MainPage = new MainPage();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Terjadi kesalahan: {ex.Message}", "OK");
            }
        }

        private void OnBackToLoginClicked(object sender, EventArgs e)
        {
            Application.Current.MainPage = new MainPage();
        }

#if WINDOWS
        private static async Task<byte[]?> NormalizeCapturedFaceAsync(byte[] photoBytes)
        {
            var cascadePath = await EnsureCascadeFileAsync();
            using var faceCascade = new CascadeClassifier(cascadePath);

            if (faceCascade.Empty())
            {
                return null;
            }

            using var encoded = Mat.FromImageData(photoBytes, ImreadModes.Color);
            using var normalizedFace = ExtractNormalizedFace(encoded, faceCascade);

            if (normalizedFace.Empty())
            {
                return null;
            }

            Cv2.ImEncode(".png", normalizedFace, out var normalizedBytes);
            return normalizedBytes;
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
#endif
    }
}
