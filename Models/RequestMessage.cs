using System.ComponentModel.DataAnnotations;
namespace FixPal.Models;
public class RequestMessage
{
    public int Id { get; set; }
    public int MaintenanceRequestId { get; set; }
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    [Required] public string SenderId { get; set; } = string.Empty;
    public ApplicationUser Sender { get; set; } = null!;
    [Required, StringLength(2000)] public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
