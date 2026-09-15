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
        public DbSet<RequestMessage> RequestMessages { get; set; }
        public DbSet<RequestEvidence> RequestEvidence { get; set; }
        public DbSet<ProviderReview> ProviderReviews { get; set; }
        public DbSet<ServiceCategory> ServiceCategories { get; set; }
        public DbSet<ProviderProfile> ProviderProfiles { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
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
