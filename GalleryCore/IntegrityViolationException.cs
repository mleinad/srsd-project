namespace GalleryCore;

// Thrown when the log file has been modified without the correct token,
// or when the provided token does not match the original token of the log.
public class IntegrityViolationException : Exception
{
    public IntegrityViolationException()
        : base("integrity violation") { }
}