using FixPal.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FixPal.Data
{
    public class ApplicationDbContext
        : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Area> Areas { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<MaintenanceRequest> MaintenanceRequests { get; set; }
        public DbSet<RequestQuote> RequestQuotes { get; set; }
        public DbSet<QuoteRevision> QuoteRevisions { get; set; }
        public DbSet<QuoteDecision> QuoteDecisions { get; set; }
        public DbSet<RequestMessage> RequestMessages { get; set; }
        public DbSet<RequestEvidence> RequestEvidence { get; set; }
        public DbSet<ProviderReview> ProviderReviews { get; set; }
        public DbSet<ServiceCategory> ServiceCategories { get; set; }
        public DbSet<ProviderProfile> ProviderProfiles { get; set; }
        public DbSet<ProviderCalendar> ProviderCalendars { get; set; }
        public DbSet<ProviderWorkingPeriod> ProviderWorkingPeriods { get; set; }
        public DbSet<ProviderBlackout> ProviderBlackouts { get; set; }
        public DbSet<Appointment> Appointments { get; set; }

        private void ProtectQuoteHistory()
        {
            ChangeTracker.DetectChanges();
            if (ChangeTracker.Entries().Any(e => (e.Entity is QuoteRevision or QuoteDecision)
                && e.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException("Quote history is immutable. Append a new revision or decision instead.");
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ProtectQuoteHistory();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ProtectQuoteHistory();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            var calendar = builder.Entity<ProviderCalendar>();
            calendar.HasKey(c => c.ProviderProfileId);
            calendar.HasOne(c => c.ProviderProfile).WithOne()
                .HasForeignKey<ProviderCalendar>(c => c.ProviderProfileId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            calendar.Property(c => c.TimeZoneId).IsRequired().HasMaxLength(100);
            calendar.Property(c => c.UpdatedAtUtc).HasColumnType("datetimeoffset(7)");
            calendar.Property(c => c.RowVersion).IsRowVersion();
            calendar.ToTable("ProviderCalendars", t =>
            {
                t.HasCheckConstraint("CK_ProviderCalendars_TimeZone", "LEN(LTRIM(RTRIM([TimeZoneId]))) > 0");
                t.HasCheckConstraint("CK_ProviderCalendars_UpdatedAtUtc", "DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
            });

            var workingPeriod = builder.Entity<ProviderWorkingPeriod>();
            workingPeriod.HasKey(p => p.Id);
            workingPeriod.HasOne(p => p.ProviderCalendar).WithMany()
                .HasForeignKey(p => p.ProviderProfileId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            workingPeriod.Property(p => p.DayOfWeek).HasConversion<int>();
            workingPeriod.Property(p => p.StartLocal).HasColumnType("time(0)");
            workingPeriod.Property(p => p.EndLocal).HasColumnType("time(0)");
            workingPeriod.HasIndex(p => new { p.ProviderProfileId, p.DayOfWeek, p.StartLocal, p.EndLocal })
                .IsUnique().HasDatabaseName("UX_ProviderWorkingPeriods_Interval");
            workingPeriod.ToTable("ProviderWorkingPeriods", t =>
            {
                t.HasCheckConstraint("CK_ProviderWorkingPeriods_Day", "[DayOfWeek] BETWEEN 0 AND 6");
                t.HasCheckConstraint("CK_ProviderWorkingPeriods_Interval", "[StartLocal] < [EndLocal]");
            });

            var blackout = builder.Entity<ProviderBlackout>();
            blackout.HasKey(b => b.Id);
            blackout.HasOne(b => b.ProviderCalendar).WithMany()
                .HasForeignKey(b => b.ProviderProfileId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            blackout.HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(b => b.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            blackout.HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(b => b.RemovedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            blackout.Property(b => b.StartUtc).HasColumnType("datetimeoffset(7)");
            blackout.Property(b => b.EndUtc).HasColumnType("datetimeoffset(7)");
            blackout.Property(b => b.CreatedAtUtc).HasColumnType("datetimeoffset(7)");
            blackout.Property(b => b.RemovedAtUtc).HasColumnType("datetimeoffset(7)").IsRequired(false);
            blackout.HasIndex(b => new { b.ProviderProfileId, b.StartUtc })
                .HasDatabaseName("IX_ProviderBlackouts_ActiveInterval")
                .IncludeProperties(b => b.EndUtc).HasFilter("[RemovedAtUtc] IS NULL");
            blackout.ToTable("ProviderBlackouts", t =>
            {
                t.HasCheckConstraint("CK_ProviderBlackouts_Interval", "[StartUtc] < [EndUtc]");
                t.HasCheckConstraint("CK_ProviderBlackouts_Removal", "([RemovedAtUtc] IS NULL AND [RemovedByUserId] IS NULL) OR ([RemovedAtUtc] IS NOT NULL AND [RemovedByUserId] IS NOT NULL)");
                t.HasCheckConstraint("CK_ProviderBlackouts_Utc", "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND ([RemovedAtUtc] IS NULL OR DATEPART(TZOFFSET, [RemovedAtUtc]) = 0)");
            });

            var appointment = builder.Entity<Appointment>();
            appointment.HasKey(a => a.Id);
            appointment.HasOne(a => a.MaintenanceRequest).WithMany()
                .HasForeignKey(a => a.MaintenanceRequestId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne(a => a.ProviderCalendar).WithMany()
                .HasForeignKey(a => a.ProviderProfileId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(a => a.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(a => a.ClosedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            appointment.HasOne(a => a.ReplacesAppointment).WithMany()
                .HasForeignKey(a => a.ReplacesAppointmentId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            appointment.Property(a => a.StartUtc).HasColumnType("datetimeoffset(7)");
            appointment.Property(a => a.EndUtc).HasColumnType("datetimeoffset(7)");
            appointment.Property(a => a.CreatedAtUtc).HasColumnType("datetimeoffset(7)");
            appointment.Property(a => a.ClosedAtUtc).HasColumnType("datetimeoffset(7)").IsRequired(false);
            appointment.Property(a => a.TimeZoneId).IsRequired().HasMaxLength(100);
            appointment.Property(a => a.Status).HasConversion<int>();
            appointment.Property(a => a.RowVersion).IsRowVersion();
            appointment.HasIndex(a => a.MaintenanceRequestId).IsUnique()
                .HasDatabaseName("UX_Appointments_ActiveRequest").HasFilter("[Status] IN (1, 2)");
            appointment.HasIndex(a => a.ReplacesAppointmentId).IsUnique()
                .HasDatabaseName("UX_Appointments_Replacement").HasFilter("[ReplacesAppointmentId] IS NOT NULL");
            appointment.HasIndex(a => new { a.ProviderProfileId, a.StartUtc })
                .HasDatabaseName("IX_Appointments_ProviderInterval")
                .IncludeProperties(a => new { a.EndUtc, a.Status, a.MaintenanceRequestId });
            appointment.HasIndex(a => new { a.MaintenanceRequestId, a.CreatedAtUtc, a.Id })
                .HasDatabaseName("IX_Appointments_RequestHistory");
            appointment.ToTable("Appointments", t =>
            {
                t.HasCheckConstraint("CK_Appointments_Interval", "[StartUtc] < [EndUtc]");
                t.HasCheckConstraint("CK_Appointments_Status", "[Status] IN (1, 2, 3, 4, 5)");
                t.HasCheckConstraint("CK_Appointments_Closure", "([Status] IN (1, 2) AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL) OR ([Status] IN (3, 4, 5) AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");
                t.HasCheckConstraint("CK_Appointments_Utc", "DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND ([ClosedAtUtc] IS NULL OR DATEPART(TZOFFSET, [ClosedAtUtc]) = 0)");
                t.HasCheckConstraint("CK_Appointments_TimeZone", "LEN(LTRIM(RTRIM([TimeZoneId]))) > 0");
                t.HasCheckConstraint("CK_Appointments_Replacement", "[ReplacesAppointmentId] IS NULL OR [ReplacesAppointmentId] <> [Id]");
            });

            var review = builder.Entity<ProviderReview>();
            review.HasIndex(r => r.MaintenanceRequestId).IsUnique();
            review.HasIndex(r => new { r.ProviderProfileId, r.CreatedAtUtc, r.Id });
            review.HasOne(r => r.MaintenanceRequest).WithMany().HasForeignKey(r => r.MaintenanceRequestId).OnDelete(DeleteBehavior.Restrict);
            review.HasOne(r => r.ProviderProfile).WithMany(p => p.Reviews).HasForeignKey(r => r.ProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            review.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
            review.ToTable(t => t.HasCheckConstraint("CK_ProviderReviews_Rating", "[Rating] BETWEEN 1 AND 5"));
            var evidence = builder.Entity<RequestEvidence>();
            evidence.HasOne(e => e.MaintenanceRequest).WithMany().HasForeignKey(e => e.MaintenanceRequestId).OnDelete(DeleteBehavior.Restrict);
            evidence.HasOne(e => e.UploadedBy).WithMany().HasForeignKey(e => e.UploadedById).OnDelete(DeleteBehavior.Restrict);
            evidence.HasIndex(e => new { e.MaintenanceRequestId, e.CreatedAtUtc, e.Id });
            var message = builder.Entity<RequestMessage>();
            message.HasOne(m => m.MaintenanceRequest).WithMany().HasForeignKey(m => m.MaintenanceRequestId).OnDelete(DeleteBehavior.Restrict);
            message.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);
            message.HasIndex(m => new { m.MaintenanceRequestId, m.CreatedAtUtc, m.Id });
            var quote = builder.Entity<RequestQuote>();
            quote.Property(q => q.Id).UseIdentityColumn();
            quote.Property(q => q.RowVersion).IsRowVersion();
            var revision = builder.Entity<QuoteRevision>();
            revision.HasKey(r => new { r.RequestQuoteId, r.Number });
            revision.HasOne(r => r.RequestQuote).WithMany().HasForeignKey(r => r.RequestQuoteId).OnDelete(DeleteBehavior.Restrict);
            revision.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ProviderAuthorId).OnDelete(DeleteBehavior.Restrict);
            revision.Property(r => r.MinimumPrice).HasPrecision(18, 2);
            revision.Property(r => r.MaximumPrice).HasPrecision(18, 2);
            revision.ToTable(t => t.HasCheckConstraint("CK_QuoteRevisions_Terms", "[Number] > 0 AND [MinimumPrice] > 0 AND [MaximumPrice] >= [MinimumPrice] AND [MaximumPrice] <= 1000000"));
            quote.HasOne(q => q.CurrentRevision).WithMany().HasForeignKey(q => new { q.Id, q.CurrentRevisionNumber }).OnDelete(DeleteBehavior.Restrict);
            quote.HasOne(q => q.AcceptedRevision).WithMany().HasForeignKey(q => new { q.Id, q.AcceptedRevisionNumber }).OnDelete(DeleteBehavior.Restrict);
            quote.ToTable(t => t.HasCheckConstraint("CK_RequestQuotes_Agreement", "([State] = 0 AND [CurrentRevisionNumber] IS NULL AND [AcceptedRevisionNumber] IS NULL) OR ([State] IN (1,3,4) AND [CurrentRevisionNumber] IS NOT NULL AND [AcceptedRevisionNumber] IS NULL) OR ([State] = 2 AND [AcceptedRevisionNumber] IS NOT NULL AND [CurrentRevisionNumber] = [AcceptedRevisionNumber] AND [AcceptedAtUtc] IS NOT NULL)"));
            var decision = builder.Entity<QuoteDecision>();
            decision.HasKey(d => new { d.RequestQuoteId, d.RevisionNumber });
            decision.HasOne(d => d.Revision).WithMany().HasForeignKey(d => new { d.RequestQuoteId, d.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
            decision.HasOne<ApplicationUser>().WithMany().HasForeignKey(d => d.CustomerAuthorId).OnDelete(DeleteBehavior.Restrict);
            decision.HasIndex(d => d.RequestQuoteId).IsUnique().HasFilter("[State] = 2");
            decision.ToTable(t => t.HasCheckConstraint("CK_QuoteDecisions_State", "[State] IN (2,3,4)"));
            quote.HasIndex(q => q.MaintenanceRequestId).IsUnique();
            quote.HasOne(q => q.MaintenanceRequest).WithMany().HasForeignKey(q => q.MaintenanceRequestId).OnDelete(DeleteBehavior.Restrict);
            quote.HasOne(q => q.ProviderProfile).WithMany().HasForeignKey(q => q.ProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            quote.Property(q => q.MinimumPrice).HasPrecision(18, 2);
            quote.Property(q => q.MaximumPrice).HasPrecision(18, 2);
            quote.Property(q => q.FinalPrice).HasPrecision(18, 2);
            quote.ToTable(t => t.HasCheckConstraint("CK_RequestQuotes_Prices", "[MinimumPrice] > 0 AND [MaximumPrice] >= [MinimumPrice] AND [MaximumPrice] <= 1000000"));
            builder.Entity<City>().HasIndex(c => c.Name).IsUnique();
            builder.Entity<Area>().HasIndex(a => new { a.CityId, a.Name }).IsUnique();
            builder.Entity<Area>().HasOne(a => a.City).WithMany(c => c.Areas)
                .HasForeignKey(a => a.CityId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ProviderProfile>().Property(p => p.ApprovalStatus).IsConcurrencyToken();
            builder.Entity<ProviderProfile>().HasIndex(p => new { p.ApprovalStatus, p.ServiceCategoryId, p.AreaId });
            var request = builder.Entity<MaintenanceRequest>();
            request.Property(r => r.IsLegacy).HasDefaultValue(false);
            request.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
            request.HasOne(r => r.ProviderProfile).WithMany().HasForeignKey(r => r.ProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            request.HasOne(r => r.ServiceCategory).WithMany().HasForeignKey(r => r.ServiceCategoryId).OnDelete(DeleteBehavior.Restrict);
            request.HasOne(r => r.Area).WithMany().HasForeignKey(r => r.AreaId).OnDelete(DeleteBehavior.Restrict);
            request.HasIndex(r => new { r.CustomerId, r.CreatedAtUtc, r.Id });
            request.HasIndex(r => new { r.ProviderProfileId, r.Status, r.CreatedAtUtc, r.Id });
            request.ToTable(t =>
            {
                t.HasCheckConstraint("CK_MaintenanceRequests_Coordinates", "([Latitude] IS NULL AND [Longitude] IS NULL) OR ([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL AND [Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)");
                t.HasCheckConstraint("CK_MaintenanceRequests_Status", "[Status] BETWEEN 1 AND 5");
                t.HasCheckConstraint("CK_MaintenanceRequests_RequestType", "[RequestType] IN (1, 2)");
            });

            // كل مستخدم يمكن أن يكون له ProviderProfile واحد فقط
            builder.Entity<ProviderProfile>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            // لا نحذف Provider عندما نحذف Area بالخطأ
            builder.Entity<ProviderProfile>()
                .HasOne(p => p.Area)
                .WithMany()
                .HasForeignKey(p => p.AreaId)
                .OnDelete(DeleteBehavior.Restrict);

            // لا نحذف Provider عندما نحذف ServiceCategory بالخطأ
            builder.Entity<ProviderProfile>()
                .HasOne(p => p.ServiceCategory)
                .WithMany()
                .HasForeignKey(p => p.ServiceCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
