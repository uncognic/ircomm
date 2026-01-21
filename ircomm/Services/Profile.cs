namespace ircomm.Services
{
    public class Profile
    {
        public string? Name { get; set; }
        public string? Server { get; set; }
        public int Port { get; set; } = 6667;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public List<string> Channels { get; set; } = new();
    }
}