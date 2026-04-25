namespace Email.Models
{
    /// <summary>
    /// 2025-11-22 00:00 UTC - Binds to appsettings Authentication section.
    /// </summary>
    public class AuthenticationSettings
    {
        public int SessionTimeoutMinutes { get; set; } = 1440;
        public string CookieName { get; set; } = "CityShufflesGuidesAuth";
        public bool RequireHttps { get; set; } = false;
    }
}


