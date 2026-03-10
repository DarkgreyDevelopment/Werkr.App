using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="TransformJsonHandler"/> action handler.
/// Validates dual-mode input (file vs. variable), JSON Pointer path navigation,
/// and all four operation types: Extract, Set, Delete, Merge.
/// </summary>
[TestClass]
public class TransformJsonHandlerTests {

    /// <summary>Handler under test.</summary>
    private TransformJsonHandler _handler = null!;
    /// <summary>Captures <see cref="OperatorOutput"/> messages.</summary>
    private Channel<OperatorOutput> _channel = null!;
    /// <summary>Temp directory for file I/O tests.</summary>
    private string _tempDir = null!;

    /// <summary>MSTest context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Creates handler, channel, and temp directory for each test.</summary>
    [TestInitialize]
    public void TestInit( ) {
        _handler = new TransformJsonHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<TransformJsonHandler>.Instance
        );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-transformjson-{Guid.NewGuid( ):N}" );
        _ = Directory.CreateDirectory( _tempDir );
    }

    /// <summary>Removes the temp directory after each test.</summary>
    [TestCleanup]
    public void TestCleanup( ) {
        if (Directory.Exists( _tempDir )) {
            Directory.Delete( _tempDir, recursive: true );
        }
    }

    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Input Resolution ─────────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>Variable input is used when no InputPath is set.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TransformJson_VariableInput_Succeeds( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/name" }],
        } );
        string input = """{"name":"Alice","age":30}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"Alice\"", result.OutputVariableValue );
    }

    /// <summary>File input takes precedence over variable input.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TransformJson_FileInputTakesPrecedence( ) {
        string filePath = Path.Combine( _tempDir, "input.json" );
        await File.WriteAllTextAsync( filePath, """{"source":"file"}""", CancellationToken.None );

        JsonElement parameters = Serialize( new TransformJsonParameters {
            InputPath = filePath,
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/source" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"source":"variable"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"file\"", result.OutputVariableValue );
    }

    /// <summary>Fails with descriptive error when neither input source is available.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TransformJson_NoInput_Fails( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/x" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer,
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "neither", result.Exception.Message );
    }

    /// <summary>Fails with descriptive error when input is not valid JSON.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TransformJson_InvalidJsonInput_Fails( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/x" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: "not-json{{{",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "not valid JSON", result.Exception.Message );
    }

    /// <summary>Fails when InputPath file does not exist.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TransformJson_InputFileNotFound_Fails( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            InputPath = Path.Combine( _tempDir, "nope.json" ),
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/x" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer,
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "not found", result.Exception.Message );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Extract ──────────────────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>Extract a top-level string property.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Extract_TopLevelProperty( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/name" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Bob","age":25}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"Bob\"", result.OutputVariableValue );
    }

    /// <summary>Extract a nested property using JSON Pointer.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Extract_NestedProperty( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/address/city" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer,
            inputVariableValue: """{"address":{"city":"Seattle","state":"WA"}}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"Seattle\"", result.OutputVariableValue );
    }

    /// <summary>Extract an array element by index.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Extract_ArrayElement( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/items/1" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"items":["a","b","c"]}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"b\"", result.OutputVariableValue );
    }

    /// <summary>Extract a non-existent path returns null.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Extract_NonExistentPath_ReturnsNull( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/missing" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Alice"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "null", result.OutputVariableValue );
    }

    /// <summary>Extract root (empty pointer) returns the full document.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Extract_RootPointer_ReturnsFullDocument( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "" }],
        } );
        string input = """{"x":1}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.OutputVariableValue );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue );
        Assert.AreEqual( 1, doc.RootElement.GetProperty( "x" ).GetInt32( ) );
    }

    /// <summary>Extract using $. convenience syntax.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Extract_DollarDotSyntax( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "$.name" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Charlie"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"Charlie\"", result.OutputVariableValue );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Set ──────────────────────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>Set an existing property to a new value.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Set_ExistingProperty( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/name", Value = "\"Eve\"" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Alice"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( "Eve", doc.RootElement.GetProperty( "name" ).GetString( ) );
    }

    /// <summary>Set a new property on an existing object.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Set_NewProperty( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/email", Value = "\"a@b.com\"" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Alice"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( "a@b.com", doc.RootElement.GetProperty( "email" ).GetString( ) );
        Assert.AreEqual( "Alice", doc.RootElement.GetProperty( "name" ).GetString( ) );
    }

    /// <summary>Set creates intermediate objects for nested paths.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Set_CreatesIntermediateObjects( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/address/city", Value = "\"Portland\"" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( "Portland", doc.RootElement.GetProperty( "address" ).GetProperty( "city" ).GetString( ) );
    }

    /// <summary>Set array element by index.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Set_ArrayElement( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/items/1", Value = "\"replaced\"" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"items":["a","b","c"]}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( "replaced", doc.RootElement.GetProperty( "items" )[1].GetString( ) );
    }

    /// <summary>Set fails when Value is null.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Set_NullValue_Fails( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/x" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "Value is required", result.Exception.Message );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Delete ───────────────────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>Delete removes an existing property.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delete_ExistingProperty( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Delete, Path = "/age" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Alice","age":30}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( "Alice", doc.RootElement.GetProperty( "name" ).GetString( ) );
        Assert.IsFalse( doc.RootElement.TryGetProperty( "age", out _ ) );
    }

    /// <summary>Delete a non-existent path succeeds silently.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delete_NonExistentPath_SilentSuccess( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Delete, Path = "/missing" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Alice"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( "Alice", doc.RootElement.GetProperty( "name" ).GetString( ) );
    }

    /// <summary>Delete a nested property.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delete_NestedProperty( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Delete, Path = "/address/city" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer,
            inputVariableValue: """{"address":{"city":"Seattle","state":"WA"}}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        JsonElement addr = doc.RootElement.GetProperty( "address" );
        Assert.IsFalse( addr.TryGetProperty( "city", out _ ) );
        Assert.AreEqual( "WA", addr.GetProperty( "state" ).GetString( ) );
    }

    /// <summary>Delete an array element by index.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delete_ArrayElement( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Delete, Path = "/items/1" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"items":["a","b","c"]}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        JsonElement items = doc.RootElement.GetProperty( "items" );
        Assert.AreEqual( 2, items.GetArrayLength( ) );
        Assert.AreEqual( "a", items[0].GetString( ) );
        Assert.AreEqual( "c", items[1].GetString( ) );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Merge ────────────────────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>Merge into root object adds/overrides properties.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Merge_IntoRoot( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation {
                Type = JsonTransformType.Merge, Path = "",
                Value = """{"age":31,"email":"a@b.com"}"""
            }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Alice","age":30}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( "Alice", doc.RootElement.GetProperty( "name" ).GetString( ) );
        Assert.AreEqual( 31, doc.RootElement.GetProperty( "age" ).GetInt32( ) );
        Assert.AreEqual( "a@b.com", doc.RootElement.GetProperty( "email" ).GetString( ) );
    }

    /// <summary>Merge into a nested object.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Merge_IntoNestedObject( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation {
                Type = JsonTransformType.Merge, Path = "/address",
                Value = """{"zip":"98101"}"""
            }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer,
            inputVariableValue: """{"address":{"city":"Seattle"}}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        JsonElement addr = doc.RootElement.GetProperty( "address" );
        Assert.AreEqual( "Seattle", addr.GetProperty( "city" ).GetString( ) );
        Assert.AreEqual( "98101", addr.GetProperty( "zip" ).GetString( ) );
    }

    /// <summary>Merge into a non-object target fails.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Merge_NonObjectTarget_Fails( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation {
                Type = JsonTransformType.Merge, Path = "/name",
                Value = """{"x":1}"""
            }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"name":"Alice"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "not a JSON object", result.Exception.Message );
    }

    /// <summary>Merge with null Value fails.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Merge_NullValue_Fails( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation {
                Type = JsonTransformType.Merge, Path = ""
            }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "Value is required", result.Exception.Message );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Operation Sequencing ─────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>Multiple operations applied in order: extract → set.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task OperationSequencing_ExtractThenSet( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [
                new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/settings" },
                new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/theme", Value = "\"dark\"" },
            ],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer,
            inputVariableValue: """{"settings":{"theme":"light","lang":"en"}}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( "dark", doc.RootElement.GetProperty( "theme" ).GetString( ) );
        Assert.AreEqual( "en", doc.RootElement.GetProperty( "lang" ).GetString( ) );
    }

    /// <summary>Set then delete in sequence.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task OperationSequencing_SetThenDelete( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [
                new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/temp", Value = "true" },
                new JsonTransformOperation { Type = JsonTransformType.Delete, Path = "/old" },
            ],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"old":"data","keep":"me"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.IsTrue( doc.RootElement.GetProperty( "temp" ).GetBoolean( ) );
        Assert.AreEqual( "me", doc.RootElement.GetProperty( "keep" ).GetString( ) );
        Assert.IsFalse( doc.RootElement.TryGetProperty( "old", out _ ) );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Output ───────────────────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>OutputPath writes file AND populates OutputVariableValue.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task OutputPath_WritesFileAndVariable( ) {
        string outputFile = Path.Combine( _tempDir, "out.json" );
        JsonElement parameters = Serialize( new TransformJsonParameters {
            OutputPath = outputFile,
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/x", Value = "42" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.OutputVariableValue );

        // Verify file was written
        Assert.IsTrue( File.Exists( outputFile ) );
        string fileContent = await File.ReadAllTextAsync( outputFile, CancellationToken.None );
        JsonDocument fileParsed = JsonDocument.Parse( fileContent );
        Assert.AreEqual( 42, fileParsed.RootElement.GetProperty( "x" ).GetInt32( ) );

        // Verify variable matches
        JsonDocument varParsed = JsonDocument.Parse( result.OutputVariableValue );
        Assert.AreEqual( 42, varParsed.RootElement.GetProperty( "x" ).GetInt32( ) );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Path Validation ──────────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>Denied InputPath fails via IFilePathResolver.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeniedInputPath_Fails( ) {
        TransformJsonHandler deniedHandler = new(
            TestFilePathResolver.DenyAll,
            NullLogger<TransformJsonHandler>.Instance );

        JsonElement parameters = Serialize( new TransformJsonParameters {
            InputPath = "/some/path.json",
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/x" }],
        } );

        ActionOperatorResult result = await deniedHandler.ExecuteAsync(
            parameters, _channel.Writer,
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    /// <summary>Denied OutputPath fails via IFilePathResolver.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeniedOutputPath_Fails( ) {
        TransformJsonHandler deniedHandler = new(
            TestFilePathResolver.DenyAll,
            NullLogger<TransformJsonHandler>.Instance );

        JsonElement parameters = Serialize( new TransformJsonParameters {
            OutputPath = "/some/output.json",
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Set, Path = "/x", Value = "1" }],
        } );

        ActionOperatorResult result = await deniedHandler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── JSON Pointer Edge Cases ──────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>Tilde escaping: ~0 → ~ and ~1 → /.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Pointer_TildeEscaping( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/a~1b" }],
        } );

        // The property name is literally "a/b"
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"a/b":"found"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"found\"", result.OutputVariableValue );
    }

    /// <summary>~0 unescapes to ~.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Pointer_Tilde0Escaping( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "/a~0b" }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{"a~b":"found"}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "\"found\"", result.OutputVariableValue );
    }

    /// <summary>Empty operations array fails.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task EmptyOperations_Fails( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: """{}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        Assert.Contains( "at least one operation", result.Exception.Message );
    }

    /// <summary>$ alone references root.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Pointer_DollarAlone_ReferencesRoot( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation { Type = JsonTransformType.Extract, Path = "$" }],
        } );
        string input = """{"x":1}""";

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: input,
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.AreEqual( 1, doc.RootElement.GetProperty( "x" ).GetInt32( ) );
    }

    /// <summary>Deep merge recursively merges nested objects.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Merge_DeepRecursive( ) {
        JsonElement parameters = Serialize( new TransformJsonParameters {
            Operations = [new JsonTransformOperation {
                Type = JsonTransformType.Merge, Path = "",
                Value = """{"a":{"y":2}}"""
            }],
        } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters, _channel.Writer,
            inputVariableValue: """{"a":{"x":1}}""",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        JsonElement a = doc.RootElement.GetProperty( "a" );
        Assert.AreEqual( 1, a.GetProperty( "x" ).GetInt32( ) );
        Assert.AreEqual( 2, a.GetProperty( "y" ).GetInt32( ) );
    }
}
