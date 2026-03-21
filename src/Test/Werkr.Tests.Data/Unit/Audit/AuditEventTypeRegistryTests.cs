using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using static Werkr.Common.Models.Audit.AuditEventType;

namespace Werkr.Tests.Data.Unit.Audit;

[TestClass]
public class AuditEventTypeRegistryTests {

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Register_ValidType_AppearsInGetAll( ) {
        AuditEventTypeRegistry registry = new( );
        registry.Register( "test.event", "Test Event", "Testing", "test-module" );

        IReadOnlyList<AuditEventTypeDto> all = registry.GetAll( );
        Assert.IsNotNull( all.FirstOrDefault( t => t.EventTypeId == "test.event" ),
            "Registered type 'test.event' should appear in GetAll." );
    }

    [TestMethod]
    public void Register_DuplicateTypeId_SilentlyIgnored( ) {
        AuditEventTypeRegistry registry = new( );
        registry.Register( "dup.event", "First", "Cat1", "mod1" );
        registry.Register( "dup.event", "Second", "Cat2", "mod2" );

        IReadOnlyList<AuditEventTypeDto> all = registry.GetAll( );
        Assert.HasCount( 1, all );
        Assert.AreEqual( "First", all[0].DisplayName );
    }

    [TestMethod]
    public void GetCategories_ReturnsDistinctCategories( ) {
        AuditEventTypeRegistry registry = new( );
        registry.Register( "a.one", "A1", "Alpha", "mod" );
        registry.Register( "b.one", "B1", "Beta", "mod" );
        registry.Register( "c.one", "C1", "Gamma", "mod" );
        registry.Register( "a.two", "A2", "Alpha", "mod" );

        IReadOnlyList<string> categories = registry.GetCategories( );
        Assert.HasCount( 3, categories );
    }

    [TestMethod]
    public void GetByTypeId_Registered_ReturnsDefinition( ) {
        AuditEventTypeRegistry registry = new( );
        registry.Register( "lookup.test", "Lookup Test", "LookupCat", "lookup-mod" );

        AuditEventTypeDto? result = registry.GetByTypeId( "lookup.test" );
        Assert.IsNotNull( result );
        Assert.AreEqual( "lookup.test", result.EventTypeId );
        Assert.AreEqual( "Lookup Test", result.DisplayName );
        Assert.AreEqual( "LookupCat", result.Category );
        Assert.AreEqual( "lookup-mod", result.SourceModule );
    }

    [TestMethod]
    public void GetByTypeId_Unregistered_ReturnsNull( ) {
        AuditEventTypeRegistry registry = new( );
        Assert.IsNull( registry.GetByTypeId( "nonexistent.type" ) );
    }

    [TestMethod]
    public void GetModules_ReturnsDistinctModules( ) {
        AuditEventTypeRegistry registry = new( );
        registry.Register( "m.one", "M1", "Cat", "module_a" );
        registry.Register( "m.two", "M2", "Cat", "module_b" );
        registry.Register( "m.three", "M3", "Cat", "module_a" );

        IReadOnlyList<string> modules = registry.GetModules( );
        Assert.HasCount( 2, modules );
    }

    [TestMethod]
    public void RegisterCoreAuditEvents_RegistersAllExpectedTypes( ) {
        AuditEventTypeRegistry registry = new( );
        _ = registry.RegisterCoreAuditEvents( );

        IReadOnlyList<AuditEventTypeDto> all = registry.GetAll( );
        Assert.HasCount( 33, all );
    }

    [TestMethod]
    [DataRow( AuthLoginSuccess )]
    [DataRow( AuthLoginFailure )]
    [DataRow( AuthLockout )]
    [DataRow( Auth2FaFailure )]
    [DataRow( ApiKeyCreated )]
    [DataRow( ApiKeyRevoked )]
    [DataRow( UserCreated )]
    [DataRow( UserUpdated )]
    [DataRow( UserDeleted )]
    [DataRow( UserDisabled )]
    [DataRow( UserEnabled )]
    [DataRow( UserPasswordReset )]
    [DataRow( AgentRegistered )]
    [DataRow( AgentRevoked )]
    [DataRow( AgentUpdated )]
    [DataRow( AgentKeyRotated )]
    [DataRow( CalendarCreated )]
    [DataRow( CalendarUpdated )]
    [DataRow( CalendarDeleted )]
    [DataRow( CalendarCloned )]
    [DataRow( CalendarAttached )]
    [DataRow( CalendarDetached )]
    [DataRow( ScheduleOccurrenceSuppressed )]
    [DataRow( ScheduleOccurrenceShifted )]
    [DataRow( TaskVersionCreated )]
    [DataRow( WorkflowVersionCreated )]
    [DataRow( WorkflowVersionRollback )]
    [DataRow( WorkflowDeleted )]
    [DataRow( WorkflowDisabled )]
    [DataRow( WorkflowEnabled )]
    [DataRow( TriggerVersionCreated )]
    [DataRow( TriggerBindingUpdated )]
    [DataRow( AuditRetentionCleanup )]
    public void RegisterCoreAuditEvents_ContainsEventType( AuditEventType eventType ) {
        AuditEventTypeRegistry registry = new( );
        _ = registry.RegisterCoreAuditEvents( );
        string eventTypeId = eventType.ToEventId( );
        Assert.IsNotNull( registry.GetByTypeId( eventTypeId ),
            $"Expected event type '{eventTypeId}' to be registered." );
    }
}
