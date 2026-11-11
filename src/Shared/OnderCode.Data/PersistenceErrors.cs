using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OnderCode.Kernel.Exceptions;

namespace OnderCode.Data;

/// <summary>SQLite hatalarını kullanıcıya gösterilebilir alan istisnalarına çevirir.</summary>
public static class PersistenceErrors
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintForeignKey = 787;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;

    public static bool TryTranslate(DbUpdateException exception, [NotNullWhen(true)] out DomainException? translated)
    {
        translated = exception switch
        {
            DbUpdateConcurrencyException => new ConcurrencyConflictException(exception),
            { InnerException: SqliteException { SqliteErrorCode: SqliteConstraint } sqlite } => sqlite.SqliteExtendedErrorCode switch
            {
                SqliteConstraintUnique or SqliteConstraintPrimaryKey => new DuplicateRecordException(exception),
                SqliteConstraintForeignKey => new ReferenceInUseException(exception),
                _ => null,
            },
            _ => null,
        };
        return translated is not null;
    }
}
