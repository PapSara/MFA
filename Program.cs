using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OtpNet;
using QRCoder;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MFAAuthApp.Models; // namespace-ul unde ai User, LoginRequest, VerifyRequest

var builder = WebApplication.CreateBuilder(args);

// Adăugăm servicii
builder.Services.AddEndpointsApiExplorer();

// 🔵 Swagger configurat cu suport pentru JWT
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MFAAuthApp", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Introduceți token-ul JWT astfel: Bearer {token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 🔵 Configurare JWT
var key = Encoding.ASCII.GetBytes("super_secret_key_1234567890_super_secret_key!");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // true în producție
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

// 🔵 Configurăm baza de date SQLite
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

// 🔵 Middleware pentru autentificare și autorizare
app.UseAuthentication();
app.UseAuthorization();

// Înregistrare utilizator
app.MapPost("/register", async ([FromBody] LoginRequest request, AppDbContext db) =>
{
    if (await db.Users.AnyAsync(u => u.Username == request.Username))
        return Results.BadRequest("Utilizatorul există deja.");

    var user = new User
    {
        Username = request.Username,
        PasswordHash = request.Password // Atenție: în producție trebuie HASH!
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok("Utilizator înregistrat.");
});

// Login + generare secret + QR Code pentru MFA
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
        var otpUri = new OtpUri(OtpType.Totp, base32Secret, request.Username, "MFAAuthApp");

        var qrGenerator = new QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(otpUri.ToString(), QRCodeGenerator.ECCLevel.Q);
        var qrCode = new Base64QRCode(qrData).GetGraphic(20);

        return Results.Ok(new
        {
            Message = "Scanează codul QR în Google Authenticator.",
            Secret = base32Secret,
            QrCodeImageBase64 = qrCode
        });
    }

    return Results.Ok(new { Message = "TOTP deja activat. Introdu codul din aplicație." });
});

// Verificare cod TOTP + emitere JWT
app.MapPost("/verify", async ([FromBody] VerifyRequest request, AppDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
    if (user == null || user.TotpSecret == null)
        return Results.Unauthorized();

    var totp = new Totp(user.TotpSecret);
    var isValid = totp.VerifyTotp(request.Code, out _, new VerificationWindow(2, 2));

    if (!isValid)
        return Results.Unauthorized();

    // Generare token JWT
    var tokenHandler = new JwtSecurityTokenHandler();
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, user.Username)
        }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    var jwt = tokenHandler.WriteToken(token);

    return Results.Ok(new { Token = jwt });
});

// Endpoint protejat - necesită JWT
app.MapGet("/protected", [Microsoft.AspNetCore.Authorization.Authorize]() =>
    "Acces permis doar cu JWT!");

app.Run();
