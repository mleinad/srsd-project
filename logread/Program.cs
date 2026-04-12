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
        catch (InvalidCommandException)
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
            if (!File.Exists(parsed.LogPath))
            {
                InvalidExit();
                return;
            }
            history = parser.ReadAllEvents(parsed.Token, parsed.LogPath);
        }
        catch (IntegrityViolationException)
        {
            Console.WriteLine("integrity violation");
            Environment.Exit(111);
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
    // Parses and validates all command-line arguments for logread.
    // Throws InvalidCommandException on any invalid or contradictory
    // combination so that Main can catch it and call InvalidExit().
    // ──────────────────────────────────────────────────────────────────
    private static ParsedArgs ProcessArgs(string[] args)
    {
        string? token      = null;
        string? logPath    = null;
        bool    queryS     = false;
        bool    queryR     = false;
        bool    queryI     = false;
        string? personType = null;
        string? personName = null;
        var     queryIList = new List<(string Type, string Name)>();

        bool seenK      = false;
        bool seenS      = false;
        bool seenR      = false;
        bool seenI      = false;
        bool seenPerson = false;

        for (int i = 0; i < args.Length; i++)
        {
            string GetNext() => (i + 1 < args.Length)
                ? args[++i]
                : throw new InvalidCommandException();

            switch (args[i])
            {
                case "-K":
                    if (seenK) throw new InvalidCommandException();
                    seenK = true;
                    token = GetNext();
                    break;

                case "-S":
                    if (seenS) throw new InvalidCommandException();
                    seenS  = true;
                    queryS = true;
                    break;

                case "-R":
                    if (seenR) throw new InvalidCommandException();
                    seenR  = true;
                    queryR = true;
                    break;

                case "-I":
                    if (seenI) throw new InvalidCommandException();
                    seenI  = true;
                    queryI = true;
                    break;

                case "-E":
                    // When used with -I, multiple -E/-G flags are allowed (one per person).
                    // When used with -R or -S, only a single -E or -G is permitted.
                    if (queryI)
                        queryIList.Add(("E", GetNext()));
                    else
                    {
                        if (seenPerson) throw new InvalidCommandException();
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
                        if (seenPerson) throw new InvalidCommandException();
                        seenPerson = true;
                        personType = "G";
                        personName = GetNext();
                    }
                    break;

                default:
                    if (!args[i].StartsWith("-"))
                        logPath = args[i];
                    else
                        throw new InvalidCommandException();
                    break;
            }
        }

        var duplicates = queryIList
            .GroupBy(p => (p.Type, p.Name))
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicates.Any())
            throw new InvalidCommandException();

        if (token == null || logPath == null)
            throw new InvalidCommandException();

        if (!Regex.IsMatch(token, @"^[a-zA-Z0-9]+$"))
            throw new InvalidCommandException();

        int queryCount = (queryS ? 1 : 0) + (queryR ? 1 : 0) + (queryI ? 1 : 0);
        if (queryCount != 1)
            throw new InvalidCommandException();

        if (queryR && (personType == null || personName == null))
            throw new InvalidCommandException();

        if (queryS && personType != null)
            throw new InvalidCommandException();

        return new ParsedArgs(token, logPath, queryS, queryR, queryI,
                              personType, personName, queryIList);
    }

    // ──────────────────────────────────────────────────────────────────
    // RunQueryS  (-S)
    //
    // Prints the current state of the gallery to stdout:
    //   Line 1 — comma-separated list of employees currently inside.
    //   Line 2 — comma-separated list of guests currently inside.
    //   Lines 3+ — one line per occupied room, format "<roomId>: <names>",
    //              rooms ordered by ascending integer ID, names ordered
    //              lexicographically (ordinal / ASCII order).
    // ──────────────────────────────────────────────────────────────────
    private static void RunQueryS(List<LogEvent> history)
    {
        var people = BuildCurrentState(history);

        var employees = people.Values
            .Where(p => p.Type == EPersonType.Employee && p.InGallery)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var guests = people.Values
            .Where(p => p.Type == EPersonType.Guest && p.InGallery)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

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
            Console.WriteLine($"{roomId}: {string.Join(",", rooms[roomId].OrderBy(n => n, StringComparer.Ordinal))}");
    }

    // ──────────────────────────────────────────────────────────────────
    // RunQueryR  (-R -E/-G <name>)
    //
    // Prints all rooms ever entered by the specified person, in
    // chronological (arrival) order, including repeated visits across
    // multiple gallery sessions. Prints nothing if the person has never
    // entered any room or does not appear in the log at all.
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
    }

    // ──────────────────────────────────────────────────────────────────
    // RunQueryI  (-I -E/-G <name> [...])
    //
    // Prints the rooms that were occupied simultaneously by every one of
    // the specified people at some point in the log's history. Room IDs
    // are printed in ascending numerical order. People who never appear
    // in the log are silently ignored. Prints nothing if no room ever
    // contained all of them at the same time.
    // ──────────────────────────────────────────────────────────────────
    private static void RunQueryI(List<LogEvent> history, List<(string Type, string Name)> people)
    {
        if (people.Count == 0) return;

        // Build a list of (roomId, enterTime, leaveTime) intervals for each person.
        // leaveTime == null means the person is still in the room at the end of the log.
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

            // Person was still in the room when the log ended.
            if (currentRoom.HasValue)
                intervals[key].Add((currentRoom.Value, enterTime, null));
        }

        // For each room visited by at least one person, check whether all
        // specified people were present simultaneously at some point.
        var sharedRooms = new HashSet<int>();

        var allRooms = intervals.Values
            .SelectMany(list => list.Select(i => i.RoomId))
            .Distinct();

        foreach (int roomId in allRooms)
        {
            // Skip rooms that not every person has visited.
            bool allVisited = people.All(p =>
                intervals[p.Name + p.Type].Any(i => i.RoomId == roomId));
            if (!allVisited) continue;

            // Collect each person's intervals in this room and check for
            // a common time window using recursive interval intersection.
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
    // Recursively intersects time windows across all people's intervals
    // in a given room. Returns true if there exists a non-empty window
    // during which every person was simultaneously present.
    //
    // personIndex — index of the person being processed in this call.
    // windowStart / windowEnd — the intersection window accumulated so far.
    // ──────────────────────────────────────────────────────────────────
    private static bool CheckOverlap(
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
    // Replays every event in the log and returns a snapshot of the
    // gallery at the time of the last recorded event. The dictionary
    // key is "<name><personType>" (e.g. "AliceE") to keep employees
    // and guests with the same name as distinct entities.
    // ──────────────────────────────────────────────────────────────────
    private static Dictionary<string, Person> BuildCurrentState(List<LogEvent> history)
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
                    _   => throw new InvalidCommandException()
                };
                people[key] = new Person(evt.Name, pt);
            }

            var person = people[key];

            if      (evt.Action == "A" && evt.RoomId == null) { person.InGallery = true;  person.CurrentRoom = null;       }
            else if (evt.Action == "L" && evt.RoomId == null) { person.InGallery = false; person.CurrentRoom = null;       }
            else if (evt.Action == "A" && evt.RoomId != null) { person.CurrentRoom = evt.RoomId;                           }
            else if (evt.Action == "L" && evt.RoomId != null) { person.CurrentRoom = null;                                 }
        }

        return people;
    }

    // ──────────────────────────────────────────────────────────────────
    // InvalidExit
    //
    // Prints "invalid" to stdout and exits with code 111.
    // ──────────────────────────────────────────────────────────────────
    private static void InvalidExit()
    {
        Console.WriteLine("invalid");
        Environment.Exit(111);
    }
}

// ════════════════════════════════════════════════════════════════════
//  ParsedArgs
//
//  Immutable record that carries the validated result of ProcessArgs.
//  Passed directly to the query methods so they receive only what they
//  need without re-parsing the command line.
// ════════════════════════════════════════════════════════════════════
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