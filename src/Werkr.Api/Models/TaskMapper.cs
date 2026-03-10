using System.Text.Json;
using System.Text.Json.Serialization;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Tasks;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Api.Models;

/// <summary>
/// Bidirectional mapping between Task/Job DTOs and domain entities.
/// </summary>
internal static class TaskMapper {

    /// <summary>
    /// Maps action sub-type names (case-insensitive) to their corresponding parameter deserialization types. Used during validation to ensure <c>ActionParameters</c> JSON can be correctly deserialized for the given <c>ActionSubType</c>.
    /// </summary>
    private static readonly Dictionary<string, Type> s_actionParameterTypes =
        new( StringComparer.OrdinalIgnoreCase ) {
            ["CopyFile"] = typeof( CopyFileParameters ),
            ["MoveFile"] = typeof( MoveFileParameters ),
            ["RenameFile"] = typeof( RenameFileParameters ),
            ["DeleteFile"] = typeof( DeleteFileParameters ),
            ["CreateFile"] = typeof( CreateFileParameters ),
            ["CreateDirectory"] = typeof( CreateDirectoryParameters ),
            ["TestExists"] = typeof( TestExistsParameters ),
            ["ClearContent"] = typeof( ClearContentParameters ),
            ["WriteContent"] = typeof( WriteContentParameters ),
            ["StartProcess"] = typeof( StartProcessParameters ),
            ["StopProcess"] = typeof( StopProcessParameters ),

            // ── Phase 1 no-code actions ──────────────────────────────
            ["Delay"] = typeof(DelayParameters),
            ["GetFileInfo"] = typeof(GetFileInfoParameters),
            ["ReadContent"] = typeof(ReadContentParameters),
            ["ListDirectory"] = typeof(ListDirectoryParameters),
            ["FindReplace"] = typeof(FindReplaceParameters),
            ["CompressArchive"] = typeof(CompressArchiveParameters),
            ["ExpandArchive"] = typeof(ExpandArchiveParameters),
            ["WatchFile"] = typeof(WatchFileParameters),
        };

    /// <summary>
    /// Shared <see cref="JsonSerializerOptions"/> configured for case-insensitive property name matching during action parameter deserialization.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter( ) },
    };

    /// <summary>Maps a <see cref="TaskCreateRequest"/> to a <see cref="WerkrTask"/> entity.</summary>
    public static WerkrTask ToEntity( TaskCreateRequest request ) {
        ValidateActionFields( request.ActionType, request.ActionSubType, request.ActionParameters );

        return new( ) {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            ActionType = Enum.Parse<TaskActionType>( request.ActionType, ignoreCase: true ),
            Content = request.Content,
            Arguments = request.Arguments,
            TargetTags = request.TargetTags,
            Enabled = request.Enabled,
            TimeoutMinutes = request.TimeoutMinutes,
            SuccessCriteria = request.SuccessCriteria,
            WorkflowId = request.WorkflowId,
            ActionSubType = request.ActionSubType,
            ActionParameters = request.ActionParameters,
        };
    }

    /// <summary>Maps a <see cref="TaskUpdateRequest"/> to a <see cref="WerkrTask"/> entity with a given ID.</summary>
    public static WerkrTask ToEntity( long id, TaskUpdateRequest request ) {
        ValidateActionFields( request.ActionType, request.ActionSubType, request.ActionParameters );

        return new( ) {
            Id = id,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            ActionType = Enum.Parse<TaskActionType>( request.ActionType, ignoreCase: true ),
            Content = request.Content,
            Arguments = request.Arguments,
            TargetTags = request.TargetTags,
            Enabled = request.Enabled,
            TimeoutMinutes = request.TimeoutMinutes,
            SuccessCriteria = request.SuccessCriteria,
            WorkflowId = request.WorkflowId,
            ActionSubType = request.ActionSubType,
            ActionParameters = request.ActionParameters,
        };
    }

    /// <summary>Maps a <see cref="WerkrTask"/> entity to a <see cref="TaskDto"/>.</summary>
    public static TaskDto ToDto( WerkrTask task ) =>
        new(
            Id: task.Id,
            Name: task.Name,
            Description: task.Description,
            ActionType: task.ActionType.ToString( ),
            Content: task.Content,
            Arguments: task.Arguments,
            TargetTags: task.TargetTags,
            Enabled: task.Enabled,
            TimeoutMinutes: task.TimeoutMinutes,
            SyncIntervalMinutes: task.SyncIntervalMinutes,
            SuccessCriteria: task.SuccessCriteria,
            EffectiveSuccessCriteria: SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
                task.ActionType, task.SuccessCriteria ),
            WorkflowId: task.WorkflowId,
            ActionSubType: task.ActionSubType,
            ActionParameters: task.ActionParameters );

    /// <summary>Maps a <see cref="WerkrJob"/> entity to a <see cref="JobDto"/>.</summary>
    public static JobDto ToJobDto( WerkrJob job ) =>
        new(
            Id: job.Id,
            TaskId: job.TaskId,
            Success: job.Success,
            ExitCode: job.ExitCode,
            ErrorCategory: job.ErrorCategory.ToString( ),
            RuntimeSeconds: job.RuntimeSeconds,
            StartTime: job.StartTime,
            EndTime: job.EndTime,
            AgentConnectionId: job.AgentConnectionId,
            Output: job.Output,
            OutputPath: job.OutputPath );

    /// <summary>Maps a <see cref="WerkrJob"/> entity to a <see cref="JobListDto"/>.</summary>
    public static JobListDto ToJobListDto( WerkrJob job ) =>
        new(
            Id: job.Id,
            TaskId: job.TaskId,
            Success: job.Success,
            RuntimeSeconds: job.RuntimeSeconds,
            StartTime: job.StartTime,
            ErrorCategory: job.ErrorCategory.ToString( ),
            TaskName: job.Task?.Name,
            AgentConnectionId: job.AgentConnectionId,
            AgentName: job.AgentConnection?.ConnectionName,
            EndTime: job.EndTime );

    private static void ValidateActionFields(
        string actionType,
        string? actionSubType,
        string? actionParameters
    ) {

        bool isActionTask = string.Equals(
            actionType,
            TaskActionType.Action.ToString( ),
            StringComparison.OrdinalIgnoreCase );

        if (isActionTask) {
            if (string.IsNullOrWhiteSpace( actionSubType )) {
                throw new ArgumentException( "ActionSubType is required when ActionType is 'Action'." );
            }

            if (!s_actionParameterTypes.TryGetValue( actionSubType, out Type? parameterType )) {
                throw new ArgumentException( $"Unknown ActionSubType '{actionSubType}'." );
            }

            if (string.IsNullOrWhiteSpace( actionParameters )) {
                throw new ArgumentException( "ActionParameters is required when ActionType is 'Action'." );
            }

            try {
                using JsonDocument parsed = JsonDocument.Parse( actionParameters );
                if (parsed.RootElement.ValueKind != JsonValueKind.Object) {
                    throw new ArgumentException( "ActionParameters must be a JSON object." );
                }

                object? deserialized = JsonSerializer.Deserialize(
                    actionParameters,
                    parameterType,
                    s_jsonOptions ) ?? throw new ArgumentException(
                        $"ActionParameters could not be deserialized for action '{actionSubType}'." );
            } catch (JsonException ex) {
                throw new ArgumentException(
                    $"ActionParameters is not valid JSON for action '{actionSubType}': {ex.Message}", ex );
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace( actionSubType )) {
            throw new ArgumentException( "ActionSubType must be null when ActionType is not 'Action'." );
        }

        if (!string.IsNullOrWhiteSpace( actionParameters )) {
            throw new ArgumentException( "ActionParameters must be null when ActionType is not 'Action'." );
        }
    }
}
