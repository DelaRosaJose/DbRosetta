# DbRosetta - The Universal Database Translator Rosetta

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com)
[![NuGet](https://img.shields.io/nuget/v/DbRosetta.Core.svg)](https://www.nuget.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

DbRosetta is a modern, cross-platform .NET 8 library designed to read database schemas, translate them between different SQL dialects, and migrate data. It provides an extensible framework for converting from one database system to another with a focus on clean, reusable architecture.

## Core Features

- **Schema Reading**: Intelligently reads tables, columns, primary keys, indexes, and foreign keys.
- **Data Migration**: Migrates data rows between databases using a universal format.
- **Type Mapping Engine**: A powerful service to translate data types between different database dialects (e.g., `NVARCHAR(MAX)` in SQL Server to `TEXT` in PostgreSQL).
- **Dependency Injection**: Modular architecture with DI support for easy testing and extensibility.
- **Extensible Dialects**: Easily add support for new databases by implementing interfaces.
- **Cross-Platform**: Built on .NET 8, the core library runs on Windows, macOS, and Linux.

## Supported Dialects

| Database       | Schema Reading | Schema Writing | Type Mapping |
| :------------- | :------------: | :------------: | :----------: |
| **SQL Server** |  In Progress   |    Planned     |   ✅ Done    |
| **PostgreSQL** |    Planned     |    Planned     |   ✅ Done    |
| **MySQL**      |    Planned     |    Planned     |   ✅ Done    |
| **SQLite**     |    Planned     |  In Progress   |   ✅ Done    |

## Project Architecture

The solution is divided into a few key projects:

- `DbRosetta.Core`: The core class library containing all the cross-platform logic. This is the main package for developers.
- `DbRosetta.Cli`: A command-line interface for performing migrations.
- `DbRosetta.WinForms`: A Windows-only GUI for users who prefer a graphical interface.

## Getting Started

### Using Dependency Injection

DbRosetta.Core is designed with Dependency Injection in mind. Register the services in your DI container:

```csharp
// In your Program.cs or Startup.cs
builder.Services.AddSingleton<DatabaseProviderFactory>();
builder.Services.AddTransient<IDataMigrator, DataMigratorService>();
builder.Services.AddTransient<MigrationService>();
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

    // Use _migrationService to perform migrations
}
```

### Using the Type Mapping Engine

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

Roadmap

Core type mapping engine for SQL Server, PostgreSQL, MySQL, SQLite.

Complete SqlServerSchemaReader.

Implement SQLiteWriter.

Implement schema readers and writers for PostgreSQL and MySQL.

Add data migration capabilities.

Publish to NuGet.
Contributing
Contributions are welcome! Please feel free to submit a pull request or open an issue.
License
This project is licensed under the MIT License - see the LICENSE.md file for details.
```
