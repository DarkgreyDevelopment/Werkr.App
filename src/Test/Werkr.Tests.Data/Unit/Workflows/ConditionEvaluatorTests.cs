using Werkr.Core.Workflows;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Workflows;

/// <summary>
/// Contains unit tests for the <see cref="ConditionEvaluator"/> class defined in Werkr.Core. Validates
/// null/empty/whitespace expressions, success expressions, exit code comparisons, unknown expressions, and multi-job
/// evaluation with All/Any dependency modes.
/// </summary>
[TestClass]
public class ConditionEvaluatorTests {
    /// <summary>
    /// The <see cref="ConditionEvaluator"/> instance under test.
    /// </summary>
    private ConditionEvaluator _evaluator = null!;

    /// <summary>
    /// Creates a new <see cref="ConditionEvaluator"/> with a null logger.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _evaluator = new ConditionEvaluator( NullLogger<ConditionEvaluator>.Instance );
    }

    // ── Evaluate: null/empty expressions ──

    /// <summary>
    /// Verifies that a <see langword="null"/> expression evaluates to <see langword="true"/> (unconditional).
    /// </summary>
    [TestMethod]
    public void Evaluate_NullExpression_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate(
            null,
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that an empty string expression evaluates to <see langword="true"/>.
    /// </summary>
    [TestMethod]
    public void Evaluate_EmptyExpression_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate(
            "",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that a whitespace-only expression evaluates to <see langword="true"/>.
    /// </summary>
    [TestMethod]
    public void Evaluate_WhitespaceExpression_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate(
            "   ",
            job
        );
        Assert.IsTrue( result );
    }

    // ── Evaluate: $? -eq $true / $false ──

    /// <summary>
    /// Verifies that "$? -eq $true" returns <see langword="true"/> when the job succeeded.
    /// </summary>
    [TestMethod]
    public void Evaluate_SuccessEqualsTrue_WhenJobSucceeded_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate(
            "$? -eq $true",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that "$? -eq $true" returns <see langword="false"/> when the job failed.
    /// </summary>
    [TestMethod]
    public void Evaluate_SuccessEqualsTrue_WhenJobFailed_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate(
            "$? -eq $true",
            job
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that "$? -eq $false" returns <see langword="true"/> when the job failed.
    /// </summary>
    [TestMethod]
    public void Evaluate_SuccessEqualsFalse_WhenJobFailed_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate(
            "$? -eq $false",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that "$? -eq $false" returns <see langword="false"/> when the job succeeded.
    /// </summary>
    [TestMethod]
    public void Evaluate_SuccessEqualsFalse_WhenJobSucceeded_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate(
            "$? -eq $false",
            job
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that success expressions are evaluated case-insensitively.
    /// </summary>
    [TestMethod]
    public void Evaluate_SuccessExpression_CaseInsensitive( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate(
            "$? -eq $TRUE",
            job
        );
        Assert.IsTrue( result );
    }

    // ── Evaluate: $exitCode comparisons ──

    /// <summary>
    /// Verifies that "$exitCode == 0" returns <see langword="true"/> when exit code is zero.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeEqualsZero_WhenZero_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate(
            "$exitCode == 0",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that "$exitCode == 0" returns <see langword="false"/> when exit code is non-zero.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeEqualsZero_WhenNonZero_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 42 };
        bool result = _evaluator.Evaluate(
            "$exitCode == 0",
            job
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that "$exitCode != 0" returns <see langword="true"/> when exit code differs.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeNotEqual_WhenDifferent_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 1 };
        bool result = _evaluator.Evaluate(
            "$exitCode != 0",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the greater-than operator returns <see langword="true"/> when exit code exceeds the threshold.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeGreaterThan_WhenGreater_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 5 };
        bool result = _evaluator.Evaluate(
            "$exitCode > 3",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the greater-than operator returns <see langword="false"/> when exit code equals the threshold.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeGreaterThan_WhenEqual_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = 3 };
        bool result = _evaluator.Evaluate(
            "$exitCode > 3",
            job
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that the less-than operator returns <see langword="true"/> when exit code is less than the threshold.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeLessThan_WhenLess_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate(
            "$exitCode < 1",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the greater-or-equal operator returns <see langword="true"/> when exit code equals the threshold.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeGreaterOrEqual_WhenEqual_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 5 };
        bool result = _evaluator.Evaluate(
            "$exitCode >= 5",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that the less-or-equal operator returns <see langword="true"/> when exit code is less than the
    /// threshold.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeLessOrEqual_WhenLess_ReturnsTrue( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 2 };
        bool result = _evaluator.Evaluate(
            "$exitCode <= 5",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that negative exit code values are matched correctly.
    /// </summary>
    [TestMethod]
    public void Evaluate_ExitCodeNegativeValue_Match( ) {
        WerkrJob job = new( ) { Success = false, ExitCode = -1 };
        bool result = _evaluator.Evaluate(
            "$exitCode == -1",
            job
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that a <see langword="null"/> exit code is treated as zero for comparison.
    /// </summary>
    [TestMethod]
    public void Evaluate_NullExitCode_TreatedAsZero( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = null };
        bool result = _evaluator.Evaluate(
            "$exitCode == 0",
            job
        );
        Assert.IsTrue( result );
    }

    // ── Evaluate: Unknown expressions ──

    /// <summary>
    /// Verifies that an unrecognized expression returns <see langword="false"/>.
    /// </summary>
    [TestMethod]
    public void Evaluate_UnknownExpression_ReturnsFalse( ) {
        WerkrJob job = new( ) { Success = true, ExitCode = 0 };
        bool result = _evaluator.Evaluate(
            "some unknown thing",
            job
        );
        Assert.IsFalse( result );
    }

    // ── EvaluateMultiple ──

    /// <summary>
    /// Verifies that <see cref="EvaluateMultiple"/> with a <see langword="null"/> expression returns <see
    /// langword="true"/>.
    /// </summary>
    [TestMethod]
    public void EvaluateMultiple_NullExpression_ReturnsTrue( ) {
        List<WerkrJob> jobs = [new( ) { Success = false, ExitCode = 1 }];
        bool result = _evaluator.EvaluateMultiple(
            null,
            jobs,
            DependencyMode.All
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that <see cref="EvaluateMultiple"/> with no predecessors evaluates against a default successful job.
    /// </summary>
    [TestMethod]
    public void EvaluateMultiple_EmptyPredecessors_EvaluatesAgainstDefault( ) {
        // Default: Success=true, ExitCode=0
        bool result = _evaluator.EvaluateMultiple(
            "$? -eq $true",
            [],
            DependencyMode.All
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that <see cref="DependencyMode.All"/> requires all predecessors to match the expression.
    /// </summary>
    [TestMethod]
    public void EvaluateMultiple_AllMode_AllMustMatch( ) {
        List<WerkrJob> jobs = [ new( ) { Success = true, ExitCode = 0 }, new( ) { Success = true, ExitCode = 0 }, ];
        bool result = _evaluator.EvaluateMultiple(
            "$? -eq $true",
            jobs,
            DependencyMode.All
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that <see cref="DependencyMode.All"/> returns <see langword="false"/> when one predecessor fails.
    /// </summary>
    [TestMethod]
    public void EvaluateMultiple_AllMode_OneFails_ReturnsFalse( ) {
        List<WerkrJob> jobs = [ new( ) { Success = true, ExitCode = 0 }, new( ) { Success = false, ExitCode = 1 }, ];
        bool result = _evaluator.EvaluateMultiple(
            "$? -eq $true",
            jobs,
            DependencyMode.All
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that <see cref="DependencyMode.Any"/> returns <see langword="true"/> when at least one predecessor
    /// passes.
    /// </summary>
    [TestMethod]
    public void EvaluateMultiple_AnyMode_OnePasses_ReturnsTrue( ) {
        List<WerkrJob> jobs = [ new( ) { Success = true, ExitCode = 0 }, new( ) { Success = false, ExitCode = 1 }, ];
        bool result = _evaluator.EvaluateMultiple(
            "$? -eq $true",
            jobs,
            DependencyMode.Any
        );
        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that <see cref="DependencyMode.Any"/> returns <see langword="false"/> when no predecessors pass.
    /// </summary>
    [TestMethod]
    public void EvaluateMultiple_AnyMode_NonePass_ReturnsFalse( ) {
        List<WerkrJob> jobs = [ new( ) { Success = false, ExitCode = 1 }, new( ) { Success = false, ExitCode = 2 }, ];
        bool result = _evaluator.EvaluateMultiple(
            "$? -eq $true",
            jobs,
            DependencyMode.Any
        );
        Assert.IsFalse( result );
    }
}
