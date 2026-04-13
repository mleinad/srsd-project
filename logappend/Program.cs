using GalleryCore;
using GalleryCore.IO;
using System.Text.RegularExpressions;

namespace logappend;

class Program
{
    static void Main(string[] args)
    {
        var parser = new LogParser();

        string? token         = null;
        int?    timestamp     = null;
        string? employeeName  = null;
        string? guestName     = null;
        bool    arrivalFlag   = false;
        bool    departureFlag = false;
        string? roomId        = null;
        string? logPath       = null;
        string? batchFile     = null;

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
        catch (InvalidCommandException)
        {
            InvalidExit();
        }
        catch (IntegrityViolationException)
        {
            InvalidExit();
        }
    }

    private static void RunBatch(string batchFile)
    {
        var parser = new LogParser();

        string[] lines = File.ReadAllLines(batchFile);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] lineArgs = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (Array.IndexOf(lineArgs, "-B") >= 0)
            {
                Console.WriteLine("invalid");
                continue;
            }

            try
            {
                ProcessCommand(lineArgs, parser);
            }
            catch (InvalidCommandException)
            {
                Console.WriteLine("invalid");
            }
            catch (IntegrityViolationException)
            {
                Console.WriteLine("invalid");
            }
        }
    }

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
            string GetNext() => (i + 1 < args.Length) ? args[++i] : throw new InvalidCommandException();

            switch (args[i])
            {
                case "-K":  token        = GetNext(); break;
                case "-E":  employeeName = GetNext(); break;
                case "-G":  guestName    = GetNext(); break;
                case "-R":  roomId       = GetNext(); break;
                case "-T":
                    if (!int.TryParse(GetNext(), out int ts)) throw new InvalidCommandException();
                    timestamp = ts;
                    break;
                case "-A":  arrivalFlag   = true; break;
                case "-L":  departureFlag = true; break;
                case "-B":  throw new InvalidCommandException();
                default:
                    if (!args[i].StartsWith("-")) logPath = args[i];
                    else throw new InvalidCommandException();
                    break;
            }
        }

        ValidateAndAppend(token, timestamp, employeeName, guestName,
                          arrivalFlag, departureFlag, roomId, logPath, parser);
    }

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
        if (token == null || timestamp == null || logPath == null)
            throw new InvalidCommandException("Missing required arguments.");

        if (arrivalFlag == departureFlag)
            throw new InvalidCommandException("Must specify exactly one of -A or -L.");

        if (employeeName == null && guestName == null)
            throw new InvalidCommandException("Must specify -E or -G.");

        if (employeeName != null && guestName != null)
            throw new InvalidCommandException("Cannot specify both -E and -G.");

        if (!ValidToken(token))
            throw new InvalidCommandException("Invalid token.");

        if (timestamp < 1 || timestamp > 1_073_741_823)
            throw new InvalidCommandException("Timestamp out of range.");

        string name = (employeeName ?? guestName)!;
        if (!Regex.IsMatch(name, @"^[a-zA-Z]+$"))
            throw new InvalidCommandException("Invalid name.");

        int? roomIdInt = null;
        if (roomId != null)
        {
            // int.TryParse drops leading zeros automatically (e.g. "003" → 3).
            if (!int.TryParse(roomId, out int rid) || rid < 0 || rid > 1_073_741_823)
                throw new InvalidCommandException("Invalid room ID.");
            roomIdInt = rid;
        }

        if (!ValidLogPath(logPath))
            throw new InvalidCommandException("Invalid log path.");

        List<LogEvent> history;
        byte[] lastHmac;
        try
        {
            (history, lastHmac) = parser.ReadAllEventsWithHmac(token, logPath);
        }
        catch (IntegrityViolationException)
        {
            throw;
        }

        var state = new GalleryState();
        foreach (var evt in history)
        {
            EPersonType t = evt.PersonType == "E" ? EPersonType.Employee : EPersonType.Guest;
            state.ApplyEvent(evt.Timestamp, evt.Name, t, evt.Action == "A", evt.RoomId);
        }

        EPersonType personType = employeeName != null ? EPersonType.Employee : EPersonType.Guest;
        try
        {
            state.ApplyEvent(timestamp.Value, name, personType, arrivalFlag, roomIdInt);
        }
        catch (InvalidCommandException ex)
        {
            throw new InvalidCommandException(ex.Message);
        }

        var logEvent = new LogEvent
        {
            Timestamp  = timestamp.Value,
            PersonType = employeeName != null ? "E" : "G",
            Name       = name,
            Action     = arrivalFlag ? "A" : "L",
            RoomId     = roomIdInt
        };

        try
        {
            parser.AppendEvent(logEvent, token, logPath, lastHmac);
        }
        catch (IntegrityViolationException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new InvalidCommandException("invalid log path");
        }
    }

    private static bool ValidToken(string t)
        => !string.IsNullOrEmpty(t) && Regex.IsMatch(t, @"^[a-zA-Z0-9]+$");

    private static bool ValidLogPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName)) return false;
        return Regex.IsMatch(fileName, @"^[a-zA-Z0-9_.]+$");
    }

    private static void InvalidExit()
    {
        Console.WriteLine("invalid");
        Environment.Exit(111);
    }
}