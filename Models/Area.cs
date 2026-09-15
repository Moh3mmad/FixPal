using System.ComponentModel.DataAnnotations;

namespace FixPal.Models
{
    public class Area
    {
        public int Id { get; set; }
        public int CityId { get; set; }
        public City City { get; set; } = null!;

        [Required]
        [StringLength(100)]
        [Display(Name = "المنطقة")]
        public string Name { get; set; } = string.Empty;
    }
}
