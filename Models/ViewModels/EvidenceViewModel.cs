namespace FixPal.Models.ViewModels;
public class EvidenceViewModel
{
    public int RequestId { get; set; }
    public EvidenceKind? UploadKind { get; set; }
    public PagedResult<RequestEvidence> Evidence { get; set; } = new();
}
