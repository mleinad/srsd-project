using System.Text.RegularExpressions;

namespace GalleryCore;

public class LogEvent
{
    public int             Timestamp  { get; init; }  // seconds since gallery opened
    public required string PersonType { get; init; }  // "E" = Employee | "G" = Guest
    public required string Name       { get; init; }  // person's name
    public required string Action     { get; init; }  // "A" = Arrive | "L" = Leave
    public int?            RoomId     { get; init; }  // null = entire gallery

    public string Serialize()
        => $"{Timestamp},{PersonType},{Name},{Action},{RoomId?.ToString() ?? ""}";

    public static LogEvent Deserialize(string line)
    {
        var parts = line.Split(',');
        if (parts.Length != 5)
            throw new IntegrityViolationException();

        // Timestamp: must be a positive integer within valid range
        if (!int.TryParse(parts[0], out int timestamp) || timestamp < 1 || timestamp > 1_073_741_823)
            throw new IntegrityViolationException();

        // PersonType: must be exactly "E" or "G"
        string personType = parts[1];
        if (personType != "E" && personType != "G")
            throw new IntegrityViolationException();

        // Name: must be non-empty alphabetic string
        string name = parts[2];
        if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"^[a-zA-Z]+$"))
            throw new IntegrityViolationException();

        // Action: must be exactly "A" or "L"
        string action = parts[3];
        if (action != "A" && action != "L")
            throw new IntegrityViolationException();

        // RoomId: empty (gallery-level event) or valid non-negative integer
        int? roomId = null;
        if (!string.IsNullOrEmpty(parts[4]))
        {
            if (!int.TryParse(parts[4], out int rid) || rid < 0 || rid > 1_073_741_823)
                throw new IntegrityViolationException();
            roomId = rid;
        }

        return new LogEvent
        {
            Timestamp  = timestamp,
            PersonType = personType,
            Name       = name,
            Action     = action,
            RoomId     = roomId
        };
    }
}