using GalleryCore;
using GalleryCore.IO;
using System.Text.RegularExpressions;

namespace logappend;

class Program
{
    static void Main(string[] args)
    {
        var parser = new LogParser();

        // ── Parsed arguments ──────────────────────────────────────────
        string? token         = null;
        int?    timestamp     = null;
        string? employeeName  = null;
        string? guestName     = null;
        bool    arrivalFlag   = false;
        bool    departureFlag = false;
        string? roomId        = null;
        string? logPath       = null;
        string? batchFile     = null;

        // ── Parse flags ───────────────────────────────────────────────
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-K":
                    if (i + 1 >= args.Length) InvalidExit();
                    token = args[++i];
                    break;

                case "-T":
                    if (i + 1 >= args.Length) InvalidExit();
                    if (!int.TryParse(args[++i], out int ts)) InvalidExit();
                    timestamp = ts;
                    break;

                case "-E":
                    if (i + 1 >= args.Length) InvalidExit();
                    employeeName = args[++i];
                    break;

                case "-G":
                    if (i + 1 >= args.Length) InvalidExit();
                    guestName = args[++i];
                    break;

                case "-A":
                    arrivalFlag = true;
                    break;

                case "-L":
                    departureFlag = true;
                    break;

                case "-R":
                    if (i + 1 >= args.Length) InvalidExit();
                    roomId = args[++i];
                    break;

                case "-B":
                    if (i + 1 >= args.Length) InvalidExit();
                    batchFile = args[++i];
                    break;

                default:
                    // Any non-flag argument is the log path (positional)
                    if (!args[i].StartsWith("-"))
                        logPath = args[i];
                    else
                        InvalidExit();
                    break;
            }
        }

        // ── Batch mode ────────────────────────────────────────────────
        if (batchFile != null)
        {
            if (!File.Exists(batchFile))
                InvalidExit();

            string[] lines = File.ReadAllLines(batchFile);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Split line into tokens respecting spaces
                string[] lineArgs = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // -B is not allowed inside a batch file
                if (lineArgs.Contains("-B"))
                {
                    Console.WriteLine("invalid");
                    continue;
                }

                try
                {
                    ProcessCommand(lineArgs, parser);
                }
                catch (InvalidOperationException)
                {
                    Console.WriteLine("invalid");
                }
                catch (IntegrityViolationException)
                {
                    Console.WriteLine("integrity violation");
                }
            }

            Environment.Exit(0);
        }

        // ── Single-command mode ───────────────────────────────────────
        try
        {
            // Re-bundle parsed values into an array and process
            ValidateAndAppend(
                token, timestamp, employeeName, guestName,
                arrivalFlag, departureFlag, roomId, logPath, parser
            );
        }
        catch (InvalidOperationException)
        {
            InvalidExit();
        }
        catch (IntegrityViolationException)
        {
            Console.WriteLine("integrity violation");
            Environment.Exit(111);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // ProcessCommand
    // Parses a single command line (from batch or recursion) and appends.
    // Throws InvalidOperationException on any validation failure.
    // ──────────────────────────────────────────────────────────────────
    static void ProcessCommand(string[] args, LogParser parser)
    {
        string? token         = null;
        int?    timestamp     = null;
        string? employeeName  = null;
        string? guestName     = null;
        bool    arrivalFlag   = false;
        bool    departureFlag = false;
        string? roomId        = null;
        string? logPath       = null;

        for (int i = 0; i < args.Length; i++)
        {
            // Helper to safely get the next argument without crashing
            string GetNext() => (i + 1 < args.Length) ? args[++i] : null;
            
            switch (args[i])
            {
                case "-K":
                    if (i + 1 >= args.Length) throw new InvalidOperationException();
                    token = args[++i];
                    break;
                case "-T":
                    if (i + 1 >= args.Length) throw new InvalidOperationException();
                    if (!int.TryParse(args[++i], out int ts)) throw new InvalidOperationException();
                    timestamp = ts;
                    break;
                case "-E":
                    if (i + 1 >= args.Length) throw new InvalidOperationException();
                    employeeName = args[++i];
                    break;
                case "-G":
                    if (i + 1 >= args.Length) throw new InvalidOperationException();
                    guestName = args[++i];
                    break;
                case "-A":
                    arrivalFlag = true;
                    break;
                case "-L":
                    departureFlag = true;
                    break;
                case "-R":
                    if (i + 1 >= args.Length) throw new InvalidOperationException();
                    roomId = args[++i];
                    break;
                case "-B":
                    // -B is not allowed inside batch files
                    throw new InvalidOperationException();
                default:
                    if (!args[i].StartsWith("-"))
                        logPath = args[i];
                    else
                        throw new InvalidOperationException();
                    break;
            }
        }
        

        ValidateAndAppend(token, timestamp, employeeName, guestName,
                          arrivalFlag, departureFlag, roomId, logPath, parser);
    }

    // ──────────────────────────────────────────────────────────────────
    // ValidateAndAppend
    // Validates all arguments and, if everything is consistent, appends
    // the event to the log. Throws InvalidOperationException on any error.
    // ──────────────────────────────────────────────────────────────────
    static void ValidateAndAppend(
        string? token,
        int?    timestamp,
        string? employeeName,
        string? guestName,
        bool    arrivalFlag,
        bool    departureFlag,
        string? roomId,
        string? logPath,
        LogParser parser)
    {
        // ── Mandatory fields ──────────────────────────────────────────
        if (token == null || timestamp == null || logPath == null)
            throw new InvalidOperationException("Missing required arguments.");

        // Exactly one of -A or -L must be set
        if (arrivalFlag == departureFlag)   // both false OR both true
            throw new InvalidOperationException("Must specify exactly one of -A or -L.");

        // Exactly one of -E or -G must be set
        if (employeeName == null && guestName == null)
            throw new InvalidOperationException("Must specify -E or -G.");
        if (employeeName != null && guestName != null)
            throw new InvalidOperationException("Cannot specify both -E and -G.");

        // ── Token validation ──────────────────────────────────────────
        if (!ValidToken(token))
            throw new InvalidOperationException("Invalid token.");

        // ── Timestamp range: 1 to 1,073,741,823 ──────────────────────
        if (timestamp < 1 || timestamp > 1_073_741_823)
            throw new InvalidOperationException("Timestamp out of range.");

        // ── Name validation: alphabetic only per spec ─────────────────
        string name = (employeeName ?? guestName)!;
        if (!Regex.IsMatch(name, @"^[a-zA-Z]+$"))
            throw new InvalidOperationException("Invalid name.");

        // ── Room ID validation ────────────────────────────────────────
        int? roomIdInt = null;
        if (roomId != null)
        {
            if (!int.TryParse(roomId, out int rid) || rid < 0 || rid > 1_073_741_823)
                throw new InvalidOperationException("Invalid room ID.");
            roomIdInt = rid;  // leading zeros are dropped automatically by int.Parse
        }

        // ── Log path validation ───────────────────────────────────────
        if (!ValidLogPath(logPath))
            throw new InvalidOperationException("Invalid log path.");

        // ── Token must match the existing log ─────────────────────────
        if (!parser.ValidateToken(token, logPath))
            throw new IntegrityViolationException();

        // ── Timestamp must be greater than the last recorded one ──────
        int lastTimestamp = parser.GetLastTimestamp(token, logPath);
        if (timestamp <= lastTimestamp)
            throw new InvalidOperationException("Timestamp is not greater than the last recorded timestamp.");

        // ── Reconstruct current gallery state from the log ────────────
        List<LogEvent> history = File.Exists(logPath)
            ? parser.ReadAllEvents(token, logPath)
            : new List<LogEvent>();

        var people = BuildState(history);
        
        // Ternary operators to set persontype & action to the correct value
        string personType = employeeName != null ? "E" : "G";
        string action     = arrivalFlag ? "A" : "L";

        // ── Business-logic consistency checks ─────────────────────────
        people.TryGetValue(name + personType, out Person? person);

        if (action == "A" && roomIdInt == null)
        {
            // Arriving at the gallery
            if (person != null && person.InGallery)
                throw new InvalidOperationException("Person is already in the gallery.");
        }
        else if (action == "L" && roomIdInt == null)
        {
            // Leaving the gallery
            if (person == null || !person.InGallery)
                throw new InvalidOperationException("Person is not in the gallery.");
            if (person.CurrentRoom != null)
                throw new InvalidOperationException("Person must leave their current room before leaving the gallery.");
        }
        else if (action == "A" && roomIdInt != null)
        {
            // Arriving at a room
            if (person == null || !person.InGallery)
                throw new InvalidOperationException("Person must be in the gallery before entering a room.");
            if (person.CurrentRoom != null)
                throw new InvalidOperationException("Person must leave their current room before entering another.");
        }
        else if (action == "L" && roomIdInt != null)
        {
            // Leaving a room
            if (person == null || !person.InGallery)
                throw new InvalidOperationException("Person is not in the gallery.");
            if (person.CurrentRoom != roomIdInt)
                throw new InvalidOperationException("Person is not in the specified room.");
        }

        // ── All checks passed — build and append the event ────────────
        var logEvent = new LogEvent
        {
            Timestamp  = timestamp.Value,
            PersonType = personType,
            Name       = name,
            Action     = action,
            RoomId     = roomIdInt
        };

        parser.AppendEvent(logEvent, token, logPath);
    }

    // ──────────────────────────────────────────────────────────────────
    // BuildState
    // Replays all log events and returns a dictionary of Person objects
    // keyed by Name+Type (e.g., "FredE", "JillG") representing the
    // current state of the gallery.
    // ──────────────────────────────────────────────────────────────────
    static Dictionary<string, Person> BuildState(List<LogEvent> history)
    {
        var people = new Dictionary<string, Person>();

        foreach (var evt in history)
        {
            string key = evt.Name + evt.PersonType;

            if (!people.ContainsKey(key))
                people[key] = new Person(evt.Name, Enum.Parse<EPersonType>(evt.PersonType));

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
    // ValidToken
    // Token must be alphanumeric (a-z, A-Z, 0-9), non-empty.
    // ──────────────────────────────────────────────────────────────────
    static bool ValidToken(string t)
        => !string.IsNullOrEmpty(t) && Regex.IsMatch(t, @"^[a-zA-Z0-9]+$");

    // ──────────────────────────────────────────────────────────────────
    // ValidLogPath
    // Log filename: alphanumeric, underscores, periods, slashes allowed.
    // ──────────────────────────────────────────────────────────────────
    static bool ValidLogPath(string path)
        => !string.IsNullOrEmpty(path) && Regex.IsMatch(path, @"^[a-zA-Z0-9_./\\]+$");

    // ──────────────────────────────────────────────────────────────────
    // InvalidExit
    // Prints "invalid" and exits with code 111 per spec.
    // ──────────────────────────────────────────────────────────────────
    static void InvalidExit()
    {
        Console.WriteLine("invalid");
        Environment.Exit(111);
    }
}