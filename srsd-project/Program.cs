using System;

class Program
{
    static void Main(string[] args)
    {
        Console.Write("Enter commad 1 (string): ");
        string name = Console.ReadLine();

        Console.Write("Enter commad 2 (int):");
        int age = int.Parse(Console.ReadLine());

        Console.WriteLine("Command 1 " + name + "!");
        Console.WriteLine("Command 2 " + age + "!");
    }
}