using Lumina.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Data;

public class LuminaDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public LuminaDbContext(DbContextOptions<LuminaDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Flashcard> Flashcards => Set<Flashcard>();

    public DbSet<Quiz> Quizzes => Set<Quiz>();

    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public DbSet<QuizJob> QuizJobs => Set<QuizJob>();

    public DbSet<FlashcardJob> FlashcardJobs => Set<FlashcardJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Document>()
            .HasOne(d => d.User)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Document>()
            .HasIndex(d => d.UserId);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.TokenHash)
            .IsUnique();

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
            .HasForeignKey(qj => qj.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuizJob>()
            .Property(qj => qj.Status)
            .HasConversion<string>();

        modelBuilder.Entity<FlashcardJob>()
            .HasOne(fj => fj.Document)
            .WithMany()
            .HasForeignKey(fj => fj.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FlashcardJob>()
            .Property(fj => fj.Status)
            .HasConversion<string>();

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