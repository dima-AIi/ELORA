namespace ELORA.Web.DTOs;

public class SlotDto
{
    public string Time { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
}

public class SlotResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<SlotDto> Slots { get; set; } = new();

    public static SlotResult Fail(string error) => new() { Ok = false, Error = error };
}

public class MasterDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Specialization { get; set; }
    public string? PhotoPath { get; set; }
}

public class BookingRequest
{
    public int ServiceId { get; set; }
    public int MasterId { get; set; }
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Telegram { get; set; }
    public string? Comment { get; set; }
}

public class BookingResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int BookingId { get; set; }
    public string ManageToken { get; set; } = "";
    public string TelegramStartLink { get; set; } = "";
    public string Summary { get; set; } = "";
}

/// <summary>Тело запроса к AI-консультанту: вопрос и предыдущие реплики диалога.</summary>
public class ChatRequest
{
    public string? Message { get; set; }
    public List<ChatTurnDto>? History { get; set; }
}

/// <summary>Реплика диалога, как её присылает браузер.</summary>
public class ChatTurnDto
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

/// <summary>Ответ консультанта. При отказе <c>reply</c> пуст, а причина — в <c>error</c>.</summary>
public class ChatReply
{
    public bool Ok { get; set; }
    public string? Reply { get; set; }
    public string? Error { get; set; }
}
