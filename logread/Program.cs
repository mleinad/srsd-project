using GalleryCore;
using GalleryCore.IO;
using System.Text.RegularExpressions;

namespace logread;

class Program
{
    static void Main(string[] args)
    {
        var parser = new LogParser();

        // ── Parsed arguments ──────────────────────────────────────────
        string?      token      = null;
        string?      logPath    = null;
        bool         queryS     = false;  // -S: print current state
        bool         queryR     = false;  // -R: list rooms visited by person
        bool         queryI     = false;  // -I: rooms shared by all specified people (optional)
        string?      personType = null;   // "E" or "G"
        string?      personName = null;   // for -R (single person)
        var          queryIList = new List<(string Type, string Name)>(); // for -I (multiple people)

        // ── Parse flags ───────────────────────────────────────────────
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-K":
                    if (i + 1 >= args.Length) InvalidExit();
                    token = args[++i];
                    break;

                case "-S":
                    queryS = true;
                    break;

                case "-R":
                    queryR = true;
                    break;

                case "-I":
                    queryI = true;
                    break;

                case "-E":
                    if (i + 1 >= args.Length) InvalidExit();
                    if (queryI)
                        queryIList.Add(("E", args[++i]));
                    else
                    {
                        personType = "E";
                        personName = args[++i];
                    }
                    break;

                case "-G":
                    if (i + 1 >= args.Length) InvalidExit();
                    if (queryI)
                        queryIList.Add(("G", args[++i]));
                    else
                    {
                        personType = "G";
                        personName = args[++i];
                    }
                    break;

                default:
                    if (!args[i].StartsWith("-"))
                        logPath = args[i];
                    else
                        InvalidExit();
                    break;
            }
        }

        // ── Validate arguments ────────────────────────────────────────

        // Token and log path are always required
        if (token == null || logPath == null)
            InvalidExit();

        // Token must be alphanumeric
        if (!Regex.IsMatch(token, @"^[a-zA-Z0-9]+$"))
            InvalidExit();

        // Exactly one query mode must be specified
        int queryCount = (queryS ? 1 : 0) + (queryR ? 1 : 0) + (queryI ? 1 : 0);
        if (queryCount != 1)
            InvalidExit();

        // -R requires exactly one -E or -G
        if (queryR && (personType == null || personName == null))
            InvalidExit();

        // -S must not have -E or -G
        if (queryS && personType != null)
            InvalidExit();

        // ── Validate token and read log ───────────────────────────────
        if (!parser.ValidateToken(token, logPath))
        {
            Console.WriteLine("integrity violation");
            Environment.Exit(111);
        }

        List<LogEvent> history;
        try
        {
            history = parser.ReadAllEvents(token, logPath);
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

        // ── Execute query ─────────────────────────────────────────────
        if (queryS)
            RunQueryS(history);
        else if (queryR)
            RunQueryR(history, personType!, personName!);
        else if (queryI)
        {
            // -I is optional — if not implemented print "unimplemented"
            // But we implement it here for extra points
            RunQueryI(history, queryIList);
        }
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
    static void RunQueryS(List<LogEvent> history)
    {
        var people = BuildCurrentState(history);

        // Employees in gallery, sorted
        var employees = people.Values
            .Where(p => p.Type == "E" && p.InGallery)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        // Guests in gallery, sorted
        var guests = people.Values
            .Where(p => p.Type == "G" && p.InGallery)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        Console.WriteLine(string.Join(",", employees));
        Console.WriteLine(string.Join(",", guests));

        // Build room occupancy: roomId -> list of names
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

        // Print rooms sorted by room ID ascending, names sorted lexicographically
        foreach (var roomId in rooms.Keys.OrderBy(r => r))
        {
            var names = rooms[roomId].OrderBy(n => n).ToList();
            Console.WriteLine($"{roomId}: {string.Join(",", names)}");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // RunQueryR  (-R -E/-G <name>)
    //
    // Prints all rooms visited by the specified person in chronological
    // order, including repeated visits. If the person doesn't exist,
    // prints nothing.
    // ──────────────────────────────────────────────────────────────────
    static void RunQueryR(List<LogEvent> history, string personType, string personName)
    {
        var rooms = new List<int>();

        foreach (var evt in history)
        {
            if (evt.PersonType == personType &&
                evt.Name == personName &&
                evt.Action == "A" &&
                evt.RoomId.HasValue)
            {
                rooms.Add(evt.RoomId.Value);
            }
        }

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
    static void RunQueryI(List<LogEvent> history, List<(string Type, string Name)> people)
    {
        if (people.Count == 0)
            return;

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
                    .Where(i => i.RoomId == roomId)
                    .ToList())
                .ToList();

            // Try every combination of one interval per person and check for overlap
            bool foundOverlap = CheckOverlap(perPerson, 0, int.MinValue, int.MaxValue);
            if (foundOverlap)
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
        int personIndex,
        int windowStart,
        int windowEnd)
    {
        if (personIndex == perPerson.Count)
            return windowStart < windowEnd;  // valid overlap window exists

        foreach (var interval in perPerson[personIndex])
        {
            int enter = interval.Enter;
            int leave = interval.Leave ?? int.MaxValue;

            // Intersect with current window
            int newStart = Math.Max(windowStart, enter);
            int newEnd   = Math.Min(windowEnd,   leave);

            if (newStart < newEnd)  // overlap exists
            {
                if (CheckOverlap(perPerson, personIndex + 1, newStart, newEnd))
                    return true;
            }
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
                people[key] = new Person(evt.Name, evt.PersonType);

            var person = people[key];

            if (evt.Action == "A" && evt.RoomId == null)
            {
                person.InGallery   = true;
                person.CurrentRoom = null;
            }
            else if (evt.Action == "L" && evt.RoomId == null)
            {
                person.InGallery   = false;
                person.CurrentRoom = null;
            }
            else if (evt.Action == "A" && evt.RoomId != null)
            {
                person.CurrentRoom = evt.RoomId;
            }
            else if (evt.Action == "L" && evt.RoomId != null)
            {
                person.CurrentRoom = null;
            }
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