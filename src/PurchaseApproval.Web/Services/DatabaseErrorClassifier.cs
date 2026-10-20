// Turns Entity Framework save failures into categories the pages can act on honestly.
//
// Why: a DbUpdateException can mean "someone else saved first" (concurrency), "a database rule
// rejected the row" (constraint) or "something is broken" (unexpected). Showing all three as
// "changed while you were working" misleads the user and hides real defects, so services ask
// this class first and only report a concurrency conflict when that is what happened.
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PurchaseApproval.Services;

public enum DatabaseFailureKind
{
    ConcurrencyConflict = 1,
    UniqueViolation = 2,
    ConstraintViolation = 3,
    Unexpected = 4
}

public sealed record DatabaseFailure(DatabaseFailureKind Kind, string? ConstraintName);

public static class DatabaseErrorClassifier
{
    // PostgreSQL SQLSTATE codes: https://www.postgresql.org/docs/current/errcodes-appendix.html
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    private const string NotNullViolation = "23502";
    private const string CheckViolation = "23514";

    public static DatabaseFailure Classify(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DbUpdateConcurrencyException)
        {
            return new DatabaseFailure(DatabaseFailureKind.ConcurrencyConflict, null);
        }

        if (exception.InnerException is PostgresException postgres)
        {
            return postgres.SqlState switch
            {
                UniqueViolation => new DatabaseFailure(DatabaseFailureKind.UniqueViolation, postgres.ConstraintName),
                ForeignKeyViolation or NotNullViolation or CheckViolation =>
                    new DatabaseFailure(DatabaseFailureKind.ConstraintViolation, postgres.ConstraintName),
                _ => new DatabaseFailure(DatabaseFailureKind.Unexpected, postgres.ConstraintName)
            };
        }

        return new DatabaseFailure(DatabaseFailureKind.Unexpected, null);
    }

    /// <summary>Generic, non-leaking wording for a rejected row; the constraint name is logged, not shown.</summary>
    public static LocalizableMessage DescribeConstraintViolation() =>
        "The database rejected this record because it conflicts with a data rule. Reload the page and check the entered values.";
}
