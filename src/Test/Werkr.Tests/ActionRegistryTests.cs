using Werkr.Common.Models.Actions;

namespace Werkr.Tests;

/// <summary>
/// Unit tests for the <see cref="ActionRegistry"/> class defined in
/// the <c>Werkr.Common</c> project. Validates that the static registry of action
/// form descriptors contains the expected actions, fields, field types, default
/// values, and encoding options used by the action parameter system.
/// </summary>
[TestClass]
public class ActionRegistryTests {
    /// <summary>
    /// Verifies that the <see cref="ActionRegistry.All"/> collection
    /// contains exactly thirty registered <see cref="ActionFormDescriptor"/>
    /// entries representing all supported actions (26 action handlers + 4 Shell/PowerShell operators).
    /// </summary>
    [TestMethod]
    public void All_Contains_Thirty_Actions( ) {
        Assert.HasCount( 31, ActionRegistry.All );
    }

    /// <summary>
    /// Verifies that the <see cref="ActionRegistry.Actions"/> dictionary
    /// uses a case-insensitive string comparer, so lookups for keys such as
    /// "copyfile", "COPYFILE", and "CopyFile" all succeed.
    /// </summary>
    [TestMethod]
    public void Actions_Dictionary_Is_Case_Insensitive( ) {
        Assert.IsTrue( ActionRegistry.Actions.ContainsKey( "copyfile" ) );
        Assert.IsTrue( ActionRegistry.Actions.ContainsKey( "COPYFILE" ) );
        Assert.IsTrue( ActionRegistry.Actions.ContainsKey( "CopyFile" ) );
    }

    /// <summary>
    /// Verifies that the <see cref="ActionRegistry.Actions"/> dictionary
    /// contains a specific expected action key.
    /// </summary>
    [TestMethod]
    [DataRow( "CopyFile" )]
    [DataRow( "MoveFile" )]
    [DataRow( "RenameFile" )]
    [DataRow( "DeleteFile" )]
    [DataRow( "CreateFile" )]
    [DataRow( "CreateDirectory" )]
    [DataRow( "TestExists" )]
    [DataRow( "ClearContent" )]
    [DataRow( "WriteContent" )]
    [DataRow( "StartProcess" )]
    [DataRow( "StopProcess" )]
    [DataRow( "Delay" )]
    [DataRow( "GetFileInfo" )]
    [DataRow( "ReadContent" )]
    [DataRow( "ListDirectory" )]
    [DataRow( "FindReplace" )]
    [DataRow( "CompressArchive" )]
    [DataRow( "ExpandArchive" )]
    [DataRow( "WatchFile" )]
    [DataRow( "ForEach" )]
    [DataRow( "HttpRequest" )]
    [DataRow( "DownloadFile" )]
    [DataRow( "TestConnection" )]
    [DataRow( "UploadFile" )]
    [DataRow( "SendEmail" )]
    [DataRow( "SendWebhook" )]
    [DataRow( "TransformJson" )]
    [DataRow( "ShellCommand" )]
    [DataRow( "ShellScript" )]
    [DataRow( "PowerShellCommand" )]
    [DataRow( "PowerShellScript" )]
    public void Actions_Contains_Expected_Key( string key ) {
        Assert.IsTrue(
            ActionRegistry.Actions.ContainsKey( key ),
            $"Missing action key: {key}"
        );
    }

    /// <summary>
    /// Verifies that every <see cref="ActionFormDescriptor"/> in the registry has
    /// at least one <see cref="FieldDescriptor"/>, ensuring no action is registered
    /// without defining its required parameters.
    /// </summary>
    [TestMethod]
    public void Every_Descriptor_Has_At_Least_One_Field( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            Assert.IsNotEmpty(
                desc.Fields,
                $"Action '{desc.Key}' has no fields." );
        }
    }

    /// <summary>
    /// Verifies that every <see cref="ActionFormDescriptor"/> has a non-null,
    /// non-whitespace <see cref="ActionFormDescriptor.DisplayName"/> and
    /// <see cref="ActionFormDescriptor.Description"/> so that the UI can present
    /// meaningful labels.
    /// </summary>
    [TestMethod]
    public void Every_Descriptor_Has_NonEmpty_DisplayName_And_Description( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace( desc.DisplayName ),
                $"Action '{desc.Key}' has empty DisplayName." );
            Assert.IsFalse(
                string.IsNullOrWhiteSpace( desc.Description ),
                $"Action '{desc.Key}' has empty Description." );
        }
    }

    /// <summary>
    /// Verifies that every <see cref="ActionFormDescriptor"/> has a non-null,
    /// non-whitespace <see cref="ActionFormDescriptor.Category"/> so actions
    /// can be grouped in the UI dropdown.
    /// </summary>
    [TestMethod]
    public void Every_Descriptor_Has_NonEmpty_Category( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace( desc.Category ),
                $"Action '{desc.Key}' has empty Category." );
        }
    }

    /// <summary>
    /// Verifies that every <see cref="ActionFormDescriptor"/> has a non-null
    /// <see cref="ActionFormDescriptor.ParameterType"/> so API-side validation
    /// can deserialize action parameters.
    /// </summary>
    [TestMethod]
    public void Every_Descriptor_Has_ParameterType( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            Assert.IsNotNull(
                desc.ParameterType,
                $"Action '{desc.Key}' has null ParameterType." );
        }
    }

    /// <summary>
    /// Verifies that every <see cref="FieldDescriptor"/> whose
    /// <see cref="FieldDescriptor.Type"/> is <see cref="FieldType.Select"/> has a non-null,
    /// non-empty <see cref="FieldDescriptor.Options"/> list so that dropdowns in the UI
    /// are populated.
    /// </summary>
    [TestMethod]
    public void Select_Fields_Have_Options( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            foreach (FieldDescriptor field in desc.Fields) {
                if (field.Type == FieldType.Select) {
                    Assert.IsNotNull(
                        field.Options,
                        $"'{desc.Key}.{field.Name}' is Select but has no Options." );
                    Assert.IsNotEmpty(
                        field.Options,
                        $"'{desc.Key}.{field.Name}' is Select but Options is empty." );
                }
            }
        }
    }

    /// <summary>
    /// Verifies that the <see cref="ActionRegistry.Encodings"/> list
    /// contains the common "utf-8" encoding value which is the default for
    /// most file operations.
    /// </summary>
    [TestMethod]
    public void Encodings_Contains_Utf8( ) {
        CollectionAssert.Contains( ActionRegistry.Encodings, "utf-8" );
    }

    /// <summary>
    /// Verifies that the <see cref="ActionRegistry.Encodings"/> list
    /// contains exactly nine supported character encoding values.
    /// </summary>
    [TestMethod]
    public void Encodings_Has_Nine_Values( ) {
        Assert.HasCount( 9, ActionRegistry.Encodings );
    }

    /// <summary>
    /// Verifies that the "CopyFile" action's <see cref="ActionFormDescriptor"/>
    /// has exactly four fields named "Source", "Destination", "Overwrite",
    /// and "Recursive" in the expected order.
    /// </summary>
    [TestMethod]
    public void CopyFile_Has_Expected_Fields( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["CopyFile"];
        Assert.HasCount( 4, desc.Fields );
        Assert.AreEqual( "Source", desc.Fields[0].Name );
        Assert.AreEqual( "Destination", desc.Fields[1].Name );
        Assert.AreEqual( "Overwrite", desc.Fields[2].Name );
        Assert.AreEqual( "Recursive", desc.Fields[3].Name );
    }

    /// <summary>
    /// Verifies that the "TestExists" action's "Type" field is a
    /// <see cref="FieldType.Select"/> with exactly three options: "Any",
    /// "File", and "Directory".
    /// </summary>
    [TestMethod]
    public void TestExists_Type_Is_Select_With_Three_Options( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["TestExists"];
        FieldDescriptor typeField = desc.Fields[1];
        Assert.AreEqual( "Type", typeField.Name );
        Assert.AreEqual( FieldType.Select, typeField.Type );
        Assert.HasCount( 3, typeField.Options! );
        CollectionAssert.Contains( typeField.Options, "Any" );
        CollectionAssert.Contains( typeField.Options, "File" );
        CollectionAssert.Contains( typeField.Options, "Directory" );
    }

    /// <summary>
    /// Verifies that the "CreateFile" action's "Encoding" field has a default
    /// value of "utf-8", ensuring new files will use UTF-8 encoding unless
    /// explicitly overridden.
    /// </summary>
    [TestMethod]
    public void CreateFile_Encoding_Default_Is_Utf8( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["CreateFile"];
        FieldDescriptor? enc = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "Encoding") { enc = f; break; }
        }
        Assert.IsNotNull( enc );
        Assert.AreEqual( "utf-8", enc.DefaultValue );
    }

    /// <summary>
    /// Verifies that the "WatchFile" action has seven fields covering
    /// all configurable parameters for file-system monitoring.
    /// </summary>
    [TestMethod]
    public void WatchFile_Has_Seven_Fields( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["WatchFile"];
        Assert.HasCount( 7, desc.Fields );
        Assert.AreEqual( "Directory", desc.Fields[0].Name );
        Assert.AreEqual( "Pattern", desc.Fields[1].Name );
        Assert.AreEqual( "StabilitySeconds", desc.Fields[2].Name );
        Assert.AreEqual( "TimeoutSeconds", desc.Fields[3].Name );
        Assert.AreEqual( "PollIntervalMs", desc.Fields[4].Name );
        Assert.AreEqual( "Mode", desc.Fields[5].Name );
        Assert.AreEqual( "UsePolling", desc.Fields[6].Name );
    }

    /// <summary>
    /// Verifies that the "CompressArchive" Format field only offers Zip and TarGz
    /// (Auto is excluded because the handler rejects it for compression).
    /// </summary>
    [TestMethod]
    public void CompressArchive_Format_Is_Select_Without_Auto( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["CompressArchive"];
        FieldDescriptor format = desc.Fields[2];
        Assert.AreEqual( "Format", format.Name );
        Assert.AreEqual( FieldType.Select, format.Type );
        Assert.HasCount( 2, format.Options! );
        CollectionAssert.Contains( format.Options, "Zip" );
        CollectionAssert.Contains( format.Options, "TarGz" );
        CollectionAssert.DoesNotContain( format.Options, "Auto" );
    }

    /// <summary>
    /// Verifies that the "ExpandArchive" Format field includes Auto for
    /// extension-based format detection alongside Zip and TarGz.
    /// </summary>
    [TestMethod]
    public void ExpandArchive_Format_Is_Select_With_Auto( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["ExpandArchive"];
        FieldDescriptor format = desc.Fields[3];
        Assert.AreEqual( "Format", format.Name );
        Assert.AreEqual( FieldType.Select, format.Type );
        Assert.HasCount( 3, format.Options! );
        CollectionAssert.Contains( format.Options, "Zip" );
        CollectionAssert.Contains( format.Options, "TarGz" );
        CollectionAssert.Contains( format.Options, "Auto" );
    }

    /// <summary>
    /// Verifies that the "Delay" action's Seconds field is a required Number field.
    /// </summary>
    [TestMethod]
    public void Delay_Seconds_Is_Number_And_Required( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["Delay"];
        FieldDescriptor seconds = desc.Fields[0];
        Assert.AreEqual( "Seconds", seconds.Name );
        Assert.AreEqual( FieldType.Number, seconds.Type );
        Assert.IsTrue( seconds.Required );
    }

    /// <summary>
    /// Verifies that the "FindReplace" action's Encoding field defaults to "utf-8".
    /// </summary>
    [TestMethod]
    public void FindReplace_Encoding_Default_Is_Utf8( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["FindReplace"];
        FieldDescriptor? enc = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "Encoding") { enc = f; break; }
        }
        Assert.IsNotNull( enc );
        Assert.AreEqual( "utf-8", enc.DefaultValue );
    }

    /// <summary>
    /// Verifies that no duplicate keys exist in the registry.
    /// </summary>
    [TestMethod]
    public void No_Duplicate_Keys( ) {
        HashSet<string> keys = new( StringComparer.OrdinalIgnoreCase );
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            Assert.IsTrue(
                keys.Add( desc.Key ),
                $"Duplicate action key: {desc.Key}" );
        }
    }

    /// <summary>
    /// Verifies that the <see cref="ActionRegistry.Categories"/> list
    /// contains all expected category values.
    /// </summary>
    [TestMethod]
    [DataRow( "File" )]
    [DataRow( "Directory" )]
    [DataRow( "Archive" )]
    [DataRow( "Process" )]
    [DataRow( "ControlFlow" )]
    [DataRow( "File monitoring" )]
    [DataRow( "Iteration" )]
    [DataRow( "Network" )]
    [DataRow( "Data" )]
    [DataRow( "Shell" )]
    [DataRow( "PowerShell" )]
    public void Categories_Contains_Expected_Value( string category ) {
        CollectionAssert.Contains(
            (System.Collections.ICollection)ActionRegistry.Categories,
            category,
            $"Missing category: {category}" );
    }

    /// <summary>
    /// Verifies that every action listed in <see cref="ActionRegistry.Grouped"/>
    /// appears exactly once and that all actions are accounted for.
    /// </summary>
    [TestMethod]
    public void Grouped_Contains_All_Actions( ) {
        List<string> groupedKeys = [];
        foreach ((string _, IReadOnlyList<ActionFormDescriptor> actions) in ActionRegistry.Grouped) {
            foreach (ActionFormDescriptor desc in actions) {
                groupedKeys.Add( desc.Key );
            }
        }
        Assert.HasCount( ActionRegistry.All.Count, groupedKeys );
    }

    /// <summary>
    /// Verifies that the "HttpRequest" action has the expected fields
    /// for a complete HTTP request configuration.
    /// </summary>
    [TestMethod]
    public void HttpRequest_Has_Expected_Fields( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["HttpRequest"];
        Assert.AreEqual( "Url", desc.Fields[0].Name );
        Assert.IsTrue( desc.Fields[0].Required );
        Assert.AreEqual( "Method", desc.Fields[1].Name );
        Assert.AreEqual( FieldType.Select, desc.Fields[1].Type );
        Assert.AreEqual( "Headers", desc.Fields[2].Name );
        Assert.AreEqual( FieldType.KeyValueMap, desc.Fields[2].Type );
    }

    /// <summary>
    /// Verifies that the "TransformJson" action's Operations field is an
    /// <see cref="FieldType.ObjectArray"/> with sub-fields.
    /// </summary>
    [TestMethod]
    public void TransformJson_Operations_Is_ObjectArray_With_SubFields( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["TransformJson"];
        FieldDescriptor ops = desc.Fields[2];
        Assert.AreEqual( "Operations", ops.Name );
        Assert.AreEqual( FieldType.ObjectArray, ops.Type );
        Assert.IsNotNull( ops.SubFields );
        Assert.IsGreaterThanOrEqualTo( ops.SubFields.Count, 3 );
        Assert.AreEqual( "Type", ops.SubFields[0].Name );
        Assert.AreEqual( "Path", ops.SubFields[1].Name );
        Assert.AreEqual( "Value", ops.SubFields[2].Name );
    }

    /// <summary>
    /// Verifies that the "SendEmail" action's To field is a required
    /// <see cref="FieldType.StringArray"/>.
    /// </summary>
    [TestMethod]
    public void SendEmail_To_Is_Required_StringArray( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["SendEmail"];
        FieldDescriptor? toField = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "To") { toField = f; break; }
        }
        Assert.IsNotNull( toField );
        Assert.AreEqual( FieldType.StringArray, toField.Type );
        Assert.IsTrue( toField.Required );
    }

    // ── Category membership tests ──────────────────────────────────────

    /// <summary>
    /// Verifies that the "File" category contains eleven actions (original file ops +
    /// merged Content + GetFileInfo).
    /// </summary>
    [TestMethod]
    public void File_Category_Contains_Eleven_Actions( ) {
        IReadOnlyList<ActionFormDescriptor> fileActions = GetCategoryActions( "File" );
        Assert.HasCount( 11, fileActions );
        List<string> keys = [.. fileActions.Select( a => a.Key )];
        string[] expected = [
            "CopyFile", "MoveFile", "RenameFile", "DeleteFile", "CreateFile",
            "TestExists", "ClearContent", "WriteContent", "ReadContent",
            "FindReplace", "GetFileInfo"
        ];
        foreach (string key in expected) {
            Assert.Contains( key, keys, $"File category missing action: {key}" );
        }
    }

    /// <summary>
    /// Verifies that the "Directory" category contains two actions.
    /// </summary>
    [TestMethod]
    public void Directory_Category_Contains_Two_Actions( ) {
        IReadOnlyList<ActionFormDescriptor> actions = GetCategoryActions( "Directory" );
        Assert.HasCount( 2, actions );
        List<string> keys = [.. actions.Select( a => a.Key )];
        Assert.Contains( "CreateDirectory", keys );
        Assert.Contains( "ListDirectory", keys );
    }

    /// <summary>
    /// Verifies that the "Network" category contains six actions (original network ops +
    /// merged SendEmail, SendWebhook).
    /// </summary>
    [TestMethod]
    public void Network_Category_Contains_Six_Actions( ) {
        IReadOnlyList<ActionFormDescriptor> actions = GetCategoryActions( "Network" );
        Assert.HasCount( 6, actions );
        List<string> keys = [.. actions.Select( a => a.Key )];
        string[] expected = ["HttpRequest", "DownloadFile", "TestConnection", "UploadFile", "SendEmail", "SendWebhook"];
        foreach (string key in expected) {
            Assert.Contains( key, keys, $"Network category missing action: {key}" );
        }
    }

    /// <summary>
    /// Verifies that single-entry categories each have exactly one action.
    /// </summary>
    [TestMethod]
    [DataRow( "ControlFlow", "Delay" )]
    [DataRow( "File monitoring", "WatchFile" )]
    [DataRow( "Iteration", "ForEach" )]
    [DataRow( "Data", "TransformJson" )]
    public void SingleEntry_Categories_Have_One_Action( string category, string expectedKey ) {
        IReadOnlyList<ActionFormDescriptor> actions = GetCategoryActions( category );
        Assert.HasCount( 1, actions );
        Assert.AreEqual( expectedKey, actions[0].Key );
    }

    // ── ShowWhen conditional visibility tests ──────────────────────────

    /// <summary>
    /// Verifies that <see cref="FieldDescriptor.ShowWhen"/> is set only on fields that
    /// should be conditionally visible, and that the referenced field name exists
    /// in the same action's field list.
    /// </summary>
    [TestMethod]
    public void ShowWhen_References_Existing_Field_In_Same_Action( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            HashSet<string> fieldNames = new( desc.Fields.Select( f => f.Name ), StringComparer.Ordinal );
            foreach (FieldDescriptor field in desc.Fields) {
                if (field.ShowWhen is null) { continue; }
                string referencedField = field.ShowWhen.Split( '=' )[0];
                Assert.Contains(
                    referencedField,
                    fieldNames,
                    $"Action '{desc.Key}' field '{field.Name}' has ShowWhen referencing " +
                    $"non-existent field '{referencedField}'." );
            }
        }
    }

    /// <summary>
    /// Verifies that the StartProcess "TimeoutMs" field has a ShowWhen that
    /// references "WaitForExit=true".
    /// </summary>
    [TestMethod]
    public void StartProcess_TimeoutMs_ShowWhen_WaitForExit( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["StartProcess"];
        FieldDescriptor? timeout = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "TimeoutMs") { timeout = f; break; }
        }
        Assert.IsNotNull( timeout );
        Assert.AreEqual( "WaitForExit=true", timeout.ShowWhen );
    }

    /// <summary>
    /// Verifies that the HttpRequest "Body" and "ContentType" fields have ShowWhen
    /// referencing "Method=POST|PUT|PATCH|DELETE".
    /// </summary>
    [TestMethod]
    public void HttpRequest_Body_And_ContentType_ShowWhen_Method( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["HttpRequest"];
        FieldDescriptor? body = null;
        FieldDescriptor? contentType = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "Body") { body = f; }
            if (f.Name == "ContentType") { contentType = f; }
        }
        Assert.IsNotNull( body );
        Assert.IsNotNull( contentType );
        Assert.AreEqual( "Method=POST|PUT|PATCH|DELETE", body.ShowWhen );
        Assert.AreEqual( "Method=POST|PUT|PATCH|DELETE", contentType.ShowWhen );
    }

    /// <summary>
    /// Verifies that the TestConnection "ExpectedStatusCode" field has ShowWhen
    /// referencing "Protocol=Http|Https".
    /// </summary>
    [TestMethod]
    public void TestConnection_ExpectedStatusCode_ShowWhen_Protocol( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["TestConnection"];
        FieldDescriptor? expected = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "ExpectedStatusCode") { expected = f; break; }
        }
        Assert.IsNotNull( expected );
        Assert.AreEqual( "Protocol=Http|Https", expected.ShowWhen );
    }

    /// <summary>
    /// Verifies that the TransformJson Operations "Value" sub-field has ShowWhen
    /// referencing "Type=Set|Merge".
    /// </summary>
    [TestMethod]
    public void TransformJson_Value_SubField_ShowWhen_Type( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["TransformJson"];
        FieldDescriptor ops = desc.Fields[2];
        Assert.IsNotNull( ops.SubFields );
        FieldDescriptor? valueField = null;
        foreach (FieldDescriptor f in ops.SubFields) {
            if (f.Name == "Value") { valueField = f; break; }
        }
        Assert.IsNotNull( valueField );
        Assert.AreEqual( "Type=Set|Merge", valueField.ShowWhen );
    }

    // ── Min/Max constraint tests ───────────────────────────────────────

    /// <summary>
    /// Verifies that the TestConnection "Port" field has Min=1 and Max=65535.
    /// </summary>
    [TestMethod]
    public void TestConnection_Port_Has_Min_Max_Constraints( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["TestConnection"];
        FieldDescriptor? port = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "Port") { port = f; break; }
        }
        Assert.IsNotNull( port );
        Assert.AreEqual( 1.0, port.Min );
        Assert.AreEqual( 65535.0, port.Max );
    }

    /// <summary>
    /// Verifies that timeout fields with Min constraints have Min=1.
    /// </summary>
    [TestMethod]
    [DataRow( "HttpRequest", "TimeoutSeconds" )]
    [DataRow( "DownloadFile", "TimeoutSeconds" )]
    [DataRow( "TestConnection", "TimeoutSeconds" )]
    [DataRow( "UploadFile", "TimeoutSeconds" )]
    [DataRow( "SendWebhook", "TimeoutSeconds" )]
    public void Timeout_Fields_Have_Min_One( string actionKey, string fieldName ) {
        ActionFormDescriptor desc = ActionRegistry.Actions[actionKey];
        FieldDescriptor? field = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == fieldName) { field = f; break; }
        }
        Assert.IsNotNull( field, $"Action '{actionKey}' missing field '{fieldName}'." );
        Assert.AreEqual( 1.0, field.Min, $"'{actionKey}.{fieldName}' should have Min=1." );
    }

    // ── Field type coverage tests ──────────────────────────────────────

    /// <summary>
    /// Verifies that every <see cref="FieldType"/> enum value is used by at least one
    /// field across all registered actions, ensuring no dead field types exist.
    /// </summary>
    [TestMethod]
    public void All_FieldType_Values_Are_Used( ) {
        HashSet<FieldType> usedTypes = [];
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            foreach (FieldDescriptor field in desc.Fields) {
                _ = usedTypes.Add( field.Type );
                if (field.SubFields is not null) {
                    foreach (FieldDescriptor sub in field.SubFields) {
                        _ = usedTypes.Add( sub.Type );
                    }
                }
            }
        }
        foreach (FieldType ft in Enum.GetValues<FieldType>( )) {
            Assert.Contains(
                ft,
                usedTypes,
                $"FieldType.{ft} is not used by any registered action." );
        }
    }

    /// <summary>
    /// Verifies that the "HttpRequest" action's "ExpectedStatusCodes" field is an
    /// <see cref="FieldType.IntArray"/> with a default value of "[200]".
    /// </summary>
    [TestMethod]
    public void HttpRequest_ExpectedStatusCodes_Is_IntArray( ) {
        ActionFormDescriptor desc = ActionRegistry.Actions["HttpRequest"];
        FieldDescriptor? field = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "ExpectedStatusCodes") { field = f; break; }
        }
        Assert.IsNotNull( field );
        Assert.AreEqual( FieldType.IntArray, field.Type );
        Assert.AreEqual( "[200]", field.DefaultValue );
    }

    /// <summary>
    /// Verifies that every <see cref="FieldType.KeyValueMap"/> field has no Options set,
    /// since key-value maps do not use fixed option lists.
    /// </summary>
    [TestMethod]
    public void KeyValueMap_Fields_Have_No_Options( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            foreach (FieldDescriptor field in desc.Fields) {
                if (field.Type == FieldType.KeyValueMap) {
                    Assert.IsNull(
                        field.Options,
                        $"'{desc.Key}.{field.Name}' is KeyValueMap but has Options set." );
                }
            }
        }
    }

    // ── Per-action field count verification ────────────────────────────

    /// <summary>
    /// Verifies exact field counts for all 31 actions, ensuring no accidental
    /// additions or removals of field definitions.
    /// </summary>
    [TestMethod]
    [DataRow( "CopyFile", 4 )]
    [DataRow( "MoveFile", 3 )]
    [DataRow( "RenameFile", 3 )]
    [DataRow( "DeleteFile", 3 )]
    [DataRow( "CreateFile", 5 )]
    [DataRow( "CreateDirectory", 1 )]
    [DataRow( "TestExists", 2 )]
    [DataRow( "ClearContent", 1 )]
    [DataRow( "WriteContent", 4 )]
    [DataRow( "ReadContent", 3 )]
    [DataRow( "FindReplace", 6 )]
    [DataRow( "GetFileInfo", 1 )]
    [DataRow( "ListDirectory", 5 )]
    [DataRow( "CompressArchive", 6 )]
    [DataRow( "ExpandArchive", 4 )]
    [DataRow( "StartProcess", 5 )]
    [DataRow( "StopProcess", 3 )]
    [DataRow( "Delay", 2 )]
    [DataRow( "WatchFile", 7 )]
    [DataRow( "ForEach", 1 )]
    [DataRow( "HttpRequest", 13 )]
    [DataRow( "DownloadFile", 5 )]
    [DataRow( "TestConnection", 5 )]
    [DataRow( "UploadFile", 6 )]
    [DataRow( "SendEmail", 11 )]
    [DataRow( "SendWebhook", 4 )]
    [DataRow( "TransformJson", 3 )]
    [DataRow( "ShellCommand", 2 )]
    [DataRow( "ShellScript", 3 )]
    [DataRow( "PowerShellCommand", 2 )]
    [DataRow( "PowerShellScript", 3 )]
    public void Action_Has_Expected_Field_Count( string key, int expectedCount ) {
        ActionFormDescriptor desc = ActionRegistry.Actions[key];
        Assert.HasCount( expectedCount, desc.Fields,
            $"Action '{key}' expected {expectedCount} fields but had {desc.Fields.Count}." );
    }

    // ── Required-field correctness ─────────────────────────────────────

    /// <summary>
    /// Verifies that every action has at least one required field (all actions
    /// need at least a path, name, or primary parameter to be meaningful).
    /// </summary>
    [TestMethod]
    public void Every_Action_Has_At_Least_One_Required_Field( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            bool hasRequired = desc.Fields.Any( f => f.Required );
            Assert.IsTrue(
                hasRequired,
                $"Action '{desc.Key}' has no required fields — every action should have at least one." );
        }
    }

    /// <summary>
    /// Verifies that all <see cref="FieldType.Bool"/> fields have a non-null
    /// <see cref="FieldDescriptor.DefaultValue"/>, ensuring toggles always start
    /// in a defined state.
    /// </summary>
    [TestMethod]
    public void Bool_Fields_Have_Default_Values( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            foreach (FieldDescriptor field in desc.Fields) {
                if (field.Type == FieldType.Bool) {
                    Assert.IsNotNull(
                        field.DefaultValue,
                        $"'{desc.Key}.{field.Name}' is Bool but has no DefaultValue." );
                    Assert.IsTrue(
                        field.DefaultValue is "true" or "false",
                        $"'{desc.Key}.{field.Name}' Bool DefaultValue should be 'true' or 'false' " +
                        $"but was '{field.DefaultValue}'." );
                }
            }
        }
    }

    /// <summary>
    /// Verifies that each <see cref="FieldDescriptor.Name"/> within an action is unique
    /// (no duplicate field names within the same descriptor).
    /// </summary>
    [TestMethod]
    public void No_Duplicate_Field_Names_Within_Action( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            HashSet<string> seen = [];
            foreach (FieldDescriptor field in desc.Fields) {
                Assert.IsTrue(
                    seen.Add( field.Name ),
                    $"Action '{desc.Key}' has duplicate field name: '{field.Name}'." );
            }
        }
    }

    /// <summary>
    /// Verifies that every <see cref="FieldDescriptor"/> has a non-empty
    /// <see cref="FieldDescriptor.Label"/> so the UI can display a human-readable label.
    /// </summary>
    [TestMethod]
    public void Every_Field_Has_NonEmpty_Label( ) {
        foreach (ActionFormDescriptor desc in ActionRegistry.All) {
            foreach (FieldDescriptor field in desc.Fields) {
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace( field.Label ),
                    $"'{desc.Key}.{field.Name}' has an empty Label." );
            }
        }
    }

    // ── Encoding-bearing actions ───────────────────────────────────────

    /// <summary>
    /// Verifies that all actions with an Encoding field reference the shared
    /// <see cref="ActionRegistry.Encodings"/> array and default to "utf-8".
    /// </summary>
    [TestMethod]
    [DataRow( "CreateFile" )]
    [DataRow( "WriteContent" )]
    [DataRow( "ReadContent" )]
    [DataRow( "FindReplace" )]
    public void Encoding_Field_Uses_Shared_Array_And_Default( string actionKey ) {
        ActionFormDescriptor desc = ActionRegistry.Actions[actionKey];
        FieldDescriptor? enc = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "Encoding") { enc = f; break; }
        }
        Assert.IsNotNull( enc, $"Action '{actionKey}' missing Encoding field." );
        Assert.AreEqual( FieldType.Select, enc.Type );
        Assert.AreEqual( "utf-8", enc.DefaultValue );
        Assert.IsNotNull( enc.Options );
        Assert.HasCount( 9, enc.Options,
            $"'{actionKey}.Encoding' should have 9 encoding options." );
    }

    // ── Helper methods ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the actions in the given category from <see cref="ActionRegistry.Grouped"/>.
    /// </summary>
    private static IReadOnlyList<ActionFormDescriptor> GetCategoryActions( string category ) {
        foreach ((string cat, IReadOnlyList<ActionFormDescriptor> actions) in ActionRegistry.Grouped) {
            if (string.Equals( cat, category, StringComparison.OrdinalIgnoreCase )) {
                return actions;
            }
        }
        Assert.Fail( $"Category '{category}' not found in Grouped." );
        return []; // unreachable
    }
}
