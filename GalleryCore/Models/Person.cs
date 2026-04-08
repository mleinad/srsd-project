namespace GalleryCore;

public class Person
{
    public string Name { get; }
    public EPersonType Type { get; }
    
    public string Id => $"{Type}_{Name}"; 

    public bool InGallery { get; set; } = false;
    public int? CurrentRoom { get; set; } = null;
    
    public List<int> RoomHistory { get; } = new();

    public Person(string name, EPersonType type)
    {
        Name = name;
        Type = type;
    }
}