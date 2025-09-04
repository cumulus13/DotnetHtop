.NET Process Monitor (like htop)
This document provides a comprehensive guide for setting up and running your new .NET console application. This project is a simple process monitor that displays real-time CPU and memory usage, similar to htop on Linux. It's a great project for getting comfortable with C#.

Prerequisites
You need the following installed on your machine:

The .NET 8.0 SDK: You can download it from the official Microsoft website.

A code editor like Visual Studio Code or Visual Studio.

Step 1: Create the Project
Open your terminal or command prompt and run the following command to create a new console project. The command dotnet new console creates the project, and -o DotnetHtop specifies the name of the new folder and project.

dotnet new console -o DotnetHtop

Step 2: Add the Required Package
Navigate into your newly created project folder and add the System.Diagnostics.PerformanceCounter package. This is necessary to access system information like total memory.

cd DotnetHtop
dotnet add package System.Diagnostics.PerformanceCounter

Step 3: Add the Code Files
You will see a file named Program.cs. Replace the entire content of this file with the C# code provided in the Program.cs file block.

Create a new file in the same folder called config.json. Copy the content from the config.json file block into this new file.

Your project folder should now look like this:

DotnetHtop/
├── Program.cs
├── DotnetHtop.csproj
└── config.json

Step 4: Understanding the Code
The application is written in a single file (Program.cs) for simplicity. Here’s a breakdown of what the code does:

Config Class: This class defines the structure for reading settings from config.json. It includes thresholds and colors for CPU and memory usage. The ColorMapping class helps associate a color name (like "Red") with an actual ConsoleColor.

Program Class: This is the main entry point of the application.

Configuration Loading: It first tries to load the config.json file. If the file is not found, it uses default values.

Consistent Display: Instead of clearing the screen, the program now uses Console.SetCursorPosition() to update the process list in place, which prevents the flickering effect.

Main Loop: The code runs in an infinite while loop to continuously refresh the process list.

Process Collection: Inside the loop, it uses Process.GetProcesses() to get a list of all running processes.

CPU Usage Calculation: Calculating CPU usage requires taking two measurements. The code captures process times at two different moments, waits for a second, and then calculates the percentage change. This gives a more accurate, real-time value.

Sorting: It listens for user input (C, M, A, D, Q) to sort the processes by CPU or memory, in ascending or descending order.

Display Logic: It iterates through the sorted list of processes. For each process, it checks its CPU and memory usage against the configured thresholds and applies the correct background and foreground colors to the console row. It also formats the output to be easy to read.

config.json: This file allows you to easily change the color scheme and performance thresholds without editing the code.

Step 5: Run the Application
With the files in place, you can now run the application from your terminal. Since you might encounter "Access is denied" errors, it's best to run the application with administrator privileges to ensure it can access all process information.

dotnet run

The console will clear, and you will see the list of processes with their real-time usage. Use the key commands listed at the top of the application to sort the output. To exit, just press Q.

Step 6: Build
dotnet publish -c Release

* **`dotnet publish`**: The command to publish the application.
* **`-c Release`**: This creates a release build, which is optimized for performance and smaller size.
* **`--self-contained`**: This flag is not needed anymore when you specify a `RuntimeIdentifier` in your `.csproj` file, as it's the default behavior. The `RuntimeIdentifier` will automatically include the .NET runtime with the application.

After the command runs, navigate to the `bin/Release/net8.0/<your-rid>/publish` folder. For example, on Windows, the path would be `bin/Release/net8.0/win-x64/publish`. Inside this folder, you will find a single executable file named `DotnetHtop.exe` with your icon!

I hope this helps you get started with .NET! Feel free to ask if you have any more questions about the code or the development process.

## Author
[Hadi Cahyadi](mailto:cumulus13@gmail.com)