using System;

class Program
{
    static void Main(string[] args)
    {
        /*
        Console.Write("Enter commad 1 (string): ");
        string name = Console.ReadLine();

        Console.Write("Enter commad 2 (int):");
        int age = int.Parse(Console.ReadLine());

        Console.WriteLine("Command 1 " + name + "!");
        Console.WriteLine("Command 2 " + age + "!");
        */

        //////////////////////////////////////////////////////////// Ideia do Matias a partir daqui vvv

        bool tokenFlag = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-K":
                    // Do whatever you need to do for -K
                    tokenFlag = true; // This one is mandatory so check if its included
                    break;

                case "-T":
                    // Do whatever you need to do for -T
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
        
        Console.WriteLine($"The user {(tokenFlag ? "provided" : "did NOT provide")} a token");
    }
}