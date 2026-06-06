using System.Text.Json;
using VisionPaint.Models;

namespace VisionPaint.Services;

internal static class JobUpdateApplier
{
    public static string? Apply(Job job, JsonElement body)
    {
        if (body.TryGetProperty("title", out var titleEl))
        {
            if (titleEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(titleEl.GetString()))
            {
                return "Job title is required.";
            }

            job.Title = titleEl.GetString()!.Trim();
        }

        if (body.TryGetProperty("description", out var descriptionEl))
        {
            job.Description = descriptionEl.ValueKind == JsonValueKind.Null ? null : descriptionEl.GetString();
        }

        if (body.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
        {
            var status = NormalizeStatus(statusEl.GetString());
            if (!IsValidStatus(status))
            {
                return "Invalid job status.";
            }

            job.Status = status;
        }

        if (body.TryGetProperty("priority", out var priorityEl) && priorityEl.ValueKind == JsonValueKind.String)
        {
            var priority = NormalizePriority(priorityEl.GetString());
            if (!IsValidPriority(priority))
            {
                return "Invalid job priority.";
            }

            job.Priority = priority;
        }

        if (body.TryGetProperty("clientId", out var clientIdEl))
        {
            job.ClientId = clientIdEl.ValueKind == JsonValueKind.Null ? null : clientIdEl.GetInt32();
        }

        ApplyOptionalString(body, "addressLine1", value => job.AddressLine1 = value);
        ApplyOptionalString(body, "addressLine2", value => job.AddressLine2 = value);
        ApplyOptionalString(body, "city", value => job.City = value);
        ApplyOptionalString(body, "stateRegion", value => job.StateRegion = value);
        ApplyOptionalString(body, "postalCode", value => job.PostalCode = value);
        ApplyOptionalString(body, "countryCode", value => job.CountryCode = value);

        ApplyOptionalDateTimeOffset(body, "scheduledStartAt", value => job.ScheduledStartAt = value);
        ApplyOptionalDateTimeOffset(body, "scheduledEndAt", value => job.ScheduledEndAt = value);
        ApplyOptionalDateTimeOffset(body, "dueAt", value => job.DueAt = value);
        ApplyOptionalDateTimeOffset(body, "startedAt", value => job.StartedAt = value);
        ApplyOptionalDateTimeOffset(body, "completedAt", value => job.CompletedAt = value);
        ApplyOptionalDateTimeOffset(body, "closedAt", value => job.ClosedAt = value);

        return null;
    }

    private static void ApplyOptionalString(JsonElement body, string propertyName, Action<string?> assign)
    {
        if (!body.TryGetProperty(propertyName, out var element))
        {
            return;
        }

        assign(element.ValueKind == JsonValueKind.Null ? null : element.GetString());
    }

    private static void ApplyOptionalDateTimeOffset(JsonElement body, string propertyName, Action<DateTimeOffset?> assign)
    {
        if (!body.TryGetProperty(propertyName, out var element))
        {
            return;
        }

        assign(element.ValueKind == JsonValueKind.Null ? null : element.GetDateTimeOffset());
    }

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "scheduled";
        }

        return status.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string NormalizePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
        {
            return "normal";
        }

        return priority.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static bool IsValidStatus(string status)
    {
        return status is "scheduled" or "in_progress" or "completed" or "cancelled";
    }

    private static bool IsValidPriority(string priority)
    {
        return priority is "low" or "normal" or "high" or "urgent";
    }
}
