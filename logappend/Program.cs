using GalleryCore;
using GalleryCore.IO;
using System.Text.RegularExpressions;

namespace logappend;
class Program
{
    static void Main(string[] args)
    {
        var parser = new LogParser();
        
        bool tokenFlag = false;
        bool validLog = true;

        int timestamp = 0;
        string? employee_name= null;
        string? guest_name = null;
        bool arrival_flag= false;
        bool departure_flag = false;
        string? room_id= null;
        string? log_file = null;
        


        for (int i = 0; i < args.Length; i++)
        {
            // Helper to safely get the next argument without crashing
            string GetNext() => (i + 1 < args.Length) ? args[++i] : null;
            
            switch (args[i])
            {
                case "-K":
                    string token = GetNext();
                    if (token != null && Regex.IsMatch(token, "^[a-zA-Z0-9]+$")) // Tokens are alphanumeric
                    {
                        tokenFlag = true;
                    }
                    else
                    {
                        validLog = false;
                    }
                    break;
                
                case "-T":
                    string tStr = GetNext();
                    if (tStr == null || !int.TryParse(tStr, out timestamp) || timestamp < 1 || timestamp > 1073741823)
                    {
                        validLog = false; // Must be valid integer in range
                    }
                    break;

                case "-E":
                    employee_name = GetNext();
                    if (employee_name == null || guest_name != null || !Regex.IsMatch(employee_name, "^[a-zA-Z]+$"))
                    {
                        validLog = false; // Fixed regex: alphabetic only
                    }
                    break;
                
                case "-G":
                    guest_name = GetNext();
                    if (guest_name == null || employee_name != null || !Regex.IsMatch(guest_name, "^[a-zA-Z]+$")) {
                        validLog = false; // Fixed regex: alphabetic only
}
                    break;
                
                case "-A":
                    arrival_flag = true;
                    if (departure_flag)
                    {
                        validLog = false;
                    }
                    break;
                
                case "-L":
                    departure_flag = true;
                    if (arrival_flag)
                    {
                        validLog = false;
                    }
                    break;
                
                case "-R":
                    room_id = GetNext();
                    if (room_id == null || !Regex.IsMatch(room_id, "^[0-9]+$"))
                    {
                        validLog = false;
                    }
                    break;
                
                case "-B":
                    // Batch processing
                    string batch_file = GetNext();
                    if (batch_file == null)
                    {
                        validLog = false;
                    }
                    break;
                
                default:
                    // If it doesn't start with '-', it's the log file name
                    if (!args[i].StartsWith("-") && log_file == null)
                    {
                        log_file = args[i];
                    }
                    else
                    {
                        validLog = false; // Unknown flag or multiple log files
                    }

                    break;
            }
        }
        
        if (log_file == null || !tokenFlag || (!arrival_flag && !departure_flag) || (employee_name == null && guest_name == null))
        {
            validLog = false;
        }

        if (!validLog)
        {
            Console.WriteLine("invalid"); // MUST be exactly this string
            Environment.Exit(111);        // MUST exit 111
        }
        
        try
        {
            GalleryState state = new GalleryState();
            
            // TODO: Read existing log_file, decrypt, and rebuild state history first!
            
            EPersonType type;
            string name;

            if (employee_name != null)
            {
                type = EPersonType.Employee;
                name = employee_name;
            }
            else
            {
                type = EPersonType.Guest;
                name = guest_name;
            }
            
            //NOTE: I think we could do some sort of exploit here with allowing a user to be an EmployeeGuest or something  
            int? parsedRoomId = null;
            if (room_id != null)
            {
                parsedRoomId = int.Parse(room_id);
            }

            state.ApplyEvent(timestamp, name, type, arrival_flag, parsedRoomId);
            // TODO: Write encrypted event to log_file
            
            Environment.Exit(0); // Success!
        }
        catch (Exception)
        {
            // If ApplyEvent throws an exception (e.g. time didn't increase, person isn't in gallery)
            Console.WriteLine("invalid");
            Environment.Exit(111);
        }
    }
    
    static bool ValidToken(string t)
    {
        //TODO
        return true;
    }
    
    static bool ValidLog(string t)
    {
        //TODO
        return true;
    }
    
    static LogEvent LogGenerator
    (
        int timestamp,
        string employee_name,
        string guest_name,
        bool arrival_flag,
        bool departure_flag,
        string room_id,
        bool log
    )
    {
        //TODO
        return null;
    }
}

