namespace GalleryCore;

public class Person
{
    public string Name       { get; set; }
    public string Type       { get; set; }  // "E" = Employee | "G" = Guest
    public bool   InGallery  { get; set; } = false;
    public int?   CurrentRoom { get; set; } = null;

    public Person(string name, string type)
    {
        Name = name;
        Type = type;
    }
}