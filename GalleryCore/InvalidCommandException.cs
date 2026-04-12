namespace GalleryCore;

// Thrown when a logappend command is invalid — either because the arguments
// are malformed or because the event is inconsistent with the current gallery
// state (e.g. entering a room without first entering the gallery).
public class InvalidCommandException : Exception
{
    public string DebugMessage { get; }

    public InvalidCommandException(string debugMessage = "")
        : base("invalid") 
    { 
        DebugMessage = debugMessage;
    }
}