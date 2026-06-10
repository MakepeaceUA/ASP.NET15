using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CinemaBookingApi.Database;
using CinemaBookingApi.Dtos;
using CinemaBookingApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CinemaDbContext>(options =>
    options.UseInMemoryDatabase("CinemaDb"));

const string JwtKey = "123456SuperSecretKeyForJWTAuthorization78910!";
const string JwtIssuer = "CinemaApiIssuer";
const string JwtAudience = "CinemaApiAudience";

builder.Services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<CinemaDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthentication()
.AddGitHub(options =>
{
    options.ClientId = "Ov23liyeGUvEIAWTsLc0";
    options.ClientSecret = "f4beb9d8686956451c436c3ccf52f6421222d7b9";
    options.CallbackPath = "/signin-github";
    options.Scope.Add("user:email");
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = JwtIssuer,
        ValidAudience = JwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey))
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
    if (!db.MovieShows.Any())
    {
        db.MovieShows.AddRange(
            new MovieShow { Id = 1, Title = "Title01", StartTime = DateTime.Now.AddHours(3), Duration = 169, AvailableSeats = 50 },
            new MovieShow { Id = 2, Title = "Title02", StartTime = DateTime.Now.AddHours(5), Duration = 148, AvailableSeats = 40 },
            new MovieShow { Id = 3, Title = "Title03", StartTime = DateTime.Now.AddHours(8), Duration = 166, AvailableSeats = 60 }
        );
        db.SaveChanges();
    }
}

app.MapGet("/movies", async (CinemaDbContext db) => 
    await db.MovieShows.ToListAsync());

app.MapGet("/movies/{id:int}", async (int id, CinemaDbContext db) =>
{
    var show = await db.MovieShows.FindAsync(id);
    return show is not null ? Results.Ok(show) : Results.NotFound("Сеанс не найден.");
});

app.MapPost("/auth/register", async (RegisterDto dto, UserManager<ApplicationUser> userManager) =>
{
    var user = new ApplicationUser 
    { 
        UserName = dto.Email, 
        Email = dto.Email, 
        Name = dto.Name 
    };

    var result = await userManager.CreateAsync(user, dto.Password);
    if (!result.Succeeded)
    {
        var errors = result.Errors.Select(e => e.Description);
        return Results.BadRequest(new { errors });
    }

    return Results.Ok("Регистрация успешна.");
});

app.MapPost("/auth/login", async (LoginDto dto, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =>
{
    var user = await userManager.FindByEmailAsync(dto.Email);
    if (user is null)
        return Results.Json(new { message = "Неверный логин или пароль" }, statusCode: StatusCodes.Status401Unauthorized);

    var result = await signInManager.PasswordSignInAsync(user, dto.Password, isPersistent: false, lockoutOnFailure: false);
    if (!result.Succeeded)
        return Results.Json(new { message = "Неверный логин или пароль" }, statusCode: StatusCodes.Status401Unauthorized);

    return Results.Ok(new { message = "Вход выполнен успешно", user = user.Name });
});

app.MapPost("/auth/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Ok("Выход выполнен успешно.");
});

app.MapGet("/auth/login-github", (SignInManager<ApplicationUser> signInManager) =>
{
    var redirectUrl = "/auth/github-callback";
    var properties = signInManager.ConfigureExternalAuthenticationProperties("GitHub", redirectUrl);
    return Results.Challenge(properties, new[] { "GitHub" });
});

app.MapGet("/auth/github-callback", async (SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager) =>
{
    var info = await signInManager.GetExternalLoginInfoAsync();
    if (info == null) return Results.BadRequest("Error loading external login information.");

    var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
    if (result.Succeeded)
    {
        var userFromDb = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        var nameEscaped = Uri.EscapeDataString(userFromDb?.Name ?? "GitHub User");
        return Results.Redirect($"/?github_login=success&name={nameEscaped}");
    }

    var email = info.Principal.FindFirstValue(ClaimTypes.Email);
    var name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? info.Principal.FindFirstValue("urn:github:name") ?? "GitHub User";

    if (string.IsNullOrEmpty(email)) return Results.BadRequest("Email claim not received from GitHub.");

    var user = await userManager.FindByEmailAsync(email);
    if (user == null)
    {
        user = new ApplicationUser { UserName = email, Email = email, Name = name };
        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded) return Results.BadRequest(createResult.Errors);
    }

    var addLoginResult = await userManager.AddLoginAsync(user, info);
    if (!addLoginResult.Succeeded) return Results.BadRequest(addLoginResult.Errors);

    await signInManager.SignInAsync(user, isPersistent: false);
    var finalNameEscaped = Uri.EscapeDataString(user.Name);
    return Results.Redirect($"/?github_login=success&name={finalNameEscaped}");
});

app.MapGet("/bookings", async (CinemaDbContext db, ClaimsPrincipal principal) =>
{
    var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(userIdStr, out int userId))
        return Results.Unauthorized();

    var userBookings = await db.Bookings.Where(b => b.UserId == userId).ToListAsync();
    return Results.Ok(userBookings);
})
.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = IdentityConstants.ApplicationScheme });

app.MapPost("/bookings/create", async (BookingCreateDto dto, CinemaDbContext db, ClaimsPrincipal principal) =>
{
    var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(userIdStr, out int userId))
        return Results.Unauthorized();

    var movie = await db.MovieShows.FindAsync(dto.MovieShowId);
    if (movie is null)
        return Results.NotFound("Сеанс не найден.");

    if (movie.AvailableSeats < dto.NumberOfSeats)
        return Results.BadRequest("Недостаточно свободных мест.");

    movie.AvailableSeats -= dto.NumberOfSeats;

    var booking = new Booking
    {
        UserId = userId,
        MovieShowId = dto.MovieShowId,
        NumberOfSeats = dto.NumberOfSeats,
        BookingTime = DateTime.UtcNow
    };

    db.Bookings.Add(booking);
    await db.SaveChangesAsync();

    return Results.Ok(booking);
})
.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = IdentityConstants.ApplicationScheme });

app.MapPost("/jwt/register", async (RegisterDto dto, UserManager<ApplicationUser> userManager) =>
{
    var user = new ApplicationUser 
    { 
        UserName = dto.Email, 
        Email = dto.Email, 
        Name = dto.Name 
    };

    var result = await userManager.CreateAsync(user, dto.Password);
    if (!result.Succeeded)
    {
        var errors = result.Errors.Select(e => e.Description);
        return Results.BadRequest(new { errors });
    }

    return Results.Ok("Регистрация успешна.");
});

app.MapPost("/jwt/login", async (LoginDto dto, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =>
{
    var user = await userManager.FindByEmailAsync(dto.Email);
    if (user is null)
        return Results.Json(new { message = "Неверный логин или пароль" }, statusCode: StatusCodes.Status401Unauthorized);

    var result = await signInManager.CheckPasswordSignInAsync(user, dto.Password, false);
    if (!result.Succeeded)
        return Results.Json(new { message = "Неверный логин или пароль" }, statusCode: StatusCodes.Status401Unauthorized);

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
        new Claim(ClaimTypes.Name, user.Name)
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: JwtIssuer,
        audience: JwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(2),
        signingCredentials: creds
    );

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new TokenResponseDto(tokenString, user.Email ?? string.Empty));
});

app.MapGet("/jwt/bookings", async (CinemaDbContext db, ClaimsPrincipal principal) =>
{
    var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(userIdStr, out int userId))
        return Results.Unauthorized();

    var userBookings = await db.Bookings.Where(b => b.UserId == userId).ToListAsync();
    return Results.Ok(userBookings);
})
.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

app.MapPost("/jwt/bookings/create", async (BookingCreateDto dto, CinemaDbContext db, ClaimsPrincipal principal) =>
{
    var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(userIdStr, out int userId))
        return Results.Unauthorized();

    var movie = await db.MovieShows.FindAsync(dto.MovieShowId);
    if (movie is null)
        return Results.NotFound("Сеанс не найден.");

    if (movie.AvailableSeats < dto.NumberOfSeats)
        return Results.BadRequest("Недостаточно свободных мест.");

    movie.AvailableSeats -= dto.NumberOfSeats;

    var booking = new Booking
    {
        UserId = userId,
        MovieShowId = dto.MovieShowId,
        NumberOfSeats = dto.NumberOfSeats,
        BookingTime = DateTime.UtcNow
    };

    db.Bookings.Add(booking);
    await db.SaveChangesAsync();

    return Results.Ok(booking);
})
.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

app.Run();