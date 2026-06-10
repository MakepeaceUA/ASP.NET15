namespace CinemaBookingApi.Models;

public class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int MovieShowId { get; set; }
    public int NumberOfSeats { get; set; }
    public DateTime BookingTime { get; set; }
}