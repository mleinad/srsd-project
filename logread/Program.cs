using GalleryCore;

namespace logread;

class Program
{
    static void Main(string[] args)
    {
        GalleryState galleryState = new GalleryState();
        galleryState.CheckState();
        Console.WriteLine("Hello, World!");
    }
}