using Lumina.Models;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Data;

public class LuminaDbContext : DbContext
{
    public LuminaDbContext(DbContextOptions<LuminaDbContext> options)
        : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Flashcard> Flashcards => Set<Flashcard>();

    public DbSet<Quiz> Quizzes => Set<Quiz>();

    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public DbSet<QuizJob> QuizJobs => Set<QuizJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Document>()
            .HasMany(d => d.Flashcards)
            .WithOne(f => f.Document)
            .HasForeignKey(f => f.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Document>()
            .HasMany(d => d.Quizzes)
            .WithOne(q => q.Document)
            .HasForeignKey(q => q.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuizJob>()
            .HasOne(qj => qj.Document)
            .WithMany()
            .HasForeignKey(qj => qj.DocumentId);

        modelBuilder.Entity<Document>()
            .HasMany(d => d.ChatMessages)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Quiz>()
            .HasMany(q => q.Questions)
            .WithOne(qq => qq.Quiz)
            .HasForeignKey(qq => qq.QuizId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<Flashcard>()
            .HasIndex(f => f.DocumentId);

        modelBuilder.Entity<Quiz>()
            .HasIndex(q => q.DocumentId);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(c => c.DocumentId);
    }
}