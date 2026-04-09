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
            {
                Console.WriteLine("invalid");
                Environment.Exit(111);
                return;
            }
            RunBatch(batchFile);
            Environment.Exit(0);
            return;
        }

        try
        {
            ValidateAndAppend(
                token, timestamp, employeeName, guestName,
                arrivalFlag, departureFlag, roomId, logPath, parser);
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
    // RunBatch
    //
    // If the batch file does not exist, prints "invalid" to stdout.
    // ──────────────────────────────────────────────────────────────────
    private static void RunBatch(string batchFile)
    {
        var parser = new LogParser();

        if (!File.Exists(batchFile))
        {
            Console.WriteLine("invalid");
            return;
        }

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
    }

    // ──────────────────────────────────────────────────────────────────
    // ProcessCommand
    //
    // Parses a single command line (from batch) and appends.
    // Throws InvalidOperationException on any validation failure.
    // ──────────────────────────────────────────────────────────────────
    private static void ProcessCommand(string[] args, LogParser parser)
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
            string GetNext() => (i + 1 < args.Length) ? args[++i] : throw new InvalidOperationException();

            switch (args[i])
            {
                case "-K":  token        = GetNext(); break;
                case "-E":  employeeName = GetNext(); break;
                case "-G":  guestName    = GetNext(); break;
                case "-R":  roomId       = GetNext(); break;
                case "-T":
                    if (!int.TryParse(GetNext(), out int ts)) throw new InvalidOperationException();
                    timestamp = ts;
                    break;
                case "-A":  arrivalFlag   = true; break;
                case "-L":  departureFlag = true; break;
                case "-B":  throw new InvalidOperationException();
                default:
                    if (!args[i].StartsWith("-")) logPath = args[i];
                    else throw new InvalidOperationException();
                    break;
            }
        }

        ValidateAndAppend(token, timestamp, employeeName, guestName,
                          arrivalFlag, departureFlag, roomId, logPath, parser);
    }

    // ──────────────────────────────────────────────────────────────────
    // ValidateAndAppend
    //
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
        // ── Mandatory presence ────────────────────────────────────────
        if (token == null || timestamp == null || logPath == null)
            throw new InvalidOperationException("Missing required arguments.");

        if (arrivalFlag == departureFlag)
            throw new InvalidOperationException("Must specify exactly one of -A or -L.");

        if (employeeName == null && guestName == null)
            throw new InvalidOperationException("Must specify -E or -G.");

        if (employeeName != null && guestName != null)
            throw new InvalidOperationException("Cannot specify both -E and -G.");

        // ── Format validation ─────────────────────────────────────────
        if (!ValidToken(token))
            throw new InvalidOperationException("Invalid token.");

        if (timestamp < 1 || timestamp > 1_073_741_823)
            throw new InvalidOperationException("Timestamp out of range.");

        string name = (employeeName ?? guestName)!;
        if (!Regex.IsMatch(name, @"^[a-zA-Z]+$"))
            throw new InvalidOperationException("Invalid name.");

        int? roomIdInt = null;
        if (roomId != null)
        {
            if (!int.TryParse(roomId, out int rid) || rid < 0 || rid > 1_073_741_823)
                throw new InvalidOperationException("Invalid room ID.");
            roomIdInt = rid;
        }

        if (!ValidLogPath(logPath))
            throw new InvalidOperationException("Invalid log path.");

        // ── Token / integrity check ───────────────────────────────────
        if (!parser.ValidateToken(token, logPath))
            throw new IntegrityViolationException();

        // ── Replay state and validate the new event ───────────────────
        List<LogEvent> history = File.Exists(logPath)
            ? parser.ReadAllEvents(token, logPath)
            : new List<LogEvent>();

        var state = new GalleryState();
        foreach (var evt in history)
        {
            EPersonType t = evt.PersonType switch {
                "E" => EPersonType.Employee,
                "G" => EPersonType.Guest,
                _   => throw new InvalidOperationException()
            };
            state.ApplyEvent(evt.Timestamp, evt.Name, t, evt.Action == "A", evt.RoomId);
        }

        // Try applying the new event - GalleryState handles all business logic
        EPersonType personType = employeeName != null ? EPersonType.Employee : EPersonType.Guest;
        try
        {
            state.ApplyEvent(timestamp.Value, name, personType, arrivalFlag, roomIdInt);
        }
        catch (InvalidCommandException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }

        // ── All checks passed — append the event ──────────────────────
        var logEvent = new LogEvent
        {
            Timestamp  = timestamp.Value,
            PersonType = employeeName != null ? "E" : "G",
            Name       = name,
            Action     = arrivalFlag ? "A" : "L",
            RoomId     = roomIdInt
        };

        try {
            parser.AppendEvent(logEvent, token, logPath);
        }
        catch (IntegrityViolationException)
        {
            throw; 
        }
        catch (IOException)  
        {
            throw new InvalidOperationException("invalid log path");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // ValidToken
    //
    // Token must be alphanumeric (a-z, A-Z, 0-9), non-empty.
    // ──────────────────────────────────────────────────────────────────
    static bool ValidToken(string t)
        => !string.IsNullOrEmpty(t) && Regex.IsMatch(t, @"^[a-zA-Z0-9]+$");

    // ──────────────────────────────────────────────────────────────────
    // ValidLogPath
    //
    // Log filename: alphanumeric, underscores, periods, slashes allowed.
    // ──────────────────────────────────────────────────────────────────
    static bool ValidLogPath(string path)
        => !string.IsNullOrEmpty(path) && Regex.IsMatch(path, @"^[a-zA-Z0-9_./\\]+$");

    // ──────────────────────────────────────────────────────────────────
    // InvalidExit
    //
    // Prints "invalid" and exits with code 111 per spec.
    // ──────────────────────────────────────────────────────────────────
    static void InvalidExit()
    {
        Console.WriteLine("invalid");
        Environment.Exit(111);
    }
}