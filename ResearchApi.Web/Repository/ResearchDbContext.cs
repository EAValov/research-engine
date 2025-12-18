using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Domain; 

namespace ResearchApi.Infrastructure;

public class ResearchDbContext : DbContext
{
    private readonly int _embeddingDimensions;

    public DbSet<ResearchJob> ResearchJobs => Set<ResearchJob>();
    public DbSet<Clarification> Clarifications => Set<Clarification>();
    public DbSet<ResearchEvent> ResearchEvents => Set<ResearchEvent>();
    public DbSet<VisitedUrl> VisitedUrls => Set<VisitedUrl>();
    public DbSet<ScrapedPage> ScrapedPages => Set<ScrapedPage>();
    public DbSet<Learning> Learnings => Set<Learning>();

    public ResearchDbContext(
        DbContextOptions<ResearchDbContext> options,
        IOptions<EmbeddingConfig> embeddingOptions)
        : base(options)
    {
        _embeddingDimensions = embeddingOptions.Value.Dimension;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        ConfigureResearchJob(modelBuilder);
        ConfigureClarification(modelBuilder);
        ConfigureEvent(modelBuilder);
        ConfigureVisitedUrl(modelBuilder);
        ConfigureScrapedPage(modelBuilder);
        ConfigureLearning(modelBuilder);
    }

    private void ConfigureResearchJob(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ResearchJob>();

        entity.ToTable("research_jobs");
        entity.HasKey(j => j.Id);

        entity.Property(j => j.Query)
            .IsRequired()
            .HasMaxLength(4000);

        entity.Property(j => j.Status)
            .HasConversion<string>() // store enum as text
            .IsRequired();

        entity.Property(j => j.TargetLanguage)
            .IsRequired()
            .HasMaxLength(20);

        entity.Property(j => j.Region)
            .HasMaxLength(500);

        entity.Property(j => j.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.Property(j => j.UpdatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");
    }

    private void ConfigureClarification(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Clarification>();

        entity.ToTable("clarifications");
        entity.HasKey(c => c.Id);

        entity.Property(c => c.Question)
            .IsRequired()
            .HasMaxLength(4000);

        entity.Property(c => c.Answer)
            .IsRequired()
            .HasMaxLength(4000);

        entity.HasOne(c => c.Job)
            .WithMany(j => j.Clarifications)
            .HasForeignKey(c => c.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private void ConfigureEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ResearchEvent>();

        entity.ToTable("research_events");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Stage)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(200);

        entity.Property(e => e.Message)
            .IsRequired()
            .HasMaxLength(4000);

        entity.HasOne(e => e.Job)
            .WithMany(j => j.Events)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private void ConfigureVisitedUrl(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<VisitedUrl>();

        entity.ToTable("visited_urls");
        entity.HasKey(v => v.Id);

        entity.Property(v => v.Url)
            .IsRequired()
            .HasMaxLength(2000);

        entity.HasIndex(v => new { v.JobId, v.Url }).IsUnique();

        entity.HasOne(v => v.Job)
            .WithMany(j => j.VisitedUrls)
            .HasForeignKey(v => v.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private void ConfigureScrapedPage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ScrapedPage>();

        entity.ToTable("scraped_pages");
        entity.HasKey(p => p.Id);

        entity.Property(p => p.Url)
            .IsRequired()
            .HasMaxLength(2000);

        entity.Property(p => p.Content)
            .IsRequired();

        entity.Property(p => p.ContentHash)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(p => p.Language)
            .HasMaxLength(20);

        entity.Property(p => p.Region)
            .HasMaxLength(500);

        entity.Property(p => p.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.HasIndex(p => p.Url);
        entity.HasIndex(p => p.ContentHash);
    }

    private void ConfigureLearning(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Learning>();

        entity.ToTable("learnings");
        entity.HasKey(l => l.Id);

        entity.HasIndex(l => new { l.PageId, l.QueryHash });

        entity.Property(l => l.Text)
            .IsRequired();

        entity.Property(l => l.SourceUrl)
            .IsRequired()
            .HasMaxLength(2000);

        entity.Property(s => s.ImportanceScore)
            .HasColumnType("real");

        entity.Property(l => l.Embedding)
            .HasColumnType($"vector({_embeddingDimensions})");

        entity.Property(l => l.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.HasOne(l => l.Job)
            .WithMany(j => j.Learnings)
            .HasForeignKey(l => l.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(l => l.Page)
            .WithMany(p => p.Learnings)
            .HasForeignKey(l => l.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(i => i.Embedding)
            .HasMethod("ivfflat")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("lists", 100);
    }
}