using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OtpNet;
using QRCoder;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MFAAuthApp.Models;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

var key = Encoding.ASCII.GetBytes("super_secret_key_1234567890_super_secret_key!");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

builder.Services.AddAuthorization();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=users.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    return Convert.ToBase64String(hashedBytes);
}

app.MapPost("/register", async ([FromBody] LoginRequest request, AppDbContext db) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest("Username și parola sunt obligatorii.");

        if (await db.Users.AnyAsync(u => u.Username == request.Username))
            return Results.BadRequest("Utilizatorul există deja.");

        var user = new User
        {
            Username = request.Username,
            PasswordHash = HashPassword(request.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Results.Ok(new { Message = "Utilizator înregistrat cu succes." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Eroare la înregistrare: {ex.Message}");
    }
});

app.MapPost("/login", async ([FromBody] LoginRequest request, AppDbContext db) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest("Username și parola sunt obligatorii.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user == null || user.PasswordHash != HashPassword(request.Password))
            return Results.Json(new { Message = "Date de autentificare invalide." }, statusCode: 401);

        // Dacă utilizatorul nu are TOTP secret, îl generăm
        if (user.TotpSecret == null)
        {
            var secret = KeyGeneration.GenerateRandomKey(20);
            user.TotpSecret = secret;
            await db.SaveChangesAsync();

            var base32Secret = Base32Encoding.ToString(secret);
            var otpUri = new OtpUri(OtpType.Totp, base32Secret, request.Username, "MFAApp");

            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(otpUri.ToString(), QRCodeGenerator.ECCLevel.Q);
            var qrCode = new Base64QRCode(qrData).GetGraphic(20);

            return Results.Ok(new
            {
                Message = "Scanează codul QR în Google Authenticator.",
                Secret = base32Secret,
                QrCodeImageBase64 = qrCode,
                RequiresMFA = true
            });
        }

        return Results.Ok(new { 
            Message = "TOTP deja activat. Introdu codul MFA.",
            RequiresMFA = true
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Eroare la login: {ex.Message}");
    }
});

app.MapPost("/verify", async ([FromBody] VerifyRequest request, AppDbContext db) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest("Username și codul MFA sunt obligatorii.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user == null || user.TotpSecret == null)
            return Results.Json(new { Message = "Utilizator sau secret TOTP invalid." }, statusCode: 401);

        var totp = new Totp(user.TotpSecret);
        var isValid = totp.VerifyTotp(request.Code, out _, new VerificationWindow(2, 2));

        if (!isValid)
            return Results.Json(new { Message = "Cod MFA invalid." }, statusCode: 401);

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwt = tokenHandler.WriteToken(token);

        return Results.Ok(new { 
            Token = jwt,
            Message = "Autentificare reușită!",
            Username = user.Username
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Eroare la verificare: {ex.Message}");
    }
});

app.MapGet("/protected", [Microsoft.AspNetCore.Authorization.Authorize]() => 
{
    return Results.Ok(new { Message = "Acces permis cu JWT!" });
});

app.MapPost("/reset-totp", async ([FromBody] LoginRequest request, AppDbContext db) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest("Username și parola sunt obligatorii.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user == null || user.PasswordHash != HashPassword(request.Password))
            return Results.Json(new { Message = "Date de autentificare invalide." }, statusCode: 401);

        // resetează TOTP secret-ul
        user.TotpSecret = null;
        await db.SaveChangesAsync();

        return Results.Ok(new { Message = "TOTP resetat cu succes. Acum te poți loga din nou pentru a genera un QR code nou." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Eroare la resetare TOTP: {ex.Message}");
    }
});

app.MapGet("/health", () => Results.Ok(new { Status = "Server is running", Time = DateTime.Now }));

app.MapControllers();

app.Run();
