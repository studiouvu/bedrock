﻿namespace Bedrock.Models;

public class DataModel
{
    public string Data { get; set; }
    public string DeviceId { get; set; }
    public string Content { get; set; }
}

public class ContentTextDataModel
{
    public string Data { get; set; }
    public string DeviceId { get; set; }
    public string Content { get; set; }
    public int Depth { get; set; }
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
    public int depth;
    public string UserId;
    public bool IsTemplate;
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
    public ProjectType ProjectType;
}

public class BedrockDiaryContent
{
    public string Partition;
    public string ProjectId;
    public string UserId;
    public long LastTick;
    public string Content;
    public string Summary;
}

public enum ProjectType
{
    Task,
    Diary,
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
    public bool ShowDoneTask;
    public string CurrentProject;
    public string DiarySummary;
    public DateTime DiarySummaryUpdateTime;
}

public class BedrockSecretary
{
    public string UserId;
    public string Partition;
    public string Content;
    public long lastUpdateTick;
}

public class BedrockDiary
{
    public string UserId;
    public string Partition;
    public string Content;
    public string GptContent;
    public long createTick;
    public long lastUpdateTick;
}
