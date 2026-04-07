How to run the program using .NET:

1. Install the .NET SDK from dotnet.microsoft.com/download
2. Go to program.cs directory
3. Build file with 'dotnet build -o <build_folder>'
4. Run with <build_folder>/srsd-project.exe [flags]


In the future, we should build with publish to that it can run without .NET:


For Windows:
  dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -o ./build/windows
  
Output: ./build/windows/YourApp.exe

For linux:
  Ubuntu / Debian / Fedora (most common)
    dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./build/linux
  Raspberry Pi / ARM servers
    dotnet publish -r linux-arm64 --self-contained -p:PublishSingleFile=true -o ./build/linux-arm
    
  Output: ./build/linux*/YourApp

For Mac:
  Apple Silicon (M1 / M2 / M3)
    dotnet publish -r osx-arm64 --self-contained -p:PublishSingleFile=true -o ./build/mac-arm
    
  Intel Mac
    dotnet publish -r osx-x64 --self-contained -p:PublishSingleFile=true -o ./build/mac-intel
    
  Output: ./build/mac-*/YourApp
