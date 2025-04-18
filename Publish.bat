#Running this script will Build the app and place it's contents within the ".\Advanced-Windows-Diagnostics\bin\Release\net8.0\win-x64\publish\" folder.
#Finally Copy the "Publish" folder itself to another location and then rename/move it to where you would like to store the app.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true


#GENERAL OPERATING PROCEDURE
#Run the WinDiagAdvanced.exe with Administrator privaleges and then open the Display.html, then import the generated JSON file located in the apps "Reports" subfolder.
#Reap the benefits of extensive easy to read diagnostics of the computer all in one place.