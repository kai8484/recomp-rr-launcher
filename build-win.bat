dotnet publish WheelWizard/WheelWizard.csproj -r win-x64 -c Release /p:UseAppHost=true /p:PublishSingleFile=true /p:IncludeAllContentForSelfExtract=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false --self-contained true -o "!launcher"
copy /Y "!launcher\WheelWizard.exe" "!launcher\Wheel Wizard.exe"
