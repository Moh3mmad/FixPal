namespace FixPal.Models.ViewModels.Dalil;

public sealed class DalilChatRequest
{
    public List<DalilChatMessageInput> Messages { get; set; } = [];
}

public sealed class DalilChatMessageInput
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}
