using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="ForEachHandler"/> action handler.
/// Validates JSON object input parsing, array property selection,
/// scalar element validation, and last-element output behavior.
/// </summary>
[TestClass]
public class ForEachHandlerTests {

    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private ForEachHandler _handler = null!;
    /// <summary>
    /// Unbounded channel used to capture <see cref="OperatorOutput"/> messages.
    /// </summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates the handler instance and output channel for each test.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _handler = new ForEachHandler(
            NullLogger<ForEachHandler>.Instance
        );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    /// <summary>
    /// Serializes a value to a <see cref="JsonElement"/> using the shared test serializer.
    /// </summary>
    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    // ── Happy-Path Tests ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that an array of strings emits the last element.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_StringArray_EmitsLastElement( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );
        string input = """{"items": ["alpha", "bravo", "charlie"]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"charlie\"", result.OutputVariableValue );
    }

    /// <summary>
    /// Verifies that an array of integers emits the last element.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_NumberArray_EmitsLastElement( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "values" } );
        string input = """{"values": [10, 20, 30]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "30", result.OutputVariableValue );
    }

    /// <summary>
    /// Verifies that a mixed string/number array is accepted and emits the last element.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_MixedScalarArray_EmitsLastElement( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "data" } );
        string input = """{"data": ["hello", 42, true, false, null]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "null", result.OutputVariableValue );
    }

    /// <summary>
    /// Verifies that a single-element array returns that element.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_SingleElement_EmitsIt( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );
        string input = """{"items": ["only"]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"only\"", result.OutputVariableValue );
    }

    // ── Empty Array ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty array succeeds with null output.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_EmptyArray_SucceedsWithNullOutput( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );
        string input = """{"items": []}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsNull( result.OutputVariableValue );
    }

    // ── Validation Failure Tests ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a non-object input (array root) fails with descriptive error.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_NonObjectInput_Fails( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );
        string input = """[1, 2, 3]""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "JSON object", result.Exception.Message );
    }

    /// <summary>
    /// Verifies that a string input (primitive) fails.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_StringInput_Fails( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );
        string input = "\"just a string\"";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "JSON object", result.Exception.Message );
    }

    /// <summary>
    /// Verifies that a missing ArrayPropertyName in the input object fails.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_MissingProperty_Fails( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "missing" } );
        string input = """{"items": [1, 2, 3]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "'missing'", result.Exception.Message );
    }

    /// <summary>
    /// Verifies that a non-array property fails.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_PropertyNotArray_Fails( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "count" } );
        string input = """{"count": 42}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "JSON array", result.Exception.Message );
    }

    /// <summary>
    /// Verifies that an array containing a complex object element is rejected.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_ComplexObjectElement_Fails( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );
        string input = """{"items": ["ok", {"nested": true}]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "scalar", result.Exception.Message );
    }

    /// <summary>
    /// Verifies that an array containing a nested array element is rejected.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_NestedArrayElement_Fails( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );
        string input = """{"items": [1, [2, 3]]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "scalar", result.Exception.Message );
    }

    /// <summary>
    /// Verifies that null/missing input variable fails with descriptive error.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_NullInput_Fails( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: null,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "input variable", result.Exception.Message );
    }

    /// <summary>
    /// Verifies that invalid JSON in the input variable fails gracefully.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_InvalidJsonInput_Fails( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "items" } );
        string input = "not-json{";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "valid JSON", result.Exception.Message );
    }

    // ── Boolean/Null Scalar Tests ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that boolean true/false array elements are accepted as scalars.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_BooleanArray_EmitsLastElement( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "flags" } );
        string input = """{"flags": [true, false, true]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "true", result.OutputVariableValue );
    }

    /// <summary>
    /// Verifies that floating-point numbers in the array are accepted.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ForEach_FloatArray_EmitsLastElement( ) {
        JsonElement parameters = Serialize( new ForEachParameters { ArrayPropertyName = "vals" } );
        string input = """{"vals": [1.5, 2.7, 3.14]}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "3.14", result.OutputVariableValue );
    }
}
