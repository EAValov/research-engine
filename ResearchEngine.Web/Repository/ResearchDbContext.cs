using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResearchEngine.Configuration;
using ResearchEngine.Domain; 

namespace ResearchEngine.Infrastructure;

public sealed class ResearchDbContext : DbContext
{
    private readonly int _embeddingDimensions;

    public DbSet<ResearchJob> ResearchJobs => Set<ResearchJob>();
    public DbSet<Clarification> Clarifications => Set<Clarification>();
    public DbSet<ResearchEvent> ResearchEvents => Set<ResearchEvent>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Learning> Learnings => Set<Learning>();
    public DbSet<LearningEmbedding> LearningEmbeddings => Set<LearningEmbedding>();
    public DbSet<Synthesis> Syntheses => Set<Synthesis>();
    public DbSet<SynthesisSection> SynthesisSections => Set<SynthesisSection>();
    public DbSet<SynthesisSourceOverride> SynthesisSourceOverrides => Set<SynthesisSourceOverride>();
    public DbSet<SynthesisLearningOverride> SynthesisLearningOverrides => Set<SynthesisLearningOverride>();
    public DbSet<LearningGroup> LearningGroups => Set<LearningGroup>();
    public DbSet<LearningGroupEmbedding> LearningGroupEmbeddings => Set<LearningGroupEmbedding>();

    public ResearchDbContext(
        DbContextOptions<ResearchDbContext> options,
        IOptions<EmbeddingConfig> embeddingOptions)
        : base(options)
    {
        _embeddingDimensions = embeddingOptions.Value.Dimension;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        ConfigureResearchJob(modelBuilder);
        ConfigureClarification(modelBuilder);
        ConfigureEvent(modelBuilder);
        ConfigureSource(modelBuilder);
        ConfigureLearning(modelBuilder);
        ConfigureLearningEmbedding(modelBuilder);
        ConfigureSynthesis(modelBuilder);
        ConfigureSynthesisSection(modelBuilder);
        ConfigureSynthesisSourceOverride(modelBuilder);
        ConfigureSynthesisLearningOverride(modelBuilder);
        ConfigureLearningGroup(modelBuilder);
        ConfigureLearningGroupEmbedding(modelBuilder);
    }

    private static void ConfigureResearchJob(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ResearchJob>();

        entity.ToTable("research_jobs");
        entity.HasKey(j => j.Id);

        entity.Property(j => j.Query)
            .IsRequired()
            .HasMaxLength(4000);

        entity.Property(j => j.Status)
            .HasConversion<string>()
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

        entity.HasMany(j => j.Sources)
            .WithOne(s => s.Job)
            .HasForeignKey(s => s.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(j => j.Syntheses)
            .WithOne(sy => sy.Job)
            .HasForeignKey(sy => sy.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureClarification(ModelBuilder modelBuilder)
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

        entity.Property(c => c.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

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
        
        entity.Property(e => e.SynthesisId);

        entity.Property(e => e.Message)
            .IsRequired()
            .HasMaxLength(4000);

        entity.HasOne(e => e.Job)
            .WithMany(j => j.Events)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => new { e.JobId, e.SynthesisId });
    }

    private static void ConfigureSource(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Source>();

        entity.ToTable("sources");
        entity.HasKey(s => s.Id);

        entity.Property(s => s.Reference)
            .IsRequired()
            .HasMaxLength(4000);

        entity.Property(s => s.ContentHash)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(s => s.Title)
            .HasMaxLength(1000);

        entity.Property(s => s.Content)
            .IsRequired();

        entity.Property(s => s.Language)
            .HasMaxLength(20);

        entity.Property(s => s.Region)
            .HasMaxLength(500);

        entity.Property(s => s.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        // Dedupe: the same ref should not be inserted twice for the same job.
        entity.HasIndex(s => new { s.JobId, s.Reference }).IsUnique();

        // Useful for caching/dedupe by content.
        entity.HasIndex(s => s.ContentHash);

        entity.HasOne(s => s.Job)
            .WithMany(j => j.Sources)
            .HasForeignKey(s => s.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(s => s.Learnings)
            .WithOne(l => l.Source)
            .HasForeignKey(l => l.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLearning(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Learning>();

        entity.ToTable("learnings");
        entity.HasKey(l => l.Id);

        entity.Property(l => l.QueryHash)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(l => l.Text)
            .IsRequired();

        entity.Property(l => l.IsUserProvided)
            .IsRequired();

        entity.Property(l => l.ImportanceScore)
            .HasColumnType("real")
            .IsRequired();

        entity.Property(l => l.EvidenceText)
            .IsRequired();

        entity.Property(l => l.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.HasOne(l => l.Job)
            .WithMany()
            .HasForeignKey(l => l.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(l => l.Source)
            .WithMany(s => s.Learnings)
            .HasForeignKey(l => l.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(l => l.Group)
            .WithMany(g => g.Learnings)
            .HasForeignKey(l => l.LearningGroupId)
            .OnDelete(DeleteBehavior.Restrict); // avoid accidental mass deletions

        entity.HasIndex(l => new { l.JobId, l.SourceId });
        entity.HasIndex(l => new { l.SourceId, l.QueryHash });
        entity.HasIndex(l => new { l.JobId, l.LearningGroupId });
    }

    private void ConfigureLearningEmbedding(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LearningEmbedding>();

        entity.ToTable("learning_embeddings");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Vector)
            .IsRequired()
            .HasColumnType($"vector({_embeddingDimensions})");

        entity.Property(e => e.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.HasOne(e => e.Learning)
            .WithOne(l => l.Embedding)
            .HasForeignKey<LearningEmbedding>(e => e.LearningId)
            .OnDelete(DeleteBehavior.Cascade);

        // ivfflat index for vector search
        entity.HasIndex(i => i.Vector)
            .HasMethod("ivfflat")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("lists", 100);

        entity.HasIndex(e => e.LearningId).IsUnique();
    }

    private static void ConfigureSynthesis(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Synthesis>();

        entity.ToTable("syntheses");
        entity.HasKey(s => s.Id);

        entity.Property(s => s.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(50);

        entity.Property(s => s.Outline);
        entity.Property(s => s.Instructions);

        entity.Property(s => s.ErrorMessage)
            .HasMaxLength(4000);

        entity.Property(s => s.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.HasOne(s => s.Job)
            .WithMany(j => j.Syntheses)
            .HasForeignKey(s => s.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        // self-referencing lineage
        entity.HasOne(s => s.ParentSynthesis)
            .WithMany(p => p.Children)
            .HasForeignKey(s => s.ParentSynthesisId)
            .OnDelete(DeleteBehavior.SetNull);

        // NEW: sections relationship
        entity.HasMany(s => s.Sections)
            .WithOne(ss => ss.Synthesis)
            .HasForeignKey(ss => ss.SynthesisId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(s => new { s.JobId, s.CreatedAt });
        entity.HasIndex(s => s.Status);
    }

    private static void ConfigureSynthesisSection(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SynthesisSection>();

        entity.ToTable("synthesis_sections");
        entity.HasKey(s => s.Id);

        entity.Property(s => s.SectionKey)
            .IsRequired();

        entity.Property(s => s.Index)
            .IsRequired();

        entity.Property(s => s.Title)
            .IsRequired()
            .HasMaxLength(500);

        entity.Property(s => s.Description)
            .IsRequired()
            .HasMaxLength(4000);

        entity.Property(s => s.IsConclusion)
            .IsRequired();

        entity.Property(s => s.ContentMarkdown)
            .IsRequired();

        entity.Property(s => s.Summary)
            .HasMaxLength(20_000);

        entity.Property(s => s.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.HasOne(s => s.Synthesis)
            .WithMany(p => p.Sections)
            .HasForeignKey(s => s.SynthesisId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ordering and fast lookup
        entity.HasIndex(s => new { s.SynthesisId, s.Index }).IsUnique();

        // Stable identity within a synthesis (helps avoid duplicates)
        entity.HasIndex(s => new { s.SynthesisId, s.SectionKey }).IsUnique();
    }

    private static void ConfigureSynthesisSourceOverride(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SynthesisSourceOverride>();

        entity.ToTable("synthesis_source_overrides");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Excluded);
        entity.Property(x => x.Pinned);

        entity.Property(x => x.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        // One row per (Synthesis, Source)
        entity.HasIndex(x => new { x.SynthesisId, x.SourceId })
            .IsUnique();

        entity.HasOne(x => x.Synthesis)
            .WithMany() // no nav required
            .HasForeignKey(x => x.SynthesisId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(x => x.Source)
            .WithMany() // no nav required
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSynthesisLearningOverride(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SynthesisLearningOverride>();

        entity.ToTable("synthesis_learning_overrides");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.ScoreOverride)
            .HasColumnType("real");

        entity.Property(x => x.Excluded);
        entity.Property(x => x.Pinned);

        entity.Property(x => x.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        // One row per (Synthesis, Learning)
        entity.HasIndex(x => new { x.SynthesisId, x.LearningId })
            .IsUnique();

        entity.HasOne(x => x.Synthesis)
            .WithMany() // no nav required
            .HasForeignKey(x => x.SynthesisId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(x => x.Learning)
            .WithMany() // no nav required
            .HasForeignKey(x => x.LearningId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLearningGroup(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LearningGroup>();

        entity.ToTable("learning_groups");
        entity.HasKey(g => g.Id);

        entity.Property(g => g.CanonicalText)
            .IsRequired();

        entity.Property(g => g.CanonicalImportanceScore)
            .HasColumnType("real")
            .IsRequired();

        entity.Property(g => g.MemberCount)
            .IsRequired();

        entity.Property(g => g.DistinctSourceCount)
            .IsRequired();

        entity.Property(g => g.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.Property(g => g.UpdatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.HasOne(g => g.Job)
            .WithMany()
            .HasForeignKey(g => g.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(g => g.Learnings)
            .WithOne(l => l.Group)
            .HasForeignKey(l => l.LearningGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(g => new { g.JobId, g.UpdatedAt });
    }
    
    private void ConfigureLearningGroupEmbedding(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LearningGroupEmbedding>();

        entity.ToTable("learning_group_embeddings");
        entity.HasKey(e => e.Id);

        entity.Property(e => e.Vector)
            .IsRequired()
            .HasColumnType($"vector({_embeddingDimensions})");

        entity.Property(e => e.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        entity.HasOne(e => e.Group)
            .WithOne(g => g.Embedding)
            .HasForeignKey<LearningGroupEmbedding>(e => e.LearningGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(i => i.Vector)
            .HasMethod("ivfflat")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("lists", 100);

        entity.HasIndex(e => e.LearningGroupId).IsUnique();
    }
}