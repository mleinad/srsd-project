namespace GalleryCore;

public class GalleryState
{
    public int? LastTimestamp { get; private set; } = null;
    public Dictionary<string, Person> People { get; } = new();

    public void ApplyEvent(int timestamp, string name, EPersonType type, bool isArrival, int? roomId)
    {
       if (LastTimestamp.HasValue && timestamp <= LastTimestamp.Value)
       {
           throw new InvalidCommandException("Time must increase.");
       }   
       
       LastTimestamp = timestamp;

        string personId = $"{type}_{name}";
        if (!People.TryGetValue(personId, out Person? p))
        {
            p = new Person(name, type);
            People[personId] = p;
        }
        
        if (isArrival) 
        {
            if (roomId == null) // Arriving at gallery
            {
                if (p.InGallery)
                {
                    throw new InvalidCommandException("Already in the gallery.");
                }
                p.InGallery = true;
            }
            else // Arriving at room
            {
                if (!p.InGallery)
                {
                    throw new InvalidCommandException("Must enter gallery first.");
                }

                if (p.CurrentRoom != null)
                {
                    throw new InvalidCommandException("Must leave previous room first.");
                }
                p.CurrentRoom = roomId;
            }
        }
        else // Departure
        {
            if (roomId == null) // Leaving gallery
            {
                if (!p.InGallery)
                {
                    throw new InvalidCommandException("Not in the gallery.");
                }
                if (p.CurrentRoom != null)
                {
                    throw new InvalidCommandException("Must leave current room before leaving gallery.");
                }
                p.InGallery = false;
            }
            else // Leaving room
            {
                if (p.CurrentRoom != roomId)
                {
                    throw new InvalidCommandException("Cannot leave a room you are not currently in.");
                }
                p.CurrentRoom = null;
            }
        }
    }
}