using SQLite;

namespace FACE;

public sealed class AppUserRecord
{
    [PrimaryKey]
    public string Username { get; set; } = string.Empty;

    public string Role { get; set; } = "Warga";

    public string Password { get; set; } = string.Empty;

    public string FacePhotoPath { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SensorReading
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public double WaterHumidity { get; set; }

    public double VibrationLevel { get; set; }

    public string Status { get; set; } = "Normal";

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CitizenReport
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Reporter { get; set; } = "Warga";

    public string Message { get; set; } = string.Empty;

    public string Status { get; set; } = "Baru";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AlertNotification
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Sender { get; set; } = "Operator";

    public string TargetRole { get; set; } = "Warga";

    public string Message { get; set; } = string.Empty;

    public bool IsAccepted { get; set; }

    public string AcceptedBy { get; set; } = string.Empty;

    public DateTime? AcceptedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
