namespace GalleryCore;

public class LogEvent
{
    public int             Timestamp  { get; set; }  // seconds since gallery opened
    public required string PersonType { get; set; }  // "E" = Employee | "G" = Guest
    public required string Name       { get; set; }  // person's name
    public required string Action     { get; set; }  // "A" = Arrive | "L" = Leave
    public int?            RoomId     { get; set; }  // null = entire gallery

    public string Serialize()
        => $"{Timestamp},{PersonType},{Name},{Action},{RoomId?.ToString() ?? ""}";

    public static LogEvent Deserialize(string line)
    {
        var parts = line.Split(',');
        if (parts.Length != 5)
            throw new FormatException($"Invalid log line: '{line}'");

        return new LogEvent
        {
            Timestamp  = int.Parse(parts[0]),
            PersonType = parts[1],
            Name       = parts[2],
            Action     = parts[3],
            RoomId     = string.IsNullOrEmpty(parts[4]) ? null : int.Parse(parts[4])
        };
    }
}