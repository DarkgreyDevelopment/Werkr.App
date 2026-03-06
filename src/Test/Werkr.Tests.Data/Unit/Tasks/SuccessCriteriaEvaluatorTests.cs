using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Core.Communication;
using Werkr.Core.Tasks;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Tests.Data.Unit.Tasks;

/// <summary>
/// Contains unit tests for the <see cref="SuccessCriteriaEvaluator"/> class defined in Werkr.Core. Validates default
/// success criteria per action type, exit code expressions, PowerShell error checks, output-contains matching, the
/// "always" criteria, unknown criteria fallback, and description generation.
/// </summary>
[TestClass]
public class SuccessCriteriaEvaluatorTests {
    /// <summary>
    /// The <see cref="SuccessCriteriaEvaluator"/> instance under test.
    /// </summary>
    private SuccessCriteriaEvaluator _evaluator = null!;

    /// <summary>
    /// Creates a new <see cref="SuccessCriteriaEvaluator"/> with a null logger.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _evaluator = new SuccessCriteriaEvaluator( NullLogger<SuccessCriteriaEvaluator>.Instance );
    }

    // ── Default Criteria ──

    /// <summary>
    /// Verifies that the default shell command criterion succeeds when exit code is zero.
    /// </summary>
    [TestMethod]
    public void DefaultShellCommand_ExitCodeZero_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            null,
            exitCode: 0,
            output: [],
            exception: null
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the default shell command criterion fails when exit code is non-zero.
    /// </summary>
    [TestMethod]
    public void DefaultShellCommand_ExitCodeNonZero_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            null,
            exitCode: 1,
            output: [],
            exception: null
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that the default shell command criterion fails when exit code is <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public void DefaultShellCommand_NullExitCode_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            null,
            exitCode: null,
            output: [],
            exception: null
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that the default PowerShell command criterion succeeds when output has no errors.
    /// </summary>
    [TestMethod]
    public void DefaultPwshCommand_NoErrors_Succeeds( ) {
        List<OperatorOutput> output = [ OperatorOutput.Create(
            "Information",
            "Hello World"
        ), ];
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand,
            null,
            exitCode: null,
            output: output,
            exception: null
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the default PowerShell command criterion fails when output contains error-level entries.
    /// </summary>
    [TestMethod]
    public void DefaultPwshCommand_WithErrorOutput_Fails( ) {
        List<OperatorOutput> output = [ OperatorOutput.Create(
            "Information",
            "Starting..."
        ), OperatorOutput.Create(
            "Error",
            "Something went wrong"
        ), ];
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand,
            null,
            exitCode: null,
            output: output,
            exception: null
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that the default Action type criterion always succeeds.
    /// </summary>
    [TestMethod]
    public void DefaultAction_AlwaysSucceeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.Action,
            null,
            exitCode: null,
            output: [],
            exception: null
        );
        Assert.IsTrue( result );
    }

    // ── Exception Handling ──

    /// <summary>
    /// Verifies that an exception causes failure unless the criteria expression is "always".
    /// </summary>
    [TestMethod]
    public void Exception_AlwaysFails_UnlessCriteriaIsAlways( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            null,
            exitCode: 0,
            output: [],
            exception: new InvalidOperationException( "test" )
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that the "always" criteria expression causes success even when an exception occurred.
    /// </summary>
    [TestMethod]
    public void Exception_WithAlwaysCriteria_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            "always",
            exitCode: null,
            output: [],
            exception: new InvalidOperationException( "test" )
        );
        Assert.IsTrue( result );
    }

    // ── Explicit Criteria: exitCode == 0 ──

    /// <summary>
    /// Verifies that the "exitCode == 0" expression succeeds when exit code is zero.
    /// </summary>
    [TestMethod]
    public void ExitCodeCriteria_Zero_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand,
            "exitCode == 0",
            exitCode: 0,
            output: [],
            exception: null
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the "exitCode == 0" expression fails when exit code is non-zero.
    /// </summary>
    [TestMethod]
    public void ExitCodeCriteria_NonZero_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand,
            "exitCode == 0",
            exitCode: 42,
            output: [],
            exception: null
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that the "exitCode == 0" expression fails when exit code is <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public void ExitCodeCriteria_Null_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand,
            "exitCode == 0",
            exitCode: null,
            output: [],
            exception: null
        );
        Assert.IsFalse( result );
    }

    // ── Explicit Criteria: pwsh.HadErrors == false ──

    /// <summary>
    /// Verifies that the "pwsh.HadErrors == false" expression succeeds when output has no error entries.
    /// </summary>
    [TestMethod]
    public void PwshHadErrorsCriteria_NoErrors_Succeeds( ) {
        List<OperatorOutput> output = [ OperatorOutput.Create(
            "Information",
            "OK"
        ), ];
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand,
            "pwsh.HadErrors == false",
            exitCode: null,
            output: output,
            exception: null
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the "pwsh.HadErrors == false" expression fails when output contains error entries.
    /// </summary>
    [TestMethod]
    public void PwshHadErrorsCriteria_WithErrors_Fails( ) {
        List<OperatorOutput> output = [ OperatorOutput.Create(
            "Error",
            "bad"
        ), ];
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand,
            "pwsh.HadErrors == false",
            exitCode: null,
            output: output,
            exception: null
        );
        Assert.IsFalse( result );
    }

    // ── Explicit Criteria: output.contains ──

    /// <summary>
    /// Verifies that the output.contains expression succeeds when the search text is found.
    /// </summary>
    [TestMethod]
    public void OutputContainsCriteria_Found_Succeeds( ) {
        List<OperatorOutput> output = [ OperatorOutput.Create(
            "Information",
            "Build succeeded"
        ), ];
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            "output.contains(\"Build succeeded\")",
            exitCode: null,
            output: output,
            exception: null
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the output.contains expression fails when the search text is not found.
    /// </summary>
    [TestMethod]
    public void OutputContainsCriteria_NotFound_Fails( ) {
        List<OperatorOutput> output = [ OperatorOutput.Create(
            "Information",
            "Build failed"
        ), ];
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            "output.contains(\"Build succeeded\")",
            exitCode: null,
            output: output,
            exception: null
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that the output.contains expression is case-insensitive.
    /// </summary>
    [TestMethod]
    public void OutputContainsCriteria_CaseInsensitive( ) {
        List<OperatorOutput> output = [ OperatorOutput.Create(
            "Information",
            "BUILD SUCCEEDED"
        ), ];
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            "output.contains(\"build succeeded\")",
            exitCode: null,
            output: output,
            exception: null
        );
        Assert.IsTrue( result );
    }

    // ── Explicit Criteria: always ──

    /// <summary>
    /// Verifies that the "always" criteria expression always succeeds regardless of exit code.
    /// </summary>
    [TestMethod]
    public void AlwaysCriteria_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            "always",
            exitCode: 99,
            output: [],
            exception: null
        );
        Assert.IsTrue( result );
    }

    // ── Unknown Criteria ──

    /// <summary>
    /// Verifies that an unknown criteria expression succeeds when exit code is <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public void UnknownCriteria_NullExitCode_Succeeds( ) {
        // Unknown criteria falls back to exitCode is null or 0
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            "someUnknownExpression",
            exitCode: null,
            output: [],
            exception: null
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that an unknown criteria expression succeeds when exit code is zero.
    /// </summary>
    [TestMethod]
    public void UnknownCriteria_ZeroExitCode_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            "someUnknownExpression",
            exitCode: 0,
            output: [],
            exception: null
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that an unknown criteria expression fails when exit code is non-zero.
    /// </summary>
    [TestMethod]
    public void UnknownCriteria_NonZeroExitCode_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand,
            "someUnknownExpression",
            exitCode: 1,
            output: [],
            exception: null
        );
        Assert.IsFalse( result );
    }

    // ── DescribeEffectiveCriteria ──

    /// <summary>
    /// Verifies that <see cref="DescribeEffectiveCriteria"/> returns the explicit expression when one is provided.
    /// </summary>
    [TestMethod]
    public void DescribeEffective_ExplicitCriteria_ReturnsCriteria( ) {
        string result = SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
            TaskActionType.ShellCommand,
            "exitCode == 0"
        );
        Assert.AreEqual(
            "exitCode == 0",
            result
        );
    }

    /// <summary>
    /// Verifies that the default description for <see cref="ShellCommand"/> contains "exitCode == 0".
    /// </summary>
    [TestMethod]
    public void DescribeEffective_ShellDefault_ReturnsExitCodeDefault( ) {
        string result = SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
            TaskActionType.ShellCommand,
            null
        );
        Assert.Contains(
            "exitCode == 0",
            result
        );
    }

    /// <summary>
    /// Verifies that the default description for <see cref="PowerShellCommand"/> contains "pwsh.HadErrors == false".
    /// </summary>
    [TestMethod]
    public void DescribeEffective_PwshDefault_ReturnsHadErrorsDefault( ) {
        string result = SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
            TaskActionType.PowerShellCommand,
            null
        );
        Assert.Contains(
            "pwsh.HadErrors == false",
            result
        );
    }

    /// <summary>
    /// Verifies that the default description for <see cref="Action"/> contains "always".
    /// </summary>
    [TestMethod]
    public void DescribeEffective_ActionDefault_ReturnsAlways( ) {
        string result = SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
            TaskActionType.Action,
            null
        );
        Assert.Contains(
            "always",
            result
        );
    }
}
