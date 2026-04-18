using System.Globalization;
using MySqlConnector;

namespace MainApi.Data;

internal static class DbValueReader
{
    public static DateTime ReadUtcDateTime(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return DateTime.MinValue;
        }

        var value = reader.GetValue(ordinal);
        if (value is DateTime dateTime)
        {
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.UtcDateTime;
        }

        if (value is string text)
        {
            return DateTime.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        return DateTime.SpecifyKind(
            Convert.ToDateTime(value, CultureInfo.InvariantCulture),
            DateTimeKind.Utc);
    }
}
