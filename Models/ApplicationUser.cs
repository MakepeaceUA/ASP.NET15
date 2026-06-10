using Microsoft.AspNetCore.Identity;

namespace CinemaBookingApi.Models;

public class ApplicationUser : IdentityUser<int>
{
    public string Name { get; set; } = string.Empty;
}