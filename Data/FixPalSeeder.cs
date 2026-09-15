using FixPal.Models;
using Microsoft.EntityFrameworkCore;

namespace FixPal.Data
{
    public static class FixPalSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            var context = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

            var jerusalem = await context.Cities.SingleOrDefaultAsync(c => c.Name == "القدس");
            if (jerusalem == null)
            {
                jerusalem = new City { Name = "القدس" };
                context.Cities.Add(jerusalem);
                await context.SaveChangesAsync();
            }

            if (!await context.Areas.AnyAsync())
            {
                await context.Areas.AddRangeAsync(
                    new Area { Name = "البلدة القديمة", CityId = jerusalem.Id },
                    new Area { Name = "بيت حنينا", CityId = jerusalem.Id },
                    new Area { Name = "شعفاط", CityId = jerusalem.Id },
                    new Area { Name = "الشيخ جراح", CityId = jerusalem.Id },
                    new Area { Name = "سلوان", CityId = jerusalem.Id },
                    new Area { Name = "الطور", CityId = jerusalem.Id },
                    new Area { Name = "صور باهر", CityId = jerusalem.Id }
                );
            }

            if (!await context.ServiceCategories.AnyAsync())
            {
                await context.ServiceCategories.AddRangeAsync(
                    new ServiceCategory
                    {
                        Name = "سباكة",
                        Description = "إصلاح التسربات والمواسير ومشاكل المياه",
                        Icon = "bi-droplet-half"
                    },
                    new ServiceCategory
                    {
                        Name = "كهرباء",
                        Description = "إصلاح الأعطال والتمديدات الكهربائية",
                        Icon = "bi-lightning-charge-fill"
                    },
                    new ServiceCategory
                    {
                        Name = "نجارة",
                        Description = "صيانة الأبواب والأثاث والأعمال الخشبية",
                        Icon = "bi-hammer"
                    },
                    new ServiceCategory
                    {
                        Name = "تكييف",
                        Description = "تركيب وصيانة وتنظيف أجهزة التكييف",
                        Icon = "bi-fan"
                    },
                    new ServiceCategory
                    {
                        Name = "صيانة عامة",
                        Description = "أعمال الصيانة العامة للمنازل والمرافق",
                        Icon = "bi-gear-wide-connected"
                    }
                );
            }

            await context.SaveChangesAsync();
        }
    }
}
