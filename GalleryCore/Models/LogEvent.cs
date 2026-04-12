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

        if (!int.TryParse(parts[0], out int timestamp) || timestamp < 1 || timestamp > 1_073_741_823)
            throw new IntegrityViolationException();

        string personType = parts[1];
        if (personType != "E" && personType != "G")
            throw new IntegrityViolationException();

        string name = parts[2];
        if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"^[a-zA-Z]+$"))
            throw new IntegrityViolationException();

        string action = parts[3];
        if (action != "A" && action != "L")
            throw new IntegrityViolationException();

        int? roomId = null;
        if (!string.IsNullOrEmpty(parts[4]))
        {
            if (!int.TryParse(parts[4], out int rid) || rid < 0 || rid > 1_073_741_823)
                throw new IntegrityViolationException();
            roomId = rid;
        }

        // Validate PersonType
        if (parts[1] is not "E" and not "G")
            throw new FormatException($"Invalid person type: '{parts[1]}'");

        // Validate Action
        if (parts[3] is not "A" and not "L")
            throw new FormatException($"Invalid action: '{parts[3]}'");

        // Validate Timestamp
        if (!int.TryParse(parts[0], out int timestamp) || timestamp < 1 || timestamp > 1_073_741_823)
            throw new FormatException($"Invalid timestamp: '{parts[0]}'");

        // Validate Name
        if (!System.Text.RegularExpressions.Regex.IsMatch(parts[2], @"^[a-zA-Z]+$"))
            throw new FormatException($"Invalid name: '{parts[2]}'");

        // Validate RoomId
        int? roomId = null;
        if (!string.IsNullOrEmpty(parts[4]))
        {
            if (!int.TryParse(parts[4], out int rid) || rid < 0 || rid > 1_073_741_823)
                throw new FormatException($"Invalid room ID: '{parts[4]}'");
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