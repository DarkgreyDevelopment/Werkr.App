#pragma warning disable CS1591
using Werkr.Common.Communication;

namespace Werkr.Common.Protos;

// Proto-generated classes are partial. Declare the IHasResponseMetadata interface
// for all agent-consumed response messages so DecryptAndCheckUrgency can read metadata.
// These partials must be in the same assembly (Werkr.Agent) as the proto-generated types.

// Agent-consumed unary RPC responses
public partial class JobResultResponse : IHasResponseMetadata { }
public partial class StepEventResponse : IHasResponseMetadata { }
public partial class AgentScheduleResponse : IHasResponseMetadata { }
public partial class GetBulkScheduleHolidayDatesResponse : IHasResponseMetadata { }
public partial class SubmitAuditLogResponse : IHasResponseMetadata { }
public partial class ConfigSyncResponse : IHasResponseMetadata { }
public partial class GetVariableResponse : IHasResponseMetadata { }
public partial class SetVariableResponse : IHasResponseMetadata { }
public partial class CreateWorkflowRunResponse : IHasResponseMetadata { }
public partial class CompleteWorkflowRunResponse : IHasResponseMetadata { }
public partial class GetStepExecutionsResponse : IHasResponseMetadata { }
public partial class FileMonitorEventResponse : IHasResponseMetadata { }
public partial class AgentHeartbeatResponse : IHasResponseMetadata { }
public partial class FetchPendingKeyResponse : IHasResponseMetadata { }
public partial class AcknowledgeKeyResponse : IHasResponseMetadata { }
public partial class RegisterAgentResponse : IHasResponseMetadata { }

// Streaming
public partial class OutputSubscription : IHasResponseMetadata { }
#pragma warning restore CS1591
