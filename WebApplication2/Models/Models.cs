namespace Bedrock.Models;

public class DataModel
{
    public string Data { get; set; }
    public string DeviceId { get; set; }
    public string Content { get; set; }
}

public class BedrockContent
{
    public string Partition;
    public string Project;
    public string ParentContent;
    public string Id;
    public string Text;
    public bool Done;
    public long Tick;
    public long DoneTick;
}

public class BedrockProject
{
    public string Partition;
    public string Id;
    public string UserId;
    public string Name;
    public long CreateTick;
    public long LastOpenTick;
    public bool IsArchive;
    public long ArchiveTick;
}

public class BedrockDeviceId
{
    public string Id;
    public string UserId;
    public string Partition;
}

public class BedrockEmailId
{
    public string Id;
    public string UserId;
    public string Partition;
}

public class EmailCode
{
    public string Email;
    public string Code;
    public DateTime DateTime;
    public string Partition;
}

public class BedrockUserSetting
{
    public string UserId;
    public string Partition;
    public bool ShowDate;
    public string CurrentProject;
}
