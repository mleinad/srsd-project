# =============================================================================
#  Makefile — Gallery Log (SRSD 2025/2026)
#
#  Usage:
#    make          → compile and produce logappend and logread executables
#    make clean    → remove all build artefacts
# =============================================================================

all:
	dotnet publish logappend/logappend.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o build/logappend
	dotnet publish logread/logread.csproj     -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o build/logread

clean:
	dotnet clean srsd-project.sln
	rm -rf build/
	rm -rf logappend/bin/ logappend/obj/
	rm -rf logread/bin/   logread/obj/
	rm -rf GalleryCore/bin/ GalleryCore/obj/

.PHONY: all clean