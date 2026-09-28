using Microsoft.Data.Sqlite;

namespace ELORA.Web.Data.Repositories;

internal static class ReaderExtensions
{
    public static string Str(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);

    public static string? StrOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static int Int(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);

    public static int? IntOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static long Long(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);

    public static decimal Decimal(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetDouble(ordinal));

    public static bool Bool(this SqliteDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && reader.GetInt32(ordinal) != 0;
}

internal static class CommandExtensions
{
    public static SqliteCommand With(this SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
