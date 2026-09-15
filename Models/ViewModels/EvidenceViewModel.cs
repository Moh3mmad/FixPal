namespace FixPal.Models.ViewModels;
public class EvidenceViewModel
{
    public int RequestId { get; set; }
    public bool CanUpload { get; set; }
    public PagedResult<RequestEvidence> Evidence { get; set; } = new();
}
