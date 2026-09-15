using System.ComponentModel.DataAnnotations;
namespace FixPal.Models;
public class City
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<Area> Areas { get; set; } = new List<Area>();
}
