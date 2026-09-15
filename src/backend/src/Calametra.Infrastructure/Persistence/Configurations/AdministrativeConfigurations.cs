using Calametra.Application.Abstractions.Data;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Calametra.Infrastructure.Persistence.Configurations;

internal sealed class PsgcRegisterEditionConfiguration : IEntityTypeConfiguration<PsgcRegisterEdition>
{
    public void Configure(EntityTypeBuilder<PsgcRegisterEdition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("psgc_register_editions");

        builder.HasKey(edition => edition.Id);

        builder.Property(edition => edition.Label).HasMaxLength(200).IsRequired();

        // Stored as its name, following the platform's rule for enums that a reader may see: PsaDirect
        // and Mirror cannot be mistaken for each other, where 1 and 2 in a column say nothing.
        builder.Property(edition => edition.Provenance)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(edition => edition.AccessRoute).HasMaxLength(500).IsRequired();
        builder.Property(edition => edition.Notes).HasMaxLength(4000);

        // The acquisition chain. Length-bounded rather than free text so a mis-set option fails at the
        // database instead of storing a stack trace as a provenance note.
        builder.Property(edition => edition.OriginalFileName).HasMaxLength(300);
        builder.Property(edition => edition.AcquisitionNote).HasMaxLength(2000);

        // Exactly 64 lowercase hex characters. Fixed length because a truncated digest is worse than no
        // digest: it looks verifiable and is not.
        builder.Property(edition => edition.FileSha256).HasMaxLength(64).IsFixedLength();

        builder.HasIndex(edition => edition.FileSha256);

        // Which edition the platform is reconciled to, asked on every readiness report.
        builder.HasIndex(edition => edition.SupersededAt);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_psgc_register_editions_hash_is_hex",
            "file_sha256 IS NULL OR file_sha256 ~ '^[0-9a-f]{64}$'"));

        // One row per label and access route: re-reading the same publication reconciles rather than
        // accumulating, so "which edition are we on" has one answer.
        builder.HasIndex(edition => new { edition.Label, edition.AccessRoute }).IsUnique();
    }
}

internal sealed class LguConfiguration : IEntityTypeConfiguration<Lgu>
{
    public void Configure(EntityTypeBuilder<Lgu> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("lgus");

        builder.HasKey(lgu => lgu.Id);

        builder.Property(lgu => lgu.CanonicalPsgcCode).HasMaxLength(10).IsRequired();
        builder.Property(lgu => lgu.Name).HasMaxLength(200).IsRequired();
        builder.Property(lgu => lgu.ParentCanonicalPsgcCode).HasMaxLength(10);
        builder.Property(lgu => lgu.RegisterStatedHistoricalCode).HasMaxLength(9);

        builder.Property(lgu => lgu.Level)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // The identity. Unique because a register that offered the same canonical code twice would be
        // a register this platform cannot use, and finding that out at import is far better than
        // discovering it through a duplicated figure later.
        builder.HasIndex(lgu => lgu.CanonicalPsgcCode).IsUnique();

        builder.HasIndex(lgu => lgu.ParentCanonicalPsgcCode);

        // The length rule is repeated in the database because it is the invariant that keeps canonical
        // and historical editions from being confused, and a bad import is exactly the moment the
        // domain guard is bypassed.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_lgus_canonical_code_is_ten_digits",
            "char_length(canonical_psgc_code) = 10 AND canonical_psgc_code ~ '^[0-9]+$'"));
    }
}

internal sealed class LguCodeLinkConfiguration : IEntityTypeConfiguration<LguCodeLink>
{
    public void Configure(EntityTypeBuilder<LguCodeLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("lgu_code_links");

        builder.HasKey(link => link.Id);

        builder.Property(link => link.HistoricalPsgcCode).HasMaxLength(9).IsRequired();
        builder.Property(link => link.ProposedBy).HasMaxLength(120).IsRequired();
        builder.Property(link => link.ReviewedBy).HasMaxLength(120);
        builder.Property(link => link.Reason).HasMaxLength(2000);
        builder.Property(link => link.ProposalBasis).HasMaxLength(1000);

        builder.Property(link => link.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(link => link.Evidence)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(link => link.Status);
        builder.HasIndex(link => link.HistoricalPsgcCode);
        builder.HasIndex(link => link.ProposedAgainstEditionId);

        // One proposal per (unit, historical code): a matcher re-run reconciles rather than duplicating,
        // so the rejection rate stays a rate rather than an artefact of how many times it ran.
        builder.HasIndex(link => new { link.LguId, link.HistoricalPsgcCode }).IsUnique();

        // At most one CONFIRMED pairing per unit, and at most one per gazetteer row. Partial unique
        // indexes rather than plain ones, because competing proposals are legitimate — that is what a
        // review queue is for — while two confirmed answers to the same question are not.
        builder.HasIndex(link => link.LguId)
            .IsUnique()
            .HasFilter("status = 'Confirmed'")
            .HasDatabaseName("ux_lgu_code_links_confirmed_per_lgu");

        builder.HasIndex(link => link.PlaceId)
            .IsUnique()
            .HasFilter("status = 'Confirmed' AND place_id IS NOT NULL")
            .HasDatabaseName("ux_lgu_code_links_confirmed_per_place");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_lgu_code_links_historical_code_is_nine_digits",
                "char_length(historical_psgc_code) = 9 AND historical_psgc_code ~ '^[0-9]+$'");

            // ADR-005 D4: constraints enforce COMPLETENESS, which is the job they can actually do. A
            // half-filled confirmed row is the failure that would make the whole gate decorative — it
            // would pass every read boundary while carrying no evidence at all.
            table.HasCheckConstraint(
                "ck_lgu_code_links_confirmed_is_complete",
                """
                status <> 'Confirmed' OR (
                    evidence <> 'Unknown'
                    AND reviewed_by IS NOT NULL
                    AND reviewed_at IS NOT NULL
                    AND confirmed_against_edition_id IS NOT NULL
                    AND (evidence <> 'ManualReview' OR reason IS NOT NULL)
                    AND (evidence <> 'DigitReslice' OR names_agree)
                )
                """);

            // A rejection without a reason is a number nobody can explain.
            table.HasCheckConstraint(
                "ck_lgu_code_links_rejected_has_reason",
                "status <> 'Rejected' OR (reviewed_by IS NOT NULL AND reason IS NOT NULL)");
        });
    }
}

/// <summary>
/// The confirmed-only read boundary, mapped to a view rather than to the base table.
/// </summary>
/// <remarks>
/// <c>ToView</c> is not cosmetic: EF will not generate inserts or updates for a view-mapped type, so the
/// analytics surface cannot write the crosswalk even by mistake, and cannot read an unreviewed row at
/// all. The view is created by migration; the base table's permissions are tightened separately, because
/// whether separate roles exist is a property of the deployment rather than of the schema.
/// </remarks>
internal sealed class ConfirmedLguLinkConfiguration : IEntityTypeConfiguration<ConfirmedLguLink>
{
    /// <summary>The view name, shared with the migration and the readiness report.</summary>
    public const string ViewName = "lgu_code_links_confirmed";

    public void Configure(EntityTypeBuilder<ConfirmedLguLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToView(ViewName);

        builder.HasKey(link => link.Id);

        builder.Property(link => link.CanonicalPsgcCode).HasColumnName("canonical_psgc_code");
        builder.Property(link => link.LguName).HasColumnName("lgu_name");
        builder.Property(link => link.LguLevel).HasColumnName("lgu_level");
        builder.Property(link => link.HistoricalPsgcCode).HasColumnName("historical_psgc_code");
        builder.Property(link => link.PlaceId).HasColumnName("place_id");
        builder.Property(link => link.Evidence).HasColumnName("evidence");
        builder.Property(link => link.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(link => link.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(link => link.Reason).HasColumnName("reason");
        builder.Property(link => link.ConfirmedAgainstEditionId)
            .HasColumnName("confirmed_against_edition_id");
    }
}
