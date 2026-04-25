using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Email.Services
{
    public interface ISchoolTourIntakeService
    {
        Task<List<SchoolTourIntakeListItem>> GetRecentAsync(int maxRows = 100, CancellationToken ct = default);
        Task<SchoolTourIntakeFormData?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<int> SaveAsync(SchoolTourIntakeFormData form, string? updatedBy, CancellationToken ct = default);
    }

    public sealed class SchoolTourIntakeListItem
    {
        public int Id { get; set; }
        public string RecordName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public sealed class SchoolTourIntakeTextField
    {
        public int Slot { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

    public sealed class SchoolTourIntakeDropdownField
    {
        public int Slot { get; set; }
        public string Label { get; set; } = string.Empty;
        public string OptionsCsv { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

    public sealed class SchoolTourIntakeDateRangeField
    {
        public int Slot { get; set; }
        public string Label { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public sealed class SchoolTourIntakeDateField
    {
        public int Slot { get; set; }
        public string Label { get; set; } = string.Empty;
        public DateTime? Value { get; set; }
    }

    public sealed class SchoolTourIntakeFormData
    {
        public int Id { get; set; }
        public string RecordName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string? Notes { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }

        public List<SchoolTourIntakeTextField> TextFields { get; set; } = SchoolTourIntakeDefaults.CreateTextFields();
        public List<SchoolTourIntakeDropdownField> DropdownFields { get; set; } = SchoolTourIntakeDefaults.CreateDropdownFields();
        public List<SchoolTourIntakeDateRangeField> DateRangeFields { get; set; } = SchoolTourIntakeDefaults.CreateDateRangeFields();
        public List<SchoolTourIntakeDateField> DateFields { get; set; } = SchoolTourIntakeDefaults.CreateDateFields();
    }

    public static class SchoolTourIntakeDefaults
    {
        public const int TextFieldCount = 20;
        public const int DropdownFieldCount = 10;
        public const int DateRangeCount = 3;
        public const int DateFieldCount = 3;

        private static readonly string[] DefaultTextLabels =
        {
            "School Name",
            "School Contact Name",
            "School Contact Phone",
            "School Contact Email",
            "Requested Tour Title",
            "Internal Tour Name",
            "Grade Level",
            "Student Count Estimate (Text)",
            "Chaperone Count Estimate",
            "Accessibility Needs",
            "Language Needs",
            "Pickup Location",
            "Dropoff Location",
            "Special Timing Notes",
            "Budget Notes",
            "Invoice Contact",
            "Billing Address",
            "Permission Slip Status",
            "Emergency Contact",
            "Internal Notes"
        };

        private static readonly string[] DefaultDropdownLabels =
        {
            "Number of Students Attending",
            "Number of Chaperones",
            "Transportation Type",
            "Lunch Included",
            "Accessibility Support Needed",
            "Photo Consent Collected",
            "Payment Method",
            "Tour Language",
            "Risk Level",
            "Follow-up Status"
        };

        private static readonly string[] DefaultDropdownOptions =
        {
            "10,15,20,25,30,35,40,45,50,60,70,80,90,100,120,150,200",
            "1,2,3,4,5,6,8,10,12,15,20",
            "Bus,Subway,Walking,Other",
            "Yes,No,Unknown",
            "Yes,No,TBD",
            "Yes,No,TBD",
            "Invoice,Card,Cash,Other",
            "English,Spanish,Bilingual,Other",
            "Low,Medium,High",
            "New,Pending,Confirmed,Completed"
        };

        private static readonly string[] DefaultDateRangeLabels =
        {
            "Requested Tour Window",
            "Backup Tour Window",
            "Payment / Approval Window"
        };

        private static readonly string[] DefaultDateLabels =
        {
            "Preferred Tour Date",
            "Contract Signed Date",
            "Final Headcount Due Date"
        };

        public static SchoolTourIntakeFormData CreateNewForm()
        {
            return new SchoolTourIntakeFormData
            {
                RecordName = $"School Tour Intake {DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
                IsActive = true,
                TextFields = CreateTextFields(),
                DropdownFields = CreateDropdownFields(),
                DateRangeFields = CreateDateRangeFields(),
                DateFields = CreateDateFields()
            };
        }

        public static List<SchoolTourIntakeTextField> CreateTextFields()
        {
            return Enumerable.Range(1, TextFieldCount)
                .Select(i => new SchoolTourIntakeTextField
                {
                    Slot = i,
                    Label = i <= DefaultTextLabels.Length ? DefaultTextLabels[i - 1] : $"Text Field {i}",
                    Value = null
                })
                .ToList();
        }

        public static List<SchoolTourIntakeDropdownField> CreateDropdownFields()
        {
            return Enumerable.Range(1, DropdownFieldCount)
                .Select(i => new SchoolTourIntakeDropdownField
                {
                    Slot = i,
                    Label = i <= DefaultDropdownLabels.Length ? DefaultDropdownLabels[i - 1] : $"Dropdown Field {i}",
                    OptionsCsv = i <= DefaultDropdownOptions.Length ? DefaultDropdownOptions[i - 1] : "Option A,Option B,Option C",
                    Value = null
                })
                .ToList();
        }

        public static List<SchoolTourIntakeDateRangeField> CreateDateRangeFields()
        {
            return Enumerable.Range(1, DateRangeCount)
                .Select(i => new SchoolTourIntakeDateRangeField
                {
                    Slot = i,
                    Label = i <= DefaultDateRangeLabels.Length ? DefaultDateRangeLabels[i - 1] : $"Date Range {i}",
                    StartDate = null,
                    EndDate = null
                })
                .ToList();
        }

        public static List<SchoolTourIntakeDateField> CreateDateFields()
        {
            return Enumerable.Range(1, DateFieldCount)
                .Select(i => new SchoolTourIntakeDateField
                {
                    Slot = i,
                    Label = i <= DefaultDateLabels.Length ? DefaultDateLabels[i - 1] : $"Date Field {i}",
                    Value = null
                })
                .ToList();
        }
    }
}
