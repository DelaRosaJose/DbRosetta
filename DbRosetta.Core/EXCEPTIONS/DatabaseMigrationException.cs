namespace DbRosetta.Core
{
    /// <summary>
    /// Exception thrown when a database migration operation fails.
    /// </summary>
    public class DatabaseMigrationException : Exception
    {
        public DatabaseMigrationException() { }

        public DatabaseMigrationException(string message) : base(message) { }

        public DatabaseMigrationException(string message, Exception innerException) : base(message, innerException) { }
    }
}