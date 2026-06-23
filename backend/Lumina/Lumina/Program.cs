using System.Text;
using Lumina.Data;
using Lumina.Models;
using Lumina.Services.AI;
using Lumina.Services.Auth;
using Lumina.Services.Chat;
using Lumina.Services.Documents;
using Lumina.Services.Flashcards;
using Lumina.Services.Quizzes;
using Lumina.WebSockets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Load secrets from a .env file (e.g. Jwt__Key) into environment variables
// BEFORE the host reads configuration. TraversePath walks up from the working
// directory so it's found whether you run from the solution or project folder.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSignalR();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

builder.Services.AddDbContext<LuminaDbContext>(options =>
{
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection"));
});

// --- Authentication & authorization (JWT access + Identity user store) ---
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
    })
    .AddEntityFrameworkStores<LuminaDbContext>();

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>() ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");

if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or too short. Set 'Jwt__Key' (>= 32 chars) in the .env file at the solution root.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // Browsers can't set the Authorization header on a WebSocket handshake,
        // so SignalR passes the access token as a query-string parameter for /ws.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/ws"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Secure-by-default: every endpoint requires an authenticated user unless it
// opts out with [AllowAnonymous]. A new controller added without [Authorize]
// is protected automatically instead of silently anonymous.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddSingleton<IBackgroundTaskQueue>(new BackgroundTaskQueue(100));
builder.Services.AddHostedService<QuizBackgroundWorker>();

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<IFlashcardService, FlashcardService>();
builder.Services.AddScoped<IChatService, ChatService>();

builder.Services.AddHttpClient<IAiService, OllamaService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QuizHub>("/ws/quiz");
app.MapHub<FlashcardHub>("/ws/flashcard");
app.MapHub<ChatHub>("/ws/chat");

app.Run();

