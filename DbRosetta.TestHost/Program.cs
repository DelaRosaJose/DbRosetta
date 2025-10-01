using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// --- STEP 1: Define the contracts to match the native library ---

internal static class NativeMethods
{
    // The name of the library without extensions. .NET handles .dll/.so/.dylib.
    private const string LibraryName = "DbRosetta.Native";

    // Import the function using P/Invoke. The signature must match exactly.
    [DllImport(LibraryName, EntryPoint = "start_migration", CallingConvention = CallingConvention.Cdecl)]
    public static extern void StartMigration(
        IntPtr requestJsonPtr,
        IntPtr onLogPtr,
        IntPtr onProgressPtr,
        IntPtr onFinishedPtr);
}

// Re-define the delegates for the callbacks
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void LogCallback(IntPtr message);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void ProgressCallback(IntPtr tableName, int rows);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void FinishedCallback();


public static class Program
{
    // Use an event to wait for the async migration to finish
    private static readonly ManualResetEventSlim _migrationFinished = new();

    public static void Main(string[] args)
    {
        Console.WriteLine("--- C# Native AOT Host Test (Decoupled) ---");

        // --- STEP 2: Create C# methods for the callbacks ---
        LogCallback onLog = messagePtr => Console.WriteLine($"[LOG]: {Marshal.PtrToStringUTF8(messagePtr)}");
        ProgressCallback onProgress = (tableNamePtr, rows) => Console.WriteLine($"[PROGRESS]: {Marshal.PtrToStringUTF8(tableNamePtr)} -> {rows} rows");
        FinishedCallback onFinished = () =>
        {
            Console.WriteLine("[FINISHED]: Migration is complete. Unblocking main thread.");
            _migrationFinished.Set(); // Signal that the work is done
        };

        // Get function pointers that can be passed to the native code
        IntPtr onLogPtr = Marshal.GetFunctionPointerForDelegate(onLog);
        IntPtr onProgressPtr = Marshal.GetFunctionPointerForDelegate(onProgress);
        IntPtr onFinishedPtr = Marshal.GetFunctionPointerForDelegate(onFinished);

        IntPtr requestJsonPtr = IntPtr.Zero;

        try
        {
            // --- STEP 3: Manually prepare the JSON request string ---
            // We know the contract: SqlServer=0, SQLite=1
            int sourceType = 0;
            int destinationType = 1;

            // IMPORTANT: Adjust these connection strings for your environment
            string sourceConnectionString = "Server=MSI\\SQLEXPRESS;Database=AdventureWorks2014;Trusted_Connection=True;TrustServerCertificate=True;";
            string destinationConnectionString = "Data Source=AdventureWorks_Test_From_Host.db";

            string requestJson = $@"{{
        ""SourceType"": {sourceType},
        ""DestinationType"": {destinationType},
        ""SourceConnectionString"": ""{sourceConnectionString.Replace(@"\", @"\\")}"",
        ""DestinationConnectionString"": ""{destinationConnectionString}""
    }}";

            // Marshall the C# string to a native UTF-8 string pointer
            requestJsonPtr = Marshal.StringToCoTaskMemUTF8(requestJson);
            Console.WriteLine($"Sending JSON: {requestJson}");

            // --- STEP 4: Call the native function ---
            Console.WriteLine("\nCalling native function 'start_migration'...");
            NativeMethods.StartMigration(requestJsonPtr, onLogPtr, onProgressPtr, onFinishedPtr);

            // --- STEP 5: Wait for completion ---
            Console.WriteLine("Main thread is now waiting for the migration to finish...\n");
            _migrationFinished.Wait(); // Block here until onFinished() is called
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            // --- STEP 6: Clean up unmanaged memory ---
            if (requestJsonPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(requestJsonPtr);
            }
        }

        // Prevent the GC from collecting the delegates while the native code is running
        GC.KeepAlive(onLog);
        GC.KeepAlive(onProgress);
        GC.KeepAlive(onFinished);

        Console.WriteLine("\n--- Test Host Finished ---");
    }
}