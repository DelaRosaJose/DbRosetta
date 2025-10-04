using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DbRosetta.Core;
using DbRosetta.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbRosetta.Native;

// Delegate definitions for the function pointers we expect from the host
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void LogCallback(IntPtr message);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void ProgressCallback(IntPtr tableName, int rows);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void FinishedCallback();

public static class Exports
{

    // Este constructor estático se ejecuta una sola vez cuando la DLL se carga.
    // Es el lugar perfecto para la inicialización que afecta a toda la librería.
    static Exports()
    {
        SQLitePCL.Batteries.Init();
    }

    // This is the single function exported from the native library.
    // The EntryPoint string must match what the host uses to import it.
    [UnmanagedCallersOnly(EntryPoint = "start_migration")]
    public static void StartMigration(
        IntPtr requestJsonPtr,
        IntPtr onLogPtr,
        IntPtr onProgressPtr,
        IntPtr onFinishedPtr)
    {
        // Run the entire migration on a background thread to avoid
        // blocking the caller. This is essential for FFI.
        Task.Run(() =>
        {
            var onFinished = Marshal.GetDelegateForFunctionPointer<FinishedCallback>(onFinishedPtr);
            try
            {
                // Convert the IntPtrs back into callable C# delegates
                var onLog = Marshal.GetDelegateForFunctionPointer<LogCallback>(onLogPtr);
                var onProgress = Marshal.GetDelegateForFunctionPointer<ProgressCallback>(onProgressPtr);

                // Read the JSON string from the native pointer
                string requestJsonString = Marshal.PtrToStringUTF8(requestJsonPtr)!;


                var request = JsonSerializer.Deserialize(requestJsonString, AppJsonSerializerContext.Default.MigrationRequest)!;

                // Set up and run the migration
                var handler = new FfiProgressHandler(onLog, onProgress);
                var factory = new DatabaseProviderFactory();
                var logger = NullLogger<DataMigratorService>.Instance;
                var dataMigrator = new DataMigratorService(factory, logger);
                var migrationService = new MigrationService(handler, factory, dataMigrator);
                migrationService.ExecuteAsync(request).Wait();

                handler.SendSuccessAsync("✅ Migration completed successfully!").Wait();
            }
            catch (Exception e)
            {
                // If anything fails, try to send an error message back
                if (onLogPtr != IntPtr.Zero)
                {
                    var onLog = Marshal.GetDelegateForFunctionPointer<LogCallback>(onLogPtr);
                    using var handler = new FfiProgressHandler(onLog, null);
                    handler.SendFailureAsync(e.Message, e.ToString()).Wait();
                }
            }
            finally
            {
                // Always call the 'onFinished' callback to signal completion
                onFinished();
            }
        });
    }
}

// Helper class to manage marshalling strings and invoking the native callbacks
internal class FfiProgressHandler : IMigrationProgressHandler, IDisposable
{
    private readonly LogCallback? _onLog;
    private readonly ProgressCallback? _onProgress;

    public FfiProgressHandler(LogCallback? onLog, ProgressCallback? onProgress)
    {
        _onLog = onLog;
        _onProgress = onProgress;
    }

    // Safely converts a C# string to a native UTF-8 pointer and invokes the callback
    private unsafe void InvokeLogCallback(string message)
    {
        if (_onLog == null) return;

        int byteCount = Encoding.UTF8.GetByteCount(message);
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)byteCount + 1); // +1 for null terminator
        try
        {
            fixed (char* pMessage = message)
            {
                Encoding.UTF8.GetBytes(pMessage, message.Length, buffer, byteCount);
            }
            buffer[byteCount] = 0; // Null terminate
            _onLog((IntPtr)buffer);
        }
        finally
        {
            NativeMemory.Free(buffer); // Clean up the unmanaged memory
        }
    }

    // Same helper logic for the progress callback
    private unsafe void InvokeProgressCallback(string tableName, int rows)
    {
        if (_onProgress == null) return;

        int byteCount = Encoding.UTF8.GetByteCount(tableName);
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)byteCount + 1);
        try
        {
            fixed (char* pTableName = tableName)
            {
                Encoding.UTF8.GetBytes(pTableName, tableName.Length, buffer, byteCount);
            }
            buffer[byteCount] = 0;
            _onProgress((IntPtr)buffer, rows);
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    // --- IMigrationProgressHandler Implementation ---
    public Task SendLogAsync(string message)
    {
        InvokeLogCallback(message);
        return Task.CompletedTask;
    }

    public Task SendProgressAsync(string tableName, int rows)
    {
        InvokeProgressCallback(tableName, rows);
        return Task.CompletedTask;
    }

    public Task SendWarningAsync(string message) => SendLogAsync($"WARN: {message}");
    public Task SendSuccessAsync(string message) => SendLogAsync(message);
    public Task SendFailureAsync(string errorMessage, string? stackTrace) => SendLogAsync($"FAIL: {errorMessage}\n{stackTrace}");

    public void Dispose() { }
}