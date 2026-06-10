using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CinemaBookingApi.Models;

namespace CinemaBookingApi.Database;

public class CinemaDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public CinemaDbContext(DbContextOptions<CinemaDbContext> options) : base(options) { }

    public DbSet<MovieShow> MovieShows => Set<MovieShow>();
    public DbSet<Booking> Bookings => Set<Booking>();
}