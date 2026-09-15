namespace FixPal.Models.ViewModels;
public record MessageItem(int Id, string Text, DateTime CreatedAtUtc, bool IsMine, bool IsCustomer);
public class RequestMessagesViewModel
{
    public int RequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool CanSend { get; set; }
    public PagedResult<MessageItem> Messages { get; set; } = new();
}
