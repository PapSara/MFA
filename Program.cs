using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using QRCoder;
using System.Text;
using MFAAuthApp.Models; // adăugat corect namespace-ul tău


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configurează baza de date SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=users.db"));

var app = builder.Build();

// Creează DB la pornire (dacă nu există)
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

// Înregistrare utilizator (fără hash pt simplitate)
app.MapPost("/register", async ([FromBody] LoginRequest request, AppDbContext db) =>
{
    if (await db.Users.AnyAsync(u => u.Username == request.Username))
        return Results.BadRequest("Utilizatorul există deja.");

    var user = new User
    {
        Username = request.Username,
        PasswordHash = request.Password // Într-o aplicație reală: hash!
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok("Utilizator înregistrat.");
});

// Login + generare secret + QR Code
app.MapPost("/login", async ([FromBody] LoginRequest request, AppDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
    if (user == null || user.PasswordHash != request.Password)
        return Results.Unauthorized();

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
            Message = "Scanează codul în Google Authenticator.",
            Secret = base32Secret,
            QrCodeImageBase64 = qrCode
        });
    }

    return Results.Ok(new { Message = "TOTP deja activat. Introdu codul." });
});

// Verificare TOTP
app.MapPost("/verify", async ([FromBody] VerifyRequest request, AppDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
    if (user == null || user.TotpSecret == null)
        return Results.Unauthorized();

    var totp = new Totp(user.TotpSecret);
    var isValid = totp.VerifyTotp(request.Code, out _, new VerificationWindow(2, 2));

    return isValid
        ? Results.Ok("Autentificare MFA reușită!")
        : Results.Unauthorized();
});

app.Run();

