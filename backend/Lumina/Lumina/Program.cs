using Lumina.Data;
using Lumina.Services.AI;
using Lumina.Services.Documents;
using Lumina.Services.Flashcards;
using Lumina.Services.Quizzes;
using Lumina.WebSockets;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddSingleton<IBackgroundTaskQueue>(new BackgroundTaskQueue(100));
builder.Services.AddHostedService<QuizBackgroundWorker>();

builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<IFlashcardService, FlashcardService>();

builder.Services.AddHttpClient<IAiService, OllamaService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(5);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");

app.MapControllers();
app.MapHub<QuizHub>("/ws/quiz");
app.MapHub<FlashcardHub>("/ws/flashcard");

app.Run();

