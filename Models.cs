namespace MFAAuthApp.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public byte[]? TotpSecret { get; set; }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class VerifyRequest
    {
        public string Username { get; set; } = "";
        public string Code { get; set; } = "";
    }
}
