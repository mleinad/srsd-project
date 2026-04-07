using GalleryCore;
using GalleryCore.IO;
using System.Text.RegularExpressions;

namespace logappend;
class Program
{
    static void Main(string[] args)
    {
        GalleryState galleryState = new GalleryState();
        galleryState.CheckState();
        
        var parser = new LogParser();
        
        bool tokenFlag = false;
        bool validLog = true;

        int timestamp = 0;
        string? employee_name= null;
        string? guest_name = null;
        bool arrival_flag= false;
        bool departure_flag = false;
        string? room_id= null;
        bool log = false;
        


        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-K":
                    // Do whatever you need to do for -K
                    if (ValidToken(args[i+1]))
                        tokenFlag = true; // This one is mandatory so check if its included
                    break;
                
                case "-T":
                    timestamp = int.Parse(args[i+1]);
                    //Check if timestamp > lastRecordedTimestamp
                    break;

                case "-E":
                    employee_name = args[i + 1];
                    if (guest_name != null)
                        validLog = false;
                    validLog = Regex.IsMatch(employee_name, @"^[a-zA-Z0-9]+$");
                    break;
                
                case "-G":
                    guest_name = args[i+1];
                    if (employee_name != null)
                        validLog = false;
                    validLog = Regex.IsMatch(guest_name, @"^[a-zA-Z0-9]+$");
                    break;
                
                case "-A":
                    arrival_flag = true;
                    if (departure_flag == true)
                        validLog = false;
                    break;
                
                case "-L":
                    departure_flag = true;
                    if (arrival_flag == true)
                        validLog = false;
                    break;
                
                case "-R":
                    room_id = args[i+1];
                    break;
                
                case "log":
                    log = true;
                    break;
                
                case "-B":
                    // Do whatever you need to do for -B
                    //valueB = args[++i]; // if -b expects a value after it
                    break;

                default:
                    Console.WriteLine($"Unknown flag: {args[i]}");
                    break;
            }
        }

        if (!validLog)
        {
            Console.Error.WriteLine("The arguments provided are invalid.");
            Environment.Exit(111);
        }

        Console.WriteLine($"The user {(tokenFlag ? "provided" : "did NOT provide")} a token");
    }
    
    static bool ValidToken(string t)
    {
        //TODO
    }
    
    static bool ValidLog(string t)
    {
        //TODO
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
    }
}

