namespace ircomm.Services
{
    public class Profile
    {
        public string? Name { get; set; }
        public string? Server { get; set; }
        public int Port { get; set; } = 6667;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool IsBouncer { get; set; }
        public string? BouncerUsername { get; set; }
        public string? BouncerPassword { get; set; }
        public string? BouncerNetwork { get; set; }
        public bool UseSsl { get; set; }
        public List<string> Channels { get; set; } = new();
    }
}