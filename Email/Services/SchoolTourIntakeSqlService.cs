using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Email.Services
{
    public sealed class SchoolTourIntakeSqlService : ISchoolTourIntakeService
    {
        private const string TableName = "dbo.SchoolTourOneOffIntakeTemp";

        private static readonly string[] SaveColumns = BuildSaveColumns();
        private static readonly string InsertSql = BuildInsertSql();
        private static readonly string UpdateSql = BuildUpdateSql();

        private readonly SqlConnectionFactory _connectionFactory;

        public SchoolTourIntakeSqlService(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<SchoolTourIntakeListItem>> GetRecentAsync(int maxRows = 100, CancellationToken ct = default)
        {
            var safeMaxRows = maxRows <= 0 ? 100 : Math.Min(maxRows, 500);
            var list = new List<SchoolTourIntakeListItem>();

            using var conn = _connectionFactory.CreateOpenConnection();
            await EnsureTableExistsAsync(conn, ct);

            var sql = $@"
SELECT TOP (@MaxRows) Id, RecordName, IsActive, UpdatedAt, UpdatedBy
FROM {TableName}
ORDER BY UpdatedAt DESC, Id DESC;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@MaxRows", SqlDbType.Int) { Value = safeMaxRows });
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new SchoolTourIntakeListItem
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    RecordName = ReadNullableString(reader, "RecordName") ?? $"Record {reader.GetInt32(reader.GetOrdinal("Id"))}",
                    IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                    UpdatedAtUtc = ReadNullableDate(reader, "UpdatedAt"),
                    UpdatedBy = ReadNullableString(reader, "UpdatedBy")
                });
            }

            return list;
        }

        public async Task<SchoolTourIntakeFormData?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0)
            {
                return null;
            }

            using var conn = _connectionFactory.CreateOpenConnection();
            await EnsureTableExistsAsync(conn, ct);

            var sql = $"SELECT * FROM {TableName} WHERE Id = @Id;";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            var form = SchoolTourIntakeDefaults.CreateNewForm();
            form.Id = reader.GetInt32(reader.GetOrdinal("Id"));
            form.RecordName = ReadNullableString(reader, "RecordName") ?? form.RecordName;
            form.IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"));
            form.Notes = ReadNullableString(reader, "Notes");
            form.UpdatedBy = ReadNullableString(reader, "UpdatedBy");
            form.CreatedAtUtc = ReadNullableDate(reader, "CreatedAt");
            form.UpdatedAtUtc = ReadNullableDate(reader, "UpdatedAt");

            foreach (var textField in form.TextFields)
            {
                var slot = textField.Slot.ToString("00", CultureInfo.InvariantCulture);
                textField.Label = ReadNullableString(reader, $"TextLabel{slot}") ?? textField.Label;
                textField.Value = ReadNullableString(reader, $"TextValue{slot}");
            }

            foreach (var dropdownField in form.DropdownFields)
            {
                var slot = dropdownField.Slot.ToString("00", CultureInfo.InvariantCulture);
                dropdownField.Label = ReadNullableString(reader, $"DropdownLabel{slot}") ?? dropdownField.Label;
                dropdownField.OptionsCsv = ReadNullableString(reader, $"DropdownOptions{slot}") ?? dropdownField.OptionsCsv;
                dropdownField.Value = ReadNullableString(reader, $"DropdownValue{slot}");
            }

            foreach (var dateRange in form.DateRangeFields)
            {
                var slot = dateRange.Slot.ToString("00", CultureInfo.InvariantCulture);
                dateRange.Label = ReadNullableString(reader, $"DateRangeLabel{slot}") ?? dateRange.Label;
                dateRange.StartDate = ReadNullableDate(reader, $"DateRangeStart{slot}");
                dateRange.EndDate = ReadNullableDate(reader, $"DateRangeEnd{slot}");
            }

            foreach (var dateField in form.DateFields)
            {
                var slot = dateField.Slot.ToString("00", CultureInfo.InvariantCulture);
                dateField.Label = ReadNullableString(reader, $"DateLabel{slot}") ?? dateField.Label;
                dateField.Value = ReadNullableDate(reader, $"DateValue{slot}");
            }

            return form;
        }

        public async Task<int> SaveAsync(SchoolTourIntakeFormData form, string? updatedBy, CancellationToken ct = default)
        {
            if (form is null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            var normalized = NormalizeForm(form);
            var safeUpdatedBy = TrimOrNull(updatedBy) ?? TrimOrNull(normalized.UpdatedBy);

            using var conn = _connectionFactory.CreateOpenConnection();
            await EnsureTableExistsAsync(conn, ct);
            using var tx = conn.BeginTransaction();

            try
            {
                if (normalized.Id > 0)
                {
                    using var updateCmd = new SqlCommand(UpdateSql, conn, tx);
                    AddFormParameters(updateCmd, normalized, safeUpdatedBy);
                    updateCmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = normalized.Id });
                    var rows = await updateCmd.ExecuteNonQueryAsync(ct);
                    tx.Commit();
                    return rows > 0 ? normalized.Id : 0;
                }

                using var insertCmd = new SqlCommand(InsertSql, conn, tx);
                AddFormParameters(insertCmd, normalized, safeUpdatedBy);
                var idObj = await insertCmd.ExecuteScalarAsync(ct);
                tx.Commit();
                return idObj is int id ? id : Convert.ToInt32(idObj, CultureInfo.InvariantCulture);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static async Task EnsureTableExistsAsync(SqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.SchoolTourOneOffIntakeTemp', N'U') IS NULL
BEGIN
    THROW 51041, 'dbo.SchoolTourOneOffIntakeTemp not found. Run Email/SqlScripts/2026-03-04_CreateSchoolTourOneOffIntakeTempTable.sql first.', 1;
END";
            using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static SchoolTourIntakeFormData NormalizeForm(SchoolTourIntakeFormData input)
        {
            var normalized = SchoolTourIntakeDefaults.CreateNewForm();
            normalized.Id = input.Id;
            normalized.RecordName = TrimOrNull(input.RecordName) ?? normalized.RecordName;
            normalized.IsActive = input.IsActive;
            normalized.Notes = TrimOrNull(input.Notes);
            normalized.UpdatedBy = TrimOrNull(input.UpdatedBy);
            normalized.CreatedAtUtc = input.CreatedAtUtc;
            normalized.UpdatedAtUtc = input.UpdatedAtUtc;

            normalized.TextFields = MergeTextFields(input.TextFields);
            normalized.DropdownFields = MergeDropdownFields(input.DropdownFields);
            normalized.DateRangeFields = MergeDateRangeFields(input.DateRangeFields);
            normalized.DateFields = MergeDateFields(input.DateFields);

            return normalized;
        }

        private static List<SchoolTourIntakeTextField> MergeTextFields(List<SchoolTourIntakeTextField>? source)
        {
            var defaults = SchoolTourIntakeDefaults.CreateTextFields();
            var sourceBySlot = (source ?? new List<SchoolTourIntakeTextField>())
                .GroupBy(x => x.Slot)
                .ToDictionary(g => g.Key, g => g.Last());

            foreach (var field in defaults)
            {
                if (sourceBySlot.TryGetValue(field.Slot, out var sourceField))
                {
                    field.Label = TrimOrNull(sourceField.Label) ?? field.Label;
                    field.Value = TrimOrNull(sourceField.Value);
                }
            }

            return defaults;
        }

        private static List<SchoolTourIntakeDropdownField> MergeDropdownFields(List<SchoolTourIntakeDropdownField>? source)
        {
            var defaults = SchoolTourIntakeDefaults.CreateDropdownFields();
            var sourceBySlot = (source ?? new List<SchoolTourIntakeDropdownField>())
                .GroupBy(x => x.Slot)
                .ToDictionary(g => g.Key, g => g.Last());

            foreach (var field in defaults)
            {
                if (sourceBySlot.TryGetValue(field.Slot, out var sourceField))
                {
                    field.Label = TrimOrNull(sourceField.Label) ?? field.Label;
                    field.OptionsCsv = TrimOrNull(sourceField.OptionsCsv) ?? field.OptionsCsv;
                    field.Value = TrimOrNull(sourceField.Value);
                }
            }

            return defaults;
        }

        private static List<SchoolTourIntakeDateRangeField> MergeDateRangeFields(List<SchoolTourIntakeDateRangeField>? source)
        {
            var defaults = SchoolTourIntakeDefaults.CreateDateRangeFields();
            var sourceBySlot = (source ?? new List<SchoolTourIntakeDateRangeField>())
                .GroupBy(x => x.Slot)
                .ToDictionary(g => g.Key, g => g.Last());

            foreach (var field in defaults)
            {
                if (sourceBySlot.TryGetValue(field.Slot, out var sourceField))
                {
                    field.Label = TrimOrNull(sourceField.Label) ?? field.Label;
                    field.StartDate = sourceField.StartDate?.Date;
                    field.EndDate = sourceField.EndDate?.Date;
                }
            }

            return defaults;
        }

        private static List<SchoolTourIntakeDateField> MergeDateFields(List<SchoolTourIntakeDateField>? source)
        {
            var defaults = SchoolTourIntakeDefaults.CreateDateFields();
            var sourceBySlot = (source ?? new List<SchoolTourIntakeDateField>())
                .GroupBy(x => x.Slot)
                .ToDictionary(g => g.Key, g => g.Last());

            foreach (var field in defaults)
            {
                if (sourceBySlot.TryGetValue(field.Slot, out var sourceField))
                {
                    field.Label = TrimOrNull(sourceField.Label) ?? field.Label;
                    field.Value = sourceField.Value?.Date;
                }
            }

            return defaults;
        }

        private static void AddFormParameters(SqlCommand cmd, SchoolTourIntakeFormData form, string? updatedBy)
        {
            cmd.Parameters.Add(new SqlParameter("@RecordName", SqlDbType.NVarChar, 200) { Value = (object)form.RecordName });
            cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = form.IsActive });
            cmd.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, -1) { Value = (object?)form.Notes ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 100) { Value = (object?)updatedBy ?? DBNull.Value });

            foreach (var textField in form.TextFields)
            {
                var slot = textField.Slot.ToString("00", CultureInfo.InvariantCulture);
                cmd.Parameters.Add(new SqlParameter($"@TextLabel{slot}", SqlDbType.NVarChar, 200) { Value = (object?)TrimOrNull(textField.Label) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter($"@TextValue{slot}", SqlDbType.NVarChar, -1) { Value = (object?)TrimOrNull(textField.Value) ?? DBNull.Value });
            }

            foreach (var dropdownField in form.DropdownFields)
            {
                var slot = dropdownField.Slot.ToString("00", CultureInfo.InvariantCulture);
                cmd.Parameters.Add(new SqlParameter($"@DropdownLabel{slot}", SqlDbType.NVarChar, 200) { Value = (object?)TrimOrNull(dropdownField.Label) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter($"@DropdownOptions{slot}", SqlDbType.NVarChar, -1) { Value = (object?)TrimOrNull(dropdownField.OptionsCsv) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter($"@DropdownValue{slot}", SqlDbType.NVarChar, 200) { Value = (object?)TrimOrNull(dropdownField.Value) ?? DBNull.Value });
            }

            foreach (var dateRange in form.DateRangeFields)
            {
                var slot = dateRange.Slot.ToString("00", CultureInfo.InvariantCulture);
                cmd.Parameters.Add(new SqlParameter($"@DateRangeLabel{slot}", SqlDbType.NVarChar, 200) { Value = (object?)TrimOrNull(dateRange.Label) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter($"@DateRangeStart{slot}", SqlDbType.Date) { Value = (object?)dateRange.StartDate?.Date ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter($"@DateRangeEnd{slot}", SqlDbType.Date) { Value = (object?)dateRange.EndDate?.Date ?? DBNull.Value });
            }

            foreach (var dateField in form.DateFields)
            {
                var slot = dateField.Slot.ToString("00", CultureInfo.InvariantCulture);
                cmd.Parameters.Add(new SqlParameter($"@DateLabel{slot}", SqlDbType.NVarChar, 200) { Value = (object?)TrimOrNull(dateField.Label) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter($"@DateValue{slot}", SqlDbType.Date) { Value = (object?)dateField.Value?.Date ?? DBNull.Value });
            }
        }

        private static string[] BuildSaveColumns()
        {
            var columns = new List<string>
            {
                "RecordName",
                "IsActive",
                "Notes",
                "UpdatedBy"
            };

            foreach (var slot in Enumerable.Range(1, SchoolTourIntakeDefaults.TextFieldCount))
            {
                var suffix = slot.ToString("00", CultureInfo.InvariantCulture);
                columns.Add($"TextLabel{suffix}");
                columns.Add($"TextValue{suffix}");
            }

            foreach (var slot in Enumerable.Range(1, SchoolTourIntakeDefaults.DropdownFieldCount))
            {
                var suffix = slot.ToString("00", CultureInfo.InvariantCulture);
                columns.Add($"DropdownLabel{suffix}");
                columns.Add($"DropdownOptions{suffix}");
                columns.Add($"DropdownValue{suffix}");
            }

            foreach (var slot in Enumerable.Range(1, SchoolTourIntakeDefaults.DateRangeCount))
            {
                var suffix = slot.ToString("00", CultureInfo.InvariantCulture);
                columns.Add($"DateRangeLabel{suffix}");
                columns.Add($"DateRangeStart{suffix}");
                columns.Add($"DateRangeEnd{suffix}");
            }

            foreach (var slot in Enumerable.Range(1, SchoolTourIntakeDefaults.DateFieldCount))
            {
                var suffix = slot.ToString("00", CultureInfo.InvariantCulture);
                columns.Add($"DateLabel{suffix}");
                columns.Add($"DateValue{suffix}");
            }

            return columns.ToArray();
        }

        private static string BuildInsertSql()
        {
            var columns = string.Join(", ", SaveColumns);
            var values = string.Join(", ", SaveColumns.Select(x => $"@{x}"));
            return $@"
INSERT INTO {TableName} ({columns}, CreatedAt, UpdatedAt)
VALUES ({values}, SYSUTCDATETIME(), SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);";
        }

        private static string BuildUpdateSql()
        {
            var setters = string.Join(", ", SaveColumns.Select(x => $"{x} = @{x}"));
            return $@"
UPDATE {TableName}
SET {setters},
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;";
        }

        private static string? ReadNullableString(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static DateTime? ReadNullableDate(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
        }

        private static string? TrimOrNull(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }
    }
}
