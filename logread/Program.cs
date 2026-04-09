using GalleryCore;
using GalleryCore.IO;
using System.Text.RegularExpressions;

namespace logread;

class Program
{
    static void Main(string[] args)
    {
        ParsedArgs parsed;
        try
        {
            parsed = ProcessArgs(args);
        }
        catch (InvalidOperationException)
        {
            InvalidExit();
            return;
        }

        var parser = new LogParser();

        if (!parser.ValidateToken(parsed.Token, parsed.LogPath))
        {
            Console.WriteLine("integrity violation");
            Environment.Exit(111);
            return;
        }

        List<LogEvent> history;
        try
        {
            history = parser.ReadAllEvents(parsed.Token, parsed.LogPath);
        }
        catch (IntegrityViolationException)
        {
            Console.WriteLine("integrity violation");
            Environment.Exit(111);
            return;
        }
        catch (FileNotFoundException)
        {
            InvalidExit();
            return;
        }

        if (parsed.QueryS)
            RunQueryS(history);
        else if (parsed.QueryR)
            RunQueryR(history, parsed.PersonType!, parsed.PersonName!);
        else if (parsed.QueryI)
            RunQueryI(history, parsed.QueryIList);
    }

    // ──────────────────────────────────────────────────────────────────
    // ProcessArgs
    //
    // Launches InvalidOperationException on any validation error.
    // ──────────────────────────────────────────────────────────────────
    private static ParsedArgs ProcessArgs(string[] args)
    {
        string? token      = null;
        string? logPath    = null;
        bool    queryS     = false; // -S: print current state
        bool    queryR     = false; // -R: list rooms visited by person
        bool    queryI     = false; // -I: rooms shared by all specified people (optional)
        string? personType = null; // -E or -G
        string? personName = null; // for -R (single person)
        var     queryIList = new List<(string Type, string Name)>(); // for -I (multiple people)

        bool seenK      = false;
        bool seenS      = false;
        bool seenR      = false;
        bool seenI      = false;
        bool seenPerson = false; // -E or -G for -R

        for (int i = 0; i < args.Length; i++)
        {
            string GetNext() => (i + 1 < args.Length)
                ? args[++i]
                : throw new InvalidOperationException();

            switch (args[i])
            {
                case "-K":
                    if (seenK) throw new InvalidOperationException();
                    seenK = true;
                    token = GetNext();
                    break;

                case "-S":
                    if (seenS) throw new InvalidOperationException();
                    seenS = true;
                    queryS = true;
                    break;

                case "-R":
                    if (seenR) throw new InvalidOperationException();
                    seenR = true;
                    queryR = true;
                    break;

                case "-I":
                    if (seenI) throw new InvalidOperationException();
                    seenI = true;
                    queryI = true;
                    break;

                case "-E":
                    if (queryI)
                        queryIList.Add(("E", GetNext()));
                    else
                    {
                        if (seenPerson) throw new InvalidOperationException();
                        seenPerson = true;
                        personType = "E";
                        personName = GetNext();
                    }
                    break;

                case "-G":
                    if (queryI)
                        queryIList.Add(("G", GetNext()));
                    else
                    {
                        if (seenPerson) throw new InvalidOperationException();
                        seenPerson = true;
                        personType = "G";
                        personName = GetNext();
                    }
                    break;

                default:
                    if (!args[i].StartsWith("-"))
                        logPath = args[i];
                    else
                        throw new InvalidOperationException();
                    break;
            }
        }

        // ── Validate arguments ────────────────────────────────────────

        var duplicates = queryIList
            .GroupBy(p => (p.Type, p.Name))
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Any())
            throw new InvalidOperationException("duplicate person in -I query");

        // Token and log path are always required
        if (token == null || logPath == null)
            throw new InvalidOperationException();

        // Token must be alphanumeric
        if (!Regex.IsMatch(token, @"^[a-zA-Z0-9]+$"))
            throw new InvalidOperationException();

        // Exactly one query mode must be specified
        int queryCount = (queryS ? 1 : 0) + (queryR ? 1 : 0) + (queryI ? 1 : 0);
        if (queryCount != 1)
            throw new InvalidOperationException();

        // -R requires exactly one -E or -G 
        if (queryR && (personType == null || personName == null))
            throw new InvalidOperationException();

        // -S must not have -E or -G
        if (queryS && personType != null)
            throw new InvalidOperationException();

        return new ParsedArgs(token, logPath, queryS, queryR, queryI,
                              personType, personName, queryIList);
    }

    // ──────────────────────────────────────────────────────────────────
    // RunQueryS  (-S)
    //
    // Prints the current state of the gallery:
    //   Line 1: comma-separated employees currently in the gallery
    //   Line 2: comma-separated guests currently in the gallery
    //   Remaining lines: room-by-room info (sorted by room ID ascending)
    //     Format: "<roomId>: <name1>,<name2>,..."
    //   Names within each line sorted lexicographically.
    // ──────────────────────────────────────────────────────────────────
    private static void RunQueryS(List<LogEvent> history)
    {
        var people = BuildCurrentState(history);

        var employees = people.Values
            .Where(p => p.Type == EPersonType.Employee && p.InGallery)
            .Select(p => p.Name).OrderBy(n => n).ToList();

        var guests = people.Values
            .Where(p => p.Type == EPersonType.Guest && p.InGallery)
            .Select(p => p.Name).OrderBy(n => n).ToList();

        Console.WriteLine(string.Join(",", employees));
        Console.WriteLine(string.Join(",", guests));

        var rooms = new Dictionary<int, List<string>>();
        foreach (var person in people.Values)
        {
            if (person.InGallery && person.CurrentRoom.HasValue)
            {
                int roomId = person.CurrentRoom.Value;
                if (!rooms.ContainsKey(roomId))
                    rooms[roomId] = new List<string>();
                rooms[roomId].Add(person.Name);
            }
        }

        foreach (var roomId in rooms.Keys.OrderBy(r => r))
            Console.WriteLine($"{roomId}: {string.Join(",", rooms[roomId].OrderBy(n => n))}");
    }

    // ──────────────────────────────────────────────────────────────────
    // RunQueryR  (-R -E/-G <name>)
    //
    // Prints all rooms visited by the specified person in chronological
    // order, including repeated visits. If the person doesn't exist,
    // prints nothing.
    // ──────────────────────────────────────────────────────────────────
    private static void RunQueryR(List<LogEvent> history, string personType, string personName)
    {
        var rooms = history
            .Where(e => e.PersonType == personType &&
                        e.Name       == personName  &&
                        e.Action     == "A"          &&
                        e.RoomId.HasValue)
            .Select(e => e.RoomId!.Value)
            .ToList();

        if (rooms.Count > 0)
            Console.WriteLine(string.Join(",", rooms));
        // If person not found or never entered a room: print nothing
    }

    // ──────────────────────────────────────────────────────────────────
    // RunQueryI  (-I -E/-G <name> [...])
    //
    // Prints the rooms that were occupied by ALL specified people at the
    // same time over the complete history. Room IDs printed in ascending
    // numerical order. If no such room exists, prints nothing.
    // People not in the log are ignored.
    // ──────────────────────────────────────────────────────────────────
    private static void RunQueryI(List<LogEvent> history, List<(string Type, string Name)> people)
    {
        if (people.Count == 0) return;

        // For each person, build a list of (roomId, enterTime, leaveTime) intervals
        // leaveTime = null means they are still in the room
        var intervals = new Dictionary<string, List<(int RoomId, int Enter, int? Leave)>>();

        foreach (var (type, name) in people)
        {
            string key = name + type;
            intervals[key] = new List<(int, int, int?)>();

            int? currentRoom = null;
            int  enterTime   = 0;

            foreach (var evt in history)
            {
                if (evt.PersonType != type || evt.Name != name || !evt.RoomId.HasValue)
                    continue;

                if (evt.Action == "A")
                {
                    currentRoom = evt.RoomId.Value;
                    enterTime   = evt.Timestamp;
                }
                else if (evt.Action == "L" && currentRoom == evt.RoomId.Value)
                {
                    intervals[key].Add((currentRoom.Value, enterTime, evt.Timestamp));
                    currentRoom = null;
                }
            }

            // Still in the room at end of log
            if (currentRoom.HasValue)
                intervals[key].Add((currentRoom.Value, enterTime, null));
        }

        // Find all rooms where all people overlapped at the same time
        var sharedRooms = new HashSet<int>();

        // Collect all unique rooms from all people
        var allRooms = intervals.Values
            .SelectMany(list => list.Select(i => i.RoomId))
            .Distinct();

        foreach (int roomId in allRooms)
        {
            // Check if every specified person has at least one interval in this room
            // that overlaps with at least one interval of every other person
            bool allInRoom = people.All(p =>
                intervals[p.Name + p.Type].Any(i => i.RoomId == roomId));

            if (!allInRoom) continue;

            // Check if there's a common time where ALL were in this room simultaneously
            // Get all intervals for this room per person
            var perPerson = people
                .Select(p => intervals[p.Name + p.Type]
                    .Where(i => i.RoomId == roomId).ToList())
                .ToList();

            if (CheckOverlap(perPerson, 0, int.MinValue, int.MaxValue))
                sharedRooms.Add(roomId);
        }

        if (sharedRooms.Count > 0)
            Console.WriteLine(string.Join(",", sharedRooms.OrderBy(r => r)));
    }

    // ──────────────────────────────────────────────────────────────────
    // CheckOverlap
    //
    // Recursively checks if there's a common time window across all
    // people's intervals in a given room.
    // ──────────────────────────────────────────────────────────────────
    static bool CheckOverlap(
        List<List<(int RoomId, int Enter, int? Leave)>> perPerson,
        int personIndex, int windowStart, int windowEnd)
    {
        if (personIndex == perPerson.Count)
            return windowStart < windowEnd;

        foreach (var interval in perPerson[personIndex])
        {
            int newStart = Math.Max(windowStart, interval.Enter);
            int newEnd   = Math.Min(windowEnd,   interval.Leave ?? int.MaxValue);

            if (newStart < newEnd &&
                CheckOverlap(perPerson, personIndex + 1, newStart, newEnd))
                return true;
        }

        return false;
    }

    // ──────────────────────────────────────────────────────────────────
    // BuildCurrentState
    //
    // Replays all events and returns a dictionary of Person objects
    // representing the current state of the gallery.
    // ──────────────────────────────────────────────────────────────────
    static Dictionary<string, Person> BuildCurrentState(List<LogEvent> history)
    {
        var people = new Dictionary<string, Person>();

        foreach (var evt in history)
        {
            string key = evt.Name + evt.PersonType;

            if (!people.ContainsKey(key))
            {
                EPersonType pt = evt.PersonType switch {
                    "E" => EPersonType.Employee,
                    "G" => EPersonType.Guest,
                    _   => throw new InvalidOperationException()
                };
                people[key] = new Person(evt.Name, pt);
            }

            var person = people[key];

            if      (evt.Action == "A" && evt.RoomId == null) { person.InGallery = true;  person.CurrentRoom = null; }
            else if (evt.Action == "L" && evt.RoomId == null) { person.InGallery = false; person.CurrentRoom = null; }
            else if (evt.Action == "A" && evt.RoomId != null) { person.CurrentRoom = evt.RoomId; }
            else if (evt.Action == "L" && evt.RoomId != null) { person.CurrentRoom = null; }
        }

        return people;
    }

    // ──────────────────────────────────────────────────────────────────
    // InvalidExit — prints "invalid" and exits with code 111
    // ──────────────────────────────────────────────────────────────────
    static void InvalidExit()
    {
        Console.WriteLine("invalid");
        Environment.Exit(111);
    }
}

// ================================================================
//  ParsedArgs — resultado do ProcessArgs
// ================================================================
public record ParsedArgs(
    string  Token,
    string  LogPath,
    bool    QueryS,
    bool    QueryR,
    bool    QueryI,
    string? PersonType,
    string? PersonName,
    List<(string Type, string Name)> QueryIList
);