using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Core.Workflows;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Workflows;

[TestClass]
public class ConditionEvaluatorTests {
    private ConditionEvaluator _evaluator = null!;

    [TestInitialize]
    public void TestInit( ) {
        _evaluator = new ConditionEvaluator( NullLogger<ConditionEvaluator>.Instance );
    }

    // ── Evaluate: null/empty expressions ──

    [TestMethod]
    public void Evaluate_NullExpression_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate( null, job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_EmptyExpression_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate( "", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_WhitespaceExpression_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate( "   ", job );
        Assert.IsTrue( result );
    }

    // ── Evaluate: $? -eq $true / $false ──

    [TestMethod]
    public void Evaluate_SuccessEqualsTrue_WhenJobSucceeded_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate( "$? -eq $true", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_SuccessEqualsTrue_WhenJobFailed_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate( "$? -eq $true", job );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void Evaluate_SuccessEqualsFalse_WhenJobFailed_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate( "$? -eq $false", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_SuccessEqualsFalse_WhenJobSucceeded_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate( "$? -eq $false", job );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void Evaluate_SuccessExpression_CaseInsensitive( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate( "$? -eq $TRUE", job );
        Assert.IsTrue( result );
    }

    // ── Evaluate: $exitCode comparisons ──

    [TestMethod]
    public void Evaluate_ExitCodeEqualsZero_WhenZero_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate( "$exitCode == 0", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_ExitCodeEqualsZero_WhenNonZero_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 42 };
        bool result = _evaluator.Evaluate( "$exitCode == 0", job );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void Evaluate_ExitCodeNotEqual_WhenDifferent_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate( "$exitCode != 0", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_ExitCodeGreaterThan_WhenGreater_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 5 };
        bool result = _evaluator.Evaluate( "$exitCode > 3", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_ExitCodeGreaterThan_WhenEqual_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 3 };
        bool result = _evaluator.Evaluate( "$exitCode > 3", job );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void Evaluate_ExitCodeLessThan_WhenLess_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate( "$exitCode < 1", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_ExitCodeGreaterOrEqual_WhenEqual_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 5 };
        bool result = _evaluator.Evaluate( "$exitCode >= 5", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_ExitCodeLessOrEqual_WhenLess_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 2 };
        bool result = _evaluator.Evaluate( "$exitCode <= 5", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_ExitCodeNegativeValue_Match( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = -1 };
        bool result = _evaluator.Evaluate( "$exitCode == -1", job );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void Evaluate_NullExitCode_TreatedAsZero( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = null };
        bool result = _evaluator.Evaluate( "$exitCode == 0", job );
        Assert.IsTrue( result );
    }

    // ── Evaluate: Unknown expressions ──

    [TestMethod]
    public void Evaluate_UnknownExpression_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate( "some unknown thing", job );
        Assert.IsFalse( result );
    }

    // ── EvaluateMultiple ──

    [TestMethod]
    public void EvaluateMultiple_NullExpression_ReturnsTrue( ) {
        List<WerkrJob> jobs = [new( ) { Success = false, ExitCode = 1 }];
        bool result = _evaluator.EvaluateMultiple( null, jobs, DependencyMode.All );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void EvaluateMultiple_EmptyPredecessors_EvaluatesAgainstDefault( ) {
        // Default: Success=true, ExitCode=0
        bool result = _evaluator.EvaluateMultiple( "$? -eq $true", [], DependencyMode.All );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void EvaluateMultiple_AllMode_AllMustMatch( ) {
        List<WerkrJob> jobs = [
            new( ) { Success = true, ExitCode = 0 },
            new( ) { Success = true, ExitCode = 0 },
        ];
        bool result = _evaluator.EvaluateMultiple( "$? -eq $true", jobs, DependencyMode.All );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void EvaluateMultiple_AllMode_OneFails_ReturnsFalse( ) {
        List<WerkrJob> jobs = [
            new( ) { Success = true, ExitCode = 0 },
            new( ) { Success = false, ExitCode = 1 },
        ];
        bool result = _evaluator.EvaluateMultiple( "$? -eq $true", jobs, DependencyMode.All );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public void EvaluateMultiple_AnyMode_OnePasses_ReturnsTrue( ) {
        List<WerkrJob> jobs = [
            new( ) { Success = true, ExitCode = 0 },
            new( ) { Success = false, ExitCode = 1 },
        ];
        bool result = _evaluator.EvaluateMultiple( "$? -eq $true", jobs, DependencyMode.Any );
        Assert.IsTrue( result );
    }

    [TestMethod]
    public void EvaluateMultiple_AnyMode_NonePass_ReturnsFalse( ) {
        List<WerkrJob> jobs = [
            new( ) { Success = false, ExitCode = 1 },
            new( ) { Success = false, ExitCode = 2 },
        ];
        bool result = _evaluator.EvaluateMultiple( "$? -eq $true", jobs, DependencyMode.Any );
        Assert.IsFalse( result );
    }
}
