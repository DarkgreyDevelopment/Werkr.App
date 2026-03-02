using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Core.Communication;
using Werkr.Core.Tasks;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Tests.Data.Unit.Tasks;

[TestClass]
public class SuccessCriteriaEvaluatorTests {
    private SuccessCriteriaEvaluator _evaluator = null!;

    [TestInitialize]
    public void TestInit( ) {
        _evaluator = new SuccessCriteriaEvaluator( NullLogger<SuccessCriteriaEvaluator>.Instance );
    }

    // ── Default Criteria ──

    [TestMethod]
    public void DefaultShellCommand_ExitCodeZero_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, null, exitCode: 0, output: [], exception: null );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void DefaultShellCommand_ExitCodeNonZero_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, null, exitCode: 1, output: [], exception: null );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void DefaultShellCommand_NullExitCode_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, null, exitCode: null, output: [], exception: null );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void DefaultPwshCommand_NoErrors_Succeeds( ) {
        List<OperatorOutput> output = [
            OperatorOutput.Create( "Information", "Hello World" ),
        ];
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand, null, exitCode: null, output: output, exception: null );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void DefaultPwshCommand_WithErrorOutput_Fails( ) {
        List<OperatorOutput> output = [
            OperatorOutput.Create( "Information", "Starting..." ),
            OperatorOutput.Create( "Error", "Something went wrong" ),
        ];
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand, null, exitCode: null, output: output, exception: null );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void DefaultAction_AlwaysSucceeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.Action, null, exitCode: null, output: [], exception: null );
        Assert.IsTrue( result );
    }

    // ── Exception Handling ──

    [TestMethod]
    public void Exception_AlwaysFails_UnlessCriteriaIsAlways( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, null, exitCode: 0, output: [],
            exception: new InvalidOperationException( "test" ) );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void Exception_WithAlwaysCriteria_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, "always", exitCode: null, output: [],
            exception: new InvalidOperationException( "test" ) );
        Assert.IsTrue( result );
    }

    // ── Explicit Criteria: exitCode == 0 ──

    [TestMethod]
    public void ExitCodeCriteria_Zero_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand, "exitCode == 0", exitCode: 0, output: [], exception: null );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void ExitCodeCriteria_NonZero_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand, "exitCode == 0", exitCode: 42, output: [], exception: null );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void ExitCodeCriteria_Null_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand, "exitCode == 0", exitCode: null, output: [], exception: null );
        Assert.IsFalse( result );
    }

    // ── Explicit Criteria: pwsh.HadErrors == false ──

    [TestMethod]
    public void PwshHadErrorsCriteria_NoErrors_Succeeds( ) {
        List<OperatorOutput> output = [
            OperatorOutput.Create( "Information", "OK" ),
        ];
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand, "pwsh.HadErrors == false",
            exitCode: null, output: output, exception: null );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void PwshHadErrorsCriteria_WithErrors_Fails( ) {
        List<OperatorOutput> output = [
            OperatorOutput.Create( "Error", "bad" ),
        ];
        bool result = _evaluator.Evaluate(
            TaskActionType.PowerShellCommand, "pwsh.HadErrors == false",
            exitCode: null, output: output, exception: null );
        Assert.IsFalse( result );
    }

    // ── Explicit Criteria: output.contains ──

    [TestMethod]
    public void OutputContainsCriteria_Found_Succeeds( ) {
        List<OperatorOutput> output = [
            OperatorOutput.Create( "Information", "Build succeeded" ),
        ];
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, "output.contains(\"Build succeeded\")",
            exitCode: null, output: output, exception: null );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void OutputContainsCriteria_NotFound_Fails( ) {
        List<OperatorOutput> output = [
            OperatorOutput.Create( "Information", "Build failed" ),
        ];
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, "output.contains(\"Build succeeded\")",
            exitCode: null, output: output, exception: null );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void OutputContainsCriteria_CaseInsensitive( ) {
        List<OperatorOutput> output = [
            OperatorOutput.Create( "Information", "BUILD SUCCEEDED" ),
        ];
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, "output.contains(\"build succeeded\")",
            exitCode: null, output: output, exception: null );
        Assert.IsTrue( result );
    }

    // ── Explicit Criteria: always ──

    [TestMethod]
    public void AlwaysCriteria_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, "always", exitCode: 99, output: [], exception: null );
        Assert.IsTrue( result );
    }

    // ── Unknown Criteria ──

    [TestMethod]
    public void UnknownCriteria_NullExitCode_Succeeds( ) {
        // Unknown criteria falls back to exitCode is null or 0
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, "someUnknownExpression",
            exitCode: null, output: [], exception: null );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void UnknownCriteria_ZeroExitCode_Succeeds( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, "someUnknownExpression",
            exitCode: 0, output: [], exception: null );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void UnknownCriteria_NonZeroExitCode_Fails( ) {
        bool result = _evaluator.Evaluate(
            TaskActionType.ShellCommand, "someUnknownExpression",
            exitCode: 1, output: [], exception: null );
        Assert.IsFalse( result );
    }

    // ── DescribeEffectiveCriteria ──

    [TestMethod]
    public void DescribeEffective_ExplicitCriteria_ReturnsCriteria( ) {
        string result = SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
            TaskActionType.ShellCommand, "exitCode == 0" );
        Assert.AreEqual( "exitCode == 0", result );
    }

    [TestMethod]
    public void DescribeEffective_ShellDefault_ReturnsExitCodeDefault( ) {
        string result = SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
            TaskActionType.ShellCommand, null );
        Assert.Contains( "exitCode == 0", result );
    }

    [TestMethod]
    public void DescribeEffective_PwshDefault_ReturnsHadErrorsDefault( ) {
        string result = SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
            TaskActionType.PowerShellCommand, null );
        Assert.Contains( "pwsh.HadErrors == false", result );
    }

    [TestMethod]
    public void DescribeEffective_ActionDefault_ReturnsAlways( ) {
        string result = SuccessCriteriaEvaluator.DescribeEffectiveCriteria(
            TaskActionType.Action, null );
        Assert.Contains( "always", result );
    }
}
