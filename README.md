# DbRosetta - The Universal Database Translator

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

DbRosetta is a modern, cross-platform .NET 8 library designed to read database schemas, translate them between different SQL dialects, and migrate data. It provides an extensible framework for converting from one database system to another with a focus on clean, reusable architecture.

## Table of Contents

- [Features](#features)
- [Supported Dialects](#supported-dialects)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Usage](#usage)
  - [Dependency Injection Setup](#dependency-injection-setup)
  - [Type Mapping Engine](#type-mapping-engine)
  - [Migration API](#migration-api)
- [API Reference](#api-reference)
- [Examples](#examples)
- [Testing](#testing)
- [Contributing](#contributing)
- [License](#license)
- [Acknowledgments](#acknowledgments)

## Features

- **Schema Reading**: Intelligently reads tables, columns, primary keys, indexes, foreign keys, constraints, and triggers from source databases.
- **Data Migration**: Migrates data rows between databases using a universal format with progress tracking.
- **Type Mapping Engine**: A powerful service to translate data types between different database dialects (e.g., `NVARCHAR(MAX)` in SQL Server to `TEXT` in PostgreSQL).
- **Expression Translation**: Parses and translates SQL expressions, default values, and check constraints across dialects.
- **Dependency Injection**: Modular architecture with DI support for easy testing and extensibility.
- **Extensible Dialects**: Easily add support for new databases by implementing interfaces.
- **Cross-Platform**: Built on .NET 8, the core library runs on Windows, macOS, and Linux.
- **Progress Tracking**: Real-time progress reporting during migrations with customizable handlers.

## Supported Dialects

| Database       | Schema Reading | Schema Writing | Data Migration | Type Mapping |
| :------------- | :------------: | :------------: | :------------: | :----------: |
| **SQL Server** |       ✅       |       ❌       |       ✅       |      ✅      |
| **PostgreSQL** |       ✅       |       ✅       |       ✅       |      ✅      |
| **MySQL**      |       ❌       |       ❌       |       ✅       |      ✅      |
| **SQLite**     |       ❌       |       ✅       |       ✅       |      ✅      |

_Legend: ✅ = Implemented, ❌ = Not yet implemented, 🔄 = In Progress_

## Installation

### Prerequisites

- .NET 8.0 or later
- Access to source and destination databases with appropriate permissions

### Manual Build

1. Clone the repository:

   ```bash
   git clone https://github.com/DelaRosaJose/DbRosetta.git
   cd DbRosetta
   ```

2. Build the solution:

   ```bash
   dotnet build DbRosetta.sln
   ```

3. Run the example console app:
   ```bash
   dotnet run --project DbRosetta.ExampleConsoleApp
   ```

## Quick Start

Here's a basic example of migrating from SQL Server to SQLite:

```csharp
using DbRosetta.Core.Services;

// Create migration request
var request = new MigrationRequest
{
    SourceDialect = DatabaseEngine.SqlServer,
    SourceConnectionString = "Server=localhost;Database=MyDB;Trusted_Connection=True;",
    DestinationDialect = DatabaseEngine.SQLite,
    DestinationConnectionString = @"C:\Output\MyDB.sqlite"
};

// Setup services
var progressHandler = new ConsoleProgressHandler();
var factory = new DatabaseProviderFactory();
var dataMigrator = new DataMigratorService(factory, logger);
var migrationService = new MigrationService(progressHandler, factory, dataMigrator);

// Execute migration
await migrationService.ExecuteAsync(request);
```

## Usage

### Dependency Injection Setup

DbRosetta.Core is designed with Dependency Injection in mind. Register the services in your DI container:

```csharp
// In your Program.cs or Startup.cs
builder.Services.AddSingleton<DatabaseProviderFactory>();
builder.Services.AddTransient<IDataMigrator, DataMigratorService>();
builder.Services.AddTransient<MigrationService>();
builder.Services.AddTransient<IMigrationProgressHandler, ConsoleProgressHandler>();
```

Then inject and use:

```csharp
public class MyMigrationController
{
    private readonly MigrationService _migrationService;

    public MyMigrationController(MigrationService migrationService)
    {
        _migrationService = migrationService;
    }

    public async Task MigrateDatabase(MigrationRequest request)
    {
        await _migrationService.ExecuteAsync(request);
    }
}
```

### Type Mapping Engine

To use the core type mapping engine directly:

```csharp
// 1. Register all the database dialects you support
var dialects = new List<IDatabaseDialect>
{
    new SqlServerDialect(),
    new PostgreSqlDialect(),
    new MySqlDialect(),
    new SQLiteDialect()
};

var typeService = new TypeMappingService(dialects);

// 2. Define a source column
var sqlServerColumn = new DbColumnInfo { TypeName = "nvarchar", Length = 255 };

// 3. Translate its type to another dialect
string sqliteType = typeService.TranslateType(sqlServerColumn, "SqlServer", "SQLite");

// Result: sqliteType will be "TEXT"
Console.WriteLine($"SQL Server 'nvarchar(255)' becomes '{sqliteType}' in SQLite.");
```

### Migration API

The `MigrationService` provides a comprehensive API for database migrations:

```csharp
public class MigrationRequest
{
    public DatabaseEngine SourceDialect { get; set; }
    public DatabaseEngine DestinationDialect { get; set; }
    public required string SourceConnectionString { get; set; }
    public required string DestinationConnectionString { get; set; }
}
```

Supported database engines:

- `DatabaseEngine.SqlServer`
- `DatabaseEngine.PostgreSql`
- `DatabaseEngine.SQLite`
- `DatabaseEngine.MySQL`

## API Reference

### Core Interfaces

- `IDatabaseDialect`: Defines dialect-specific type mapping
- `IDatabaseSchemaReader`: Reads schema information from databases
- `IDatabaseSchemaWriter`: Writes schema to target databases
- `IDataMigrator`: Handles data migration between databases
- `IMigrationProgressHandler`: Reports migration progress

### Key Classes

- `MigrationService`: Main service for executing migrations
- `DatabaseProviderFactory`: Factory for creating database-specific components
- `TypeMappingService`: Handles type translations between dialects
- `DataMigratorService`: Implements data migration logic

### Models

- `MigrationRequest`: Configuration for migration operations
- `TableSchema`: Represents table structure with columns, constraints, etc.
- `ColumnSchema`: Column definition with type, constraints, and defaults
- `ForeignKeySchema`: Foreign key relationship definitions

## Examples

### Console Application

The repository includes a console application (`DbRosetta.ExampleConsoleApp`) that demonstrates basic migration functionality. See [`Program.cs`](DbRosetta.ExampleConsoleApp/Program.cs) for implementation details.

### ASP.NET Core Integration

```csharp
[ApiController]
[Route("api/migration")]
public class MigrationController : ControllerBase
{
    private readonly MigrationService _migrationService;

    public MigrationController(MigrationService migrationService)
    {
        _migrationService = migrationService;
    }

    [HttpPost]
    public async Task<IActionResult> Migrate([FromBody] MigrationRequest request)
    {
        try
        {
            await _migrationService.ExecuteAsync(request);
            return Ok("Migration completed successfully");
        }
        catch (Exception ex)
        {
            return BadRequest($"Migration failed: {ex.Message}");
        }
    }
}
```

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue.

### Development Setup

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Make your changes and add tests
4. Ensure all tests pass: `dotnet test`
5. Submit a pull request

### Code Style

- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments
- Keep methods focused and single-purpose

### Adding New Dialects

To add support for a new database dialect:

1. Implement `IDatabaseDialect` and `IDatabaseTypeMapper`
2. Create schema reader implementing `IDatabaseSchemaReader`
3. Create schema writer implementing `IDatabaseSchemaWriter`
4. Add data reader/writer implementing `IDataReader`/`IDataWriter`
5. Register the new dialect in `DatabaseProviderFactory`

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built with .NET 8 for cross-platform compatibility
- Uses Microsoft.Data.SqlClient, Npgsql, and Microsoft.Data.Sqlite for database connectivity
- Inspired by the need for seamless database migrations in enterprise environments
