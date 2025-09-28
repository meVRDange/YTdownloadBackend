namespace YTdownloadBackend.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;

    // You can add more fields later (email, etc.)
}
