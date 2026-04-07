namespace GalleryCore;

public class Room
{
    public int Id { get; set; }
    public List<string> Occupants { get; set; } = new();

    public Room(int id)
    {
        Id = id;
    }
}