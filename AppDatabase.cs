using SQLite;

namespace FACE;

public sealed class AppDatabase
{
    private readonly SQLiteAsyncConnection _database;
    private bool _initialized;

    public static string DatabaseDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FACE_Database");

    public static string DatabasePath =>
        Path.Combine(DatabaseDirectory, "landslide_warning.db3");

    public AppDatabase()
    {
        Directory.CreateDirectory(DatabaseDirectory);
        CopyOldDatabaseIfNeeded();

        _database = new SQLiteAsyncConnection(DatabasePath);
    }

    private static void CopyOldDatabaseIfNeeded()
    {
        var oldPath = Path.Combine(FileSystem.AppDataDirectory, "landslide_warning.db3");

        if (!File.Exists(DatabasePath) && File.Exists(oldPath))
        {
            File.Copy(oldPath, DatabasePath);
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _database.CreateTableAsync<AppUserRecord>();
        await _database.CreateTableAsync<SensorReading>();
        await _database.CreateTableAsync<CitizenReport>();
        await _database.CreateTableAsync<AlertNotification>();
        await EnsureSensorReadingSchemaAsync();

        if (await _database.Table<SensorReading>().CountAsync() == 0)
        {
            await _database.InsertAsync(new SensorReading
            {
                WaterHumidity = 76,
                VibrationLevel = 2.8,
                Status = "Siaga",
                RecordedAt = DateTime.UtcNow
            });
        }

        if (await _database.Table<CitizenReport>().CountAsync() == 0)
        {
            await _database.InsertAllAsync(new[]
            {
                new CitizenReport
                {
                    Reporter = "Warga",
                    Message = "Retakan kecil terlihat di dekat jalan desa",
                    Status = "Perlu dicek operator",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-110)
                },
                new CitizenReport
                {
                    Reporter = "Warga",
                    Message = "Air mengalir lebih deras dari area tebing",
                    Status = "Dipantau",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-75)
                },
                new CitizenReport
                {
                    Reporter = "Warga",
                    Message = "Tanah terasa basah di area pemukiman bawah",
                    Status = "Menunggu tindak lanjut",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-50)
                }
            });
        }

        if (await _database.Table<AlertNotification>().CountAsync() == 0)
        {
            await _database.InsertAsync(new AlertNotification
            {
                Sender = "Operator",
                TargetRole = "Warga",
                Message = "Pantau kondisi sekitar lereng dan segera laporkan jika melihat retakan tanah atau aliran air baru.",
                CreatedAt = DateTime.UtcNow.AddMinutes(-30)
            });
        }

        _initialized = true;
    }

    private async Task EnsureSensorReadingSchemaAsync()
    {
        try
        {
            await _database.ExecuteAsync(
                "ALTER TABLE SensorReading ADD COLUMN VibrationLevel real NOT NULL DEFAULT 2.8");
        }
        catch (SQLiteException)
        {
            // Kolom sudah ada pada database yang lebih baru.
        }
    }

    public async Task SaveUserAsync(string username, string password, string role, string facePhotoPath)
    {
        await InitializeAsync();

        await _database.InsertOrReplaceAsync(new AppUserRecord
        {
            Username = username,
            Password = password,
            Role = role,
            FacePhotoPath = facePhotoPath,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task<AppUserRecord?> GetUserAsync(string username)
    {
        await InitializeAsync();

        return await _database.Table<AppUserRecord>()
            .Where(user => user.Username == username)
            .FirstOrDefaultAsync();
    }

    public async Task<SensorReading> GetLatestSensorReadingAsync()
    {
        await InitializeAsync();

        return await _database.Table<SensorReading>()
            .OrderByDescending(reading => reading.RecordedAt)
            .FirstAsync();
    }

    public async Task<List<CitizenReport>> GetLatestReportsAsync(int limit = 5)
    {
        await InitializeAsync();

        return await _database.Table<CitizenReport>()
            .OrderByDescending(report => report.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task AddReportAsync(string reporter, string message)
    {
        await InitializeAsync();

        await _database.InsertAsync(new CitizenReport
        {
            Reporter = reporter,
            Message = message,
            Status = "Baru",
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task AddNotificationAsync(string sender, string message, string targetRole = "Warga")
    {
        await InitializeAsync();

        await _database.InsertAsync(new AlertNotification
        {
            Sender = sender,
            TargetRole = targetRole,
            Message = message,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task<List<AlertNotification>> GetLatestNotificationsAsync(string role, int limit = 5)
    {
        await InitializeAsync();

        var query = _database.Table<AlertNotification>()
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(limit);

        if (role.Trim().Equals("Operator", StringComparison.OrdinalIgnoreCase))
        {
            return await query.ToListAsync();
        }

        return await _database.Table<AlertNotification>()
            .Where(notification => notification.TargetRole == role)
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task MarkNotificationAcceptedAsync(int notificationId, string acceptedBy)
    {
        await InitializeAsync();

        var notification = await _database.Table<AlertNotification>()
            .Where(item => item.Id == notificationId)
            .FirstOrDefaultAsync();

        if (notification == null)
        {
            return;
        }

        notification.IsAccepted = true;
        notification.AcceptedBy = acceptedBy;
        notification.AcceptedAt = DateTime.UtcNow;

        await _database.UpdateAsync(notification);
    }
}
