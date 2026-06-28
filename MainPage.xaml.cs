namespace FACE;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var username = EntryUsername.Text?.Trim();
        var password = EntryPassword.Text;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlert("Error", "Username dan password harus diisi!", "OK");
            return;
        }

        var database = new AppDatabase();
        var userRecord = await database.GetUserAsync(username);

        if (userRecord != null)
        {
            if (password != userRecord.Password)
            {
                await DisplayAlert("Ditolak", "Password salah!", "OK");
                return;
            }

            Preferences.Set("session_username", userRecord.Username);
            Preferences.Set("session_role", userRecord.Role);
            Application.Current!.MainPage = new FaceVerificationPage(userRecord.Username, userRecord.Role);
            return;
        }

        if (!Preferences.ContainsKey($"user_pass_{username}"))
        {
            await DisplayAlert("Ditolak", "Username tidak ditemukan!", "OK");
            return;
        }

        if (password != Preferences.Get($"user_pass_{username}", string.Empty))
        {
            await DisplayAlert("Ditolak", "Password salah!", "OK");
            return;
        }

        var role = Preferences.Get($"user_role_{username}", "Warga");
        var imagePath = Path.Combine(FileSystem.AppDataDirectory, $"{username}_face.png");

        await database.SaveUserAsync(username, password, role, imagePath);

        Preferences.Set("session_username", username);
        Preferences.Set("session_role", role);
        Application.Current!.MainPage = new FaceVerificationPage(username, role);
    }

    private void OnGoToRegisterClicked(object sender, EventArgs e)
    {
        Application.Current!.MainPage = new RegisterPage();
    }
}
