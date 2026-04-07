namespace GalleryCore;

// Thrown when the log file has been modified without the correct token,
// or when the provided token does not match the original token of the log.
public class IntegrityViolationException : Exception
{
    public IntegrityViolationException()
        : base("integrity violation") { }
}

public class InvalidCommandException : Exception
{
    // The base message is strictly "invalid" to match stdout requirements.
    public string DebugMessage { get; }

    public InvalidCommandException(string debugMessage = "")
        : base("invalid") 
    { 
        DebugMessage = debugMessage;
    }
}